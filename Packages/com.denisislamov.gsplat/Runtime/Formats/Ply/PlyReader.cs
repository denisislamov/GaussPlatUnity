using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Decodes a 3DGS-style .ply (the format the original Gaussian Splatting code and most trainers write)
    /// into a <see cref="SplatCloud"/>. Property names: x y z, scale_0..2 (log scale), rot_0..3 (w x y z!),
    /// opacity (a logit: sigmoid applied here), f_dc_0..2 (SH degree 0), f_rest_0..44 (higher SH, stored
    /// channel-major: all coefficients of R, then G, then B - reordered to the SPZ interleaved order here).
    /// </summary>
    public static class PlyReader
    {
        private static readonly string[] ShRestNames = BuildShRestNames();

        public static PlyHeader ReadHeader(byte[] bytes)
        {
            return PlyHeader.Parse(bytes);
        }

        /// <summary>SH degree implied by the number of f_rest properties; 0 when there are none.</summary>
        public static int ShDegreeOf(PlyHeader header)
        {
            int restCount = header.CountShRestProperties();
            if (restCount % 3 != 0) throw new PlyException(PlyError.InvalidValue, $"PLY has {restCount} f_rest properties, which is not a multiple of 3.");

            int degree = ShMath.DegreeForCoefficientCount(restCount / 3);
            if (degree < 0) throw new PlyException(PlyError.InvalidValue, $"PLY has {restCount} f_rest properties, which matches no SH degree (expected 0, 9, 24 or 45).");
            return math.min(degree, ShMath.MaxDegree);
        }

        public static SplatCloud Read(byte[] bytes, Allocator allocator = Allocator.Persistent)
        {
            PlyHeader header = ReadHeader(bytes);
            var cloud = new SplatCloud(header.VertexCount, ShDegreeOf(header), false, allocator);
            try
            {
                Decode(bytes, header, cloud);
            }
            catch
            {
                cloud.Dispose();
                throw;
            }

            return cloud;
        }

        /// <summary>Fills a cloud allocated by the caller. Safe on a worker thread (reads managed memory, writes NativeArrays).</summary>
        public static void Decode(byte[] bytes, PlyHeader header, SplatCloud cloud)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (cloud == null) throw new ArgumentNullException(nameof(cloud));
            if (cloud.Count != header.VertexCount) throw new ArgumentException($"The cloud holds {cloud.Count} splats but the header says {header.VertexCount}.", nameof(cloud));

            var layout = new VertexLayout(header, cloud.ShDegree);
            if (header.Format == PlyFormat.BinaryLittleEndian)
            {
                DecodeBinary(bytes, header, layout, cloud);
            }
            else
            {
                DecodeAscii(bytes, header, layout, cloud);
            }
        }

        /// <summary>Which property feeds which attribute, resolved once per file instead of once per vertex.</summary>
        private sealed class VertexLayout
        {
            public readonly PlyProperty X, Y, Z;
            public readonly PlyProperty ScaleX, ScaleY, ScaleZ;
            public readonly PlyProperty RotW, RotX, RotY, RotZ;
            public readonly PlyProperty Opacity;
            public readonly PlyProperty ColorR, ColorG, ColorB;

            /// <summary>f_rest properties in file order (channel-major); length = fileCoefficients * 3.</summary>
            public readonly PlyProperty[] ShRest;

            /// <summary>Coefficients per channel in the file (may exceed what the cloud keeps).</summary>
            public readonly int FileShCoefficients;

            public VertexLayout(PlyHeader header, int keptShDegree)
            {
                X = header.GetRequiredProperty("x");
                Y = header.GetRequiredProperty("y");
                Z = header.GetRequiredProperty("z");
                ScaleX = header.GetRequiredProperty("scale_0");
                ScaleY = header.GetRequiredProperty("scale_1");
                ScaleZ = header.GetRequiredProperty("scale_2");
                RotW = header.GetRequiredProperty("rot_0");
                RotX = header.GetRequiredProperty("rot_1");
                RotY = header.GetRequiredProperty("rot_2");
                RotZ = header.GetRequiredProperty("rot_3");
                Opacity = header.GetRequiredProperty("opacity");
                ColorR = header.GetRequiredProperty("f_dc_0");
                ColorG = header.GetRequiredProperty("f_dc_1");
                ColorB = header.GetRequiredProperty("f_dc_2");

                int restCount = header.CountShRestProperties();
                if (restCount > ShRestNames.Length)
                {
                    throw new PlyException(PlyError.InvalidValue, $"PLY has {restCount} f_rest properties; SH degree 4 (72) is the most the format defines.");
                }

                FileShCoefficients = restCount / 3;
                ShRest = new PlyProperty[restCount];
                for (int restIndex = 0; restIndex < restCount; restIndex++)
                {
                    ShRest[restIndex] = header.GetRequiredProperty(ShRestNames[restIndex]);
                }
            }
        }

        private static void DecodeBinary(byte[] bytes, PlyHeader header, VertexLayout layout, SplatCloud cloud)
        {
            long needed = header.BodyOffset + (long)header.VertexStride * header.VertexCount;
            if (bytes.Length < needed)
            {
                throw new PlyException(PlyError.TruncatedPayload, $"The PLY body needs {needed} bytes for {header.VertexCount} vertices but the file has {bytes.Length}. The file is probably cut off.");
            }

            int keptCoefficients = cloud.ShCoefficientCount;
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                int vertexOffset = header.BodyOffset + splatIndex * header.VertexStride;
                ReadOnlySpan<byte> vertex = new ReadOnlySpan<byte>(bytes, vertexOffset, header.VertexStride);

                cloud.Positions[splatIndex] = new float3(ReadScalar(vertex, layout.X), ReadScalar(vertex, layout.Y), ReadScalar(vertex, layout.Z));
                cloud.LogScales[splatIndex] = new float3(ReadScalar(vertex, layout.ScaleX), ReadScalar(vertex, layout.ScaleY), ReadScalar(vertex, layout.ScaleZ));
                cloud.Rotations[splatIndex] = NormalizeRotation(ReadScalar(vertex, layout.RotX), ReadScalar(vertex, layout.RotY), ReadScalar(vertex, layout.RotZ), ReadScalar(vertex, layout.RotW));
                cloud.Alphas[splatIndex] = Sigmoid(ReadScalar(vertex, layout.Opacity));
                cloud.Colors[splatIndex] = new float3(ReadScalar(vertex, layout.ColorR), ReadScalar(vertex, layout.ColorG), ReadScalar(vertex, layout.ColorB));

                int shBase = splatIndex * cloud.ShFloatsPerSplat;
                for (int coefficient = 0; coefficient < keptCoefficients; coefficient++)
                {
                    for (int channel = 0; channel < 3; channel++)
                    {
                        // File: f_rest_[channel * fileCoefficients + coefficient]; cloud: interleaved (coefficient * 3 + channel).
                        PlyProperty property = layout.ShRest[channel * layout.FileShCoefficients + coefficient];
                        cloud.Sh[shBase + coefficient * 3 + channel] = ReadScalar(vertex, property);
                    }
                }
            }
        }

        private static void DecodeAscii(byte[] bytes, PlyHeader header, VertexLayout layout, SplatCloud cloud)
        {
            // ASCII splat files are rare and large; this path favours simplicity over speed.
            string text = Encoding.ASCII.GetString(bytes, header.BodyOffset, bytes.Length - header.BodyOffset);
            string[] tokens = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int propertyCount = header.Properties.Count;
            if (tokens.Length < propertyCount * header.VertexCount)
            {
                throw new PlyException(PlyError.TruncatedPayload, $"The ASCII PLY body has {tokens.Length} values but {propertyCount * header.VertexCount} are needed.");
            }

            var values = new float[propertyCount];
            int keptCoefficients = cloud.ShCoefficientCount;
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
                {
                    string token = tokens[splatIndex * propertyCount + propertyIndex];
                    if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out values[propertyIndex]))
                    {
                        throw new PlyException(PlyError.InvalidValue, $"Vertex {splatIndex}: '{token}' is not a number.");
                    }
                }

                cloud.Positions[splatIndex] = new float3(ValueOf(values, header, layout.X), ValueOf(values, header, layout.Y), ValueOf(values, header, layout.Z));
                cloud.LogScales[splatIndex] = new float3(ValueOf(values, header, layout.ScaleX), ValueOf(values, header, layout.ScaleY), ValueOf(values, header, layout.ScaleZ));
                cloud.Rotations[splatIndex] = NormalizeRotation(ValueOf(values, header, layout.RotX), ValueOf(values, header, layout.RotY), ValueOf(values, header, layout.RotZ), ValueOf(values, header, layout.RotW));
                cloud.Alphas[splatIndex] = Sigmoid(ValueOf(values, header, layout.Opacity));
                cloud.Colors[splatIndex] = new float3(ValueOf(values, header, layout.ColorR), ValueOf(values, header, layout.ColorG), ValueOf(values, header, layout.ColorB));

                int shBase = splatIndex * cloud.ShFloatsPerSplat;
                for (int coefficient = 0; coefficient < keptCoefficients; coefficient++)
                {
                    for (int channel = 0; channel < 3; channel++)
                    {
                        PlyProperty property = layout.ShRest[channel * layout.FileShCoefficients + coefficient];
                        cloud.Sh[shBase + coefficient * 3 + channel] = ValueOf(values, header, property);
                    }
                }
            }
        }

        /// <summary>In ASCII files the property order is the column order, so the byte offset is only used to find the column.</summary>
        private static float ValueOf(float[] values, PlyHeader header, PlyProperty property)
        {
            for (int propertyIndex = 0; propertyIndex < header.Properties.Count; propertyIndex++)
            {
                if (header.Properties[propertyIndex].ByteOffset == property.ByteOffset) return values[propertyIndex];
            }

            throw new InvalidOperationException("Property not found in header; this cannot happen after VertexLayout resolved it.");
        }

        private static float ReadScalar(ReadOnlySpan<byte> vertex, PlyProperty property)
        {
            ReadOnlySpan<byte> field = vertex.Slice(property.ByteOffset);
            switch (property.Type)
            {
                case PlyScalarType.Float32: return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field));
                case PlyScalarType.Float64: return (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(field));
                case PlyScalarType.Int8: return (sbyte)field[0];
                case PlyScalarType.UInt8: return field[0];
                case PlyScalarType.Int16: return BinaryPrimitives.ReadInt16LittleEndian(field);
                case PlyScalarType.UInt16: return BinaryPrimitives.ReadUInt16LittleEndian(field);
                case PlyScalarType.Int32: return BinaryPrimitives.ReadInt32LittleEndian(field);
                case PlyScalarType.UInt32: return BinaryPrimitives.ReadUInt32LittleEndian(field);
                default: throw new ArgumentOutOfRangeException(nameof(property));
            }
        }

        /// <summary>PLY stores rot_0..3 as w x y z; the cloud keeps x y z w. A zero quaternion (broken trainer output) becomes identity.</summary>
        private static float4 NormalizeRotation(float x, float y, float z, float w)
        {
            var q = new float4(x, y, z, w);
            float length = math.length(q);
            if (length < 1e-8f || float.IsNaN(length)) return new float4(0f, 0f, 0f, 1f);
            return q / length;
        }

        private static float Sigmoid(float logit)
        {
            return 1f / (1f + math.exp(-logit));
        }

        private static string[] BuildShRestNames()
        {
            var names = new string[ShMath.CoefficientCount(4) * 3];
            for (int restIndex = 0; restIndex < names.Length; restIndex++)
            {
                names[restIndex] = "f_rest_" + restIndex.ToString(CultureInfo.InvariantCulture);
            }

            return names;
        }
    }
}
