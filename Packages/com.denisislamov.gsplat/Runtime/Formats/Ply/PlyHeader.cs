using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GSplat
{
    /// <summary>One "property &lt;type&gt; &lt;name&gt;" line of the vertex element.</summary>
    public readonly struct PlyProperty
    {
        public readonly string Name;
        public readonly PlyScalarType Type;

        /// <summary>Byte offset inside one binary vertex record.</summary>
        public readonly int ByteOffset;

        public PlyProperty(string name, PlyScalarType type, int byteOffset)
        {
            Name = name;
            Type = type;
            ByteOffset = byteOffset;
        }
    }

    public enum PlyScalarType
    {
        Int8, UInt8, Int16, UInt16, Int32, UInt32, Float32, Float64
    }

    public enum PlyFormat
    {
        Ascii,
        BinaryLittleEndian
    }

    /// <summary>
    /// The text header of a 3DGS .ply: "ply", "format ...", "element vertex N", one "property" line per
    /// attribute, "end_header". Only the vertex element is used; other elements are rejected because no
    /// splat file we know of has them and skipping them correctly needs their sizes.
    /// </summary>
    public sealed class PlyHeader
    {
        public readonly PlyFormat Format;
        public readonly int VertexCount;
        public readonly IReadOnlyList<PlyProperty> Properties;

        /// <summary>Size of one vertex in the binary body.</summary>
        public readonly int VertexStride;

        /// <summary>Byte offset of the first vertex (right after "end_header\n").</summary>
        public readonly int BodyOffset;

        private readonly Dictionary<string, int> propertyIndexByName;

        private PlyHeader(PlyFormat format, int vertexCount, List<PlyProperty> properties, int vertexStride, int bodyOffset)
        {
            Format = format;
            VertexCount = vertexCount;
            Properties = properties;
            VertexStride = vertexStride;
            BodyOffset = bodyOffset;
            propertyIndexByName = new Dictionary<string, int>(properties.Count);
            for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
            {
                propertyIndexByName[properties[propertyIndex].Name] = propertyIndex;
            }
        }

        public bool TryGetProperty(string name, out PlyProperty property)
        {
            if (propertyIndexByName.TryGetValue(name, out int index))
            {
                property = Properties[index];
                return true;
            }

            property = default;
            return false;
        }

        public PlyProperty GetRequiredProperty(string name)
        {
            if (!TryGetProperty(name, out PlyProperty property))
            {
                throw new PlyException(PlyError.MissingProperty, $"The PLY vertex element has no '{name}' property; this does not look like a Gaussian splat file.");
            }

            return property;
        }

        /// <summary>How many f_rest_N properties exist (45 for SH degree 3, 0 for degree 0).</summary>
        public int CountShRestProperties()
        {
            int count = 0;
            while (propertyIndexByName.ContainsKey("f_rest_" + count.ToString(CultureInfo.InvariantCulture)))
            {
                count++;
            }

            return count;
        }

        public static int SizeOf(PlyScalarType type)
        {
            switch (type)
            {
                case PlyScalarType.Int8:
                case PlyScalarType.UInt8: return 1;
                case PlyScalarType.Int16:
                case PlyScalarType.UInt16: return 2;
                case PlyScalarType.Int32:
                case PlyScalarType.UInt32:
                case PlyScalarType.Float32: return 4;
                case PlyScalarType.Float64: return 8;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static PlyHeader Parse(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length == 0) throw new PlyException(PlyError.EmptyPayload, "The PLY payload is empty.");
            if (bytes.Length < 4 || bytes[0] != (byte)'p' || bytes[1] != (byte)'l' || bytes[2] != (byte)'y' || (bytes[3] != (byte)'\n' && bytes[3] != (byte)'\r'))
            {
                throw new PlyException(PlyError.BadMagic, "Not a PLY file: it does not start with a \"ply\" line.");
            }

            int headerEnd = FindHeaderEnd(bytes);
            string headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);
            string[] lines = headerText.Split('\n');

            PlyFormat? format = null;
            int vertexCount = -1;
            bool insideVertexElement = false;
            var properties = new List<PlyProperty>();
            int stride = 0;

            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex].Trim();
                if (line.Length == 0 || line.StartsWith("comment", StringComparison.Ordinal) || line.StartsWith("obj_info", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (tokens[0])
                {
                    case "format":
                        format = ParseFormat(tokens);
                        break;

                    case "element":
                        if (tokens.Length < 3) throw new PlyException(PlyError.MalformedHeader, $"Malformed PLY element line: \"{line}\".");
                        if (tokens[1] == "vertex")
                        {
                            if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out vertexCount) || vertexCount < 0)
                            {
                                throw new PlyException(PlyError.MalformedHeader, $"Malformed PLY vertex count: \"{line}\".");
                            }

                            insideVertexElement = true;
                        }
                        else
                        {
                            if (vertexCount < 0)
                            {
                                throw new PlyException(PlyError.UnsupportedFormat, $"PLY element '{tokens[1]}' comes before the vertex element; only vertex-only splat files are supported.");
                            }

                            insideVertexElement = false; // properties of trailing elements are ignored; their data follows the vertices
                        }
                        break;

                    case "property":
                        if (!insideVertexElement) continue;
                        if (tokens.Length < 3) throw new PlyException(PlyError.MalformedHeader, $"Malformed PLY property line: \"{line}\".");
                        if (tokens[1] == "list") throw new PlyException(PlyError.UnsupportedPropertyType, "PLY list properties on vertices are not supported.");
                        PlyScalarType type = ParseType(tokens[1]);
                        properties.Add(new PlyProperty(tokens[2], type, stride));
                        stride += SizeOf(type);
                        break;

                    case "end_header":
                        break;

                    default:
                        throw new PlyException(PlyError.MalformedHeader, $"Unexpected PLY header line: \"{line}\".");
                }
            }

            if (format == null) throw new PlyException(PlyError.MalformedHeader, "The PLY header has no format line.");
            if (vertexCount < 0) throw new PlyException(PlyError.MalformedHeader, "The PLY header has no vertex element.");

            const int maxPoints = 64 * 1024 * 1024;
            if (vertexCount > maxPoints) throw new PlyException(PlyError.TooManyPoints, $"PLY header claims {vertexCount} vertices; the limit is {maxPoints}.");

            return new PlyHeader(format.Value, vertexCount, properties, stride, headerEnd);
        }

        /// <summary>Index of the first byte after the "end_header" line and its newline.</summary>
        private static int FindHeaderEnd(byte[] bytes)
        {
            byte[] marker = Encoding.ASCII.GetBytes("end_header");
            // Headers are small; scanning the first 64 KB is plenty and bounds the cost on huge files.
            int searchLimit = Math.Min(bytes.Length, 64 * 1024) - marker.Length;
            for (int position = 0; position <= searchLimit; position++)
            {
                if (!MatchesAt(bytes, position, marker)) continue;

                int lineEnd = position + marker.Length;
                while (lineEnd < bytes.Length && bytes[lineEnd] != (byte)'\n') lineEnd++;
                if (lineEnd >= bytes.Length) throw new PlyException(PlyError.TruncatedPayload, "The PLY file ends inside the header.");
                return lineEnd + 1;
            }

            throw new PlyException(PlyError.MalformedHeader, "The PLY header has no end_header line within the first 64 KB.");
        }

        private static bool MatchesAt(byte[] bytes, int position, byte[] marker)
        {
            for (int markerIndex = 0; markerIndex < marker.Length; markerIndex++)
            {
                if (bytes[position + markerIndex] != marker[markerIndex]) return false;
            }

            return true;
        }

        private static PlyFormat ParseFormat(string[] tokens)
        {
            if (tokens.Length < 2) throw new PlyException(PlyError.MalformedHeader, "Malformed PLY format line.");
            switch (tokens[1])
            {
                case "binary_little_endian": return PlyFormat.BinaryLittleEndian;
                case "ascii": return PlyFormat.Ascii;
                case "binary_big_endian":
                    throw new PlyException(PlyError.UnsupportedFormat, "Big-endian PLY files are not supported; every 3DGS exporter writes little-endian.");
                default:
                    throw new PlyException(PlyError.UnsupportedFormat, $"Unknown PLY format '{tokens[1]}'.");
            }
        }

        private static PlyScalarType ParseType(string token)
        {
            switch (token)
            {
                case "char": case "int8": return PlyScalarType.Int8;
                case "uchar": case "uint8": return PlyScalarType.UInt8;
                case "short": case "int16": return PlyScalarType.Int16;
                case "ushort": case "uint16": return PlyScalarType.UInt16;
                case "int": case "int32": return PlyScalarType.Int32;
                case "uint": case "uint32": return PlyScalarType.UInt32;
                case "float": case "float32": return PlyScalarType.Float32;
                case "double": case "float64": return PlyScalarType.Float64;
                default: throw new PlyException(PlyError.UnsupportedPropertyType, $"Unknown PLY property type '{token}'.");
            }
        }
    }
}
