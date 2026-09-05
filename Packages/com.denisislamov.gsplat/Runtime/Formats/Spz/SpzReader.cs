using System;
using System.IO;
using System.IO.Compression;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Decodes a Niantic .spz file (versions 1-3) into a <see cref="SplatCloud"/>. The whole file - 16-byte header
    /// and then, per attribute, all points in a row (positions, alphas, colors, scales, rotations, SH) - is one gzip
    /// stream; the reference writer gzips header and body together. Version 4 keeps its 32-byte header in plain
    /// text and uses zstd streams; it is recognised but not decoded yet, see <see cref="Decode"/>.
    /// Positions come out in the file's own coordinate system; convert with <see cref="CoordinateConverter"/>.
    /// </summary>
    public static class SpzReader
    {
        /// <summary>Gzip files start with these two bytes; a plain-text header means version 4 (or a broken file).</summary>
        public static bool IsGzip(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;
        }

        /// <summary>
        /// Reads only the header, so the caller can allocate before decoding (possibly on another thread). For the
        /// gzip versions only the first 16 bytes are inflated, which is cheap even for a 30 MB file.
        /// </summary>
        public static SpzHeader ReadHeader(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (!IsGzip(bytes)) return SpzHeader.Parse(bytes);

            byte[] headerBytes = Decompress(bytes, 0, SpzHeader.LegacyHeaderSize, "header");
            return SpzHeader.Parse(headerBytes);
        }

        /// <summary>Header + decode in one call, for callers that do not care about threads.</summary>
        public static SplatCloud Read(byte[] bytes, Allocator allocator = Allocator.Persistent)
        {
            SpzHeader header = ReadHeader(bytes);
            var cloud = new SplatCloud(header.PointCount, math.min(header.ShDegree, ShMath.MaxDegree), header.Antialiased, allocator);
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

        /// <summary>
        /// Fills <paramref name="cloud"/> (allocated by the caller from <paramref name="header"/>) with the decoded
        /// splats. Safe to run on a worker thread: it only reads managed memory and writes into the NativeArrays.
        /// SH degree 4 files are read but the degree-4 coefficients are dropped; nothing here renders them.
        /// </summary>
        public static void Decode(byte[] bytes, SpzHeader header, SplatCloud cloud)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (cloud == null) throw new ArgumentNullException(nameof(cloud));
            if (cloud.Count != header.PointCount)
            {
                throw new ArgumentException($"The cloud holds {cloud.Count} splats but the header says {header.PointCount}.", nameof(cloud));
            }

            if (header.Version >= SpzHeader.MinZstdVersion)
            {
                // TODO: SPZ v4 stores each attribute as a separate zstd stream. We have no managed zstd decoder yet;
                // whether to bring ZstdSharp (managed, works in Wasm) or a native plugin is open question 3 of the TZ.
                throw new SpzException(SpzError.UnsupportedCompression,
                    $"SPZ version {header.Version} uses zstd compression, which this build cannot decode yet. Re-export the file as SPZ version 3 or use PLY.");
            }

            if (!IsGzip(bytes))
            {
                throw new SpzException(SpzError.CorruptedCompression, $"SPZ version {header.Version} must be a gzip stream, but the file does not start with the gzip signature.");
            }

            int fileShCoefficients = ShMath.CoefficientCount(header.ShDegree);
            long bodySize = (long)header.PointCount * (header.PositionBytes + 1 + 3 + 3 + header.RotationBytes + fileShCoefficients * 3);
            if (bodySize + SpzHeader.LegacyHeaderSize > int.MaxValue)
            {
                throw new SpzException(SpzError.TooManyPoints, $"The decompressed SPZ body would be {bodySize} bytes.");
            }

            // The header is inside the gzip stream too; the body starts right after it.
            byte[] body = Decompress(bytes, 0, SpzHeader.LegacyHeaderSize + (int)bodySize, "body");
            int offset = SpzHeader.LegacyHeaderSize;
            offset = DecodePositions(body, offset, header, cloud);
            offset = DecodeAlphas(body, offset, cloud);
            offset = DecodeColors(body, offset, cloud);
            offset = DecodeScales(body, offset, cloud);
            offset = DecodeRotations(body, offset, header, cloud);
            DecodeSh(body, offset, fileShCoefficients, cloud);
        }

        /// <summary>Inflates exactly <paramref name="expectedSize"/> bytes starting at <paramref name="offset"/> of the file.</summary>
        private static byte[] Decompress(byte[] bytes, int offset, int expectedSize, string what)
        {
            var result = new byte[expectedSize];
            try
            {
                using (var source = new MemoryStream(bytes, offset, bytes.Length - offset, false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                {
                    int total = 0;
                    while (total < expectedSize)
                    {
                        int read = gzip.Read(result, total, expectedSize - total);
                        if (read == 0) break;
                        total += read;
                    }

                    if (total < expectedSize)
                    {
                        throw new SpzException(SpzError.TruncatedPayload,
                            $"The SPZ {what} decompressed to {total} bytes but {expectedSize} were expected. The file is probably cut off.");
                    }

                    // Extension data may follow the body when FlagHasExtensions is set; we do not read it.
                }
            }
            catch (Exception inner) when (inner is InvalidDataException || inner is IOException)
            {
                // .NET reports a bad deflate stream as InvalidDataException, Mono (the Unity editor) as a plain IOException.
                throw new SpzException(SpzError.CorruptedCompression, "The SPZ gzip stream is corrupted: " + inner.Message, inner);
            }

            return result;
        }

        private static int DecodePositions(byte[] body, int offset, SpzHeader header, SplatCloud cloud)
        {
            if (header.Version == 1)
            {
                // Version 1 was never released publicly (float16 positions) but the reference reader still accepts it.
                for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
                {
                    float x = Mathf.HalfToFloat((ushort)(body[offset] | (body[offset + 1] << 8)));
                    float y = Mathf.HalfToFloat((ushort)(body[offset + 2] | (body[offset + 3] << 8)));
                    float z = Mathf.HalfToFloat((ushort)(body[offset + 4] | (body[offset + 5] << 8)));
                    cloud.Positions[splatIndex] = new float3(x, y, z);
                    offset += 6;
                }

                return offset;
            }

            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                float x = SpzQuantization.DecodePosition(body[offset], body[offset + 1], body[offset + 2], header.FractionalBits);
                float y = SpzQuantization.DecodePosition(body[offset + 3], body[offset + 4], body[offset + 5], header.FractionalBits);
                float z = SpzQuantization.DecodePosition(body[offset + 6], body[offset + 7], body[offset + 8], header.FractionalBits);
                cloud.Positions[splatIndex] = new float3(x, y, z);
                offset += 9;
            }

            return offset;
        }

        private static int DecodeAlphas(byte[] body, int offset, SplatCloud cloud)
        {
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                cloud.Alphas[splatIndex] = SpzQuantization.DecodeAlpha(body[offset]);
                offset += 1;
            }

            return offset;
        }

        private static int DecodeColors(byte[] body, int offset, SplatCloud cloud)
        {
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                cloud.Colors[splatIndex] = new float3(
                    SpzQuantization.DecodeColor(body[offset]),
                    SpzQuantization.DecodeColor(body[offset + 1]),
                    SpzQuantization.DecodeColor(body[offset + 2]));
                offset += 3;
            }

            return offset;
        }

        private static int DecodeScales(byte[] body, int offset, SplatCloud cloud)
        {
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                cloud.LogScales[splatIndex] = new float3(
                    SpzQuantization.DecodeLogScale(body[offset]),
                    SpzQuantization.DecodeLogScale(body[offset + 1]),
                    SpzQuantization.DecodeLogScale(body[offset + 2]));
                offset += 3;
            }

            return offset;
        }

        private static int DecodeRotations(byte[] body, int offset, SpzHeader header, SplatCloud cloud)
        {
            if (header.UsesSmallestThreeRotation)
            {
                for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
                {
                    uint packed = (uint)(body[offset] | (body[offset + 1] << 8) | (body[offset + 2] << 16) | (body[offset + 3] << 24));
                    cloud.Rotations[splatIndex] = SpzQuantization.DecodeRotationSmallestThree(packed);
                    offset += 4;
                }

                return offset;
            }

            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                cloud.Rotations[splatIndex] = SpzQuantization.DecodeRotationFirstThree(body[offset], body[offset + 1], body[offset + 2]);
                offset += 3;
            }

            return offset;
        }

        private static void DecodeSh(byte[] body, int offset, int fileShCoefficients, SplatCloud cloud)
        {
            int keptFloats = cloud.ShFloatsPerSplat;
            int fileFloats = fileShCoefficients * 3;
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                int destination = splatIndex * keptFloats;
                for (int floatIndex = 0; floatIndex < keptFloats; floatIndex++)
                {
                    cloud.Sh[destination + floatIndex] = SpzQuantization.DecodeSh(body[offset + floatIndex]);
                }

                offset += fileFloats; // skip the degree-4 coefficients we do not keep, if any
            }
        }
    }
}
