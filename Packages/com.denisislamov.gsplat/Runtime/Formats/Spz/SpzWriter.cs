using System;
using System.IO;
using System.IO.Compression;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Encodes a <see cref="SplatCloud"/> as SPZ version 3 (gzip body, smallest-three rotations). Used by the
    /// tests to round-trip the reader and by editor tooling to re-export; positions are written in whatever
    /// coordinate system the cloud currently holds.
    /// </summary>
    public static class SpzWriter
    {
        public const int Version = 3;

        /// <summary>12 fractional bits = 1/4096 units, the value the reference tools use.</summary>
        public const int DefaultFractionalBits = 12;

        public static byte[] Write(SplatCloud cloud, int fractionalBits = DefaultFractionalBits)
        {
            if (cloud == null) throw new ArgumentNullException(nameof(cloud));
            if (fractionalBits < 0 || fractionalBits > 24) throw new ArgumentOutOfRangeException(nameof(fractionalBits));

            byte flags = cloud.Antialiased ? SpzHeader.FlagAntialiased : (byte)0;
            var header = new SpzHeader(Version, cloud.Count, cloud.ShDegree, fractionalBits, flags, 0, 0);
            int bodySize = cloud.Count * (9 + 1 + 3 + 3 + 4 + cloud.ShFloatsPerSplat);
            var body = new byte[bodySize];

            int offset = 0;
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                float3 position = cloud.Positions[splatIndex];
                offset = WriteInt24(body, offset, SpzQuantization.EncodePosition(position.x, fractionalBits));
                offset = WriteInt24(body, offset, SpzQuantization.EncodePosition(position.y, fractionalBits));
                offset = WriteInt24(body, offset, SpzQuantization.EncodePosition(position.z, fractionalBits));
            }

            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                body[offset++] = SpzQuantization.EncodeAlpha(cloud.Alphas[splatIndex]);
            }

            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                float3 color = cloud.Colors[splatIndex];
                body[offset++] = SpzQuantization.EncodeColor(color.x);
                body[offset++] = SpzQuantization.EncodeColor(color.y);
                body[offset++] = SpzQuantization.EncodeColor(color.z);
            }

            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                float3 logScale = cloud.LogScales[splatIndex];
                body[offset++] = SpzQuantization.EncodeLogScale(logScale.x);
                body[offset++] = SpzQuantization.EncodeLogScale(logScale.y);
                body[offset++] = SpzQuantization.EncodeLogScale(logScale.z);
            }

            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                uint packed = SpzQuantization.EncodeRotationSmallestThree(cloud.Rotations[splatIndex]);
                body[offset++] = (byte)(packed & 0xFF);
                body[offset++] = (byte)((packed >> 8) & 0xFF);
                body[offset++] = (byte)((packed >> 16) & 0xFF);
                body[offset++] = (byte)((packed >> 24) & 0xFF);
            }

            for (int floatIndex = 0; floatIndex < cloud.Sh.Length; floatIndex++)
            {
                body[offset++] = SpzQuantization.EncodeSh(cloud.Sh[floatIndex]);
            }

            using (var output = new MemoryStream(SpzHeader.LegacyHeaderSize + bodySize / 2))
            {
                var headerBytes = new byte[SpzHeader.LegacyHeaderSize];
                header.WriteLegacy(headerBytes);
                output.Write(headerBytes, 0, headerBytes.Length);
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                {
                    gzip.Write(body, 0, body.Length);
                }

                return output.ToArray();
            }
        }

        private static int WriteInt24(byte[] destination, int offset, int value)
        {
            destination[offset] = (byte)(value & 0xFF);
            destination[offset + 1] = (byte)((value >> 8) & 0xFF);
            destination[offset + 2] = (byte)((value >> 16) & 0xFF);
            return offset + 3;
        }
    }
}
