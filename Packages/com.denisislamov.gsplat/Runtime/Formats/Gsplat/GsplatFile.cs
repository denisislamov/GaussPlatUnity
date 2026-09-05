using System;
using System.Buffers.Binary;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace GSplat
{
    public enum GsplatFileError
    {
        None = 0,
        EmptyPayload,
        BadMagic,
        UnsupportedVersion,
        TruncatedPayload,
        InvalidValue
    }

    public sealed class GsplatFileException : Exception
    {
        public GsplatFileError Code { get; }

        public GsplatFileException(GsplatFileError code, string message) : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// The package's own on-disk format: <see cref="GsplatData"/> written out as-is so loading is a memcpy.
    /// Layout (little-endian):
    ///   header 44 bytes: magic "GSPC", version u32, splatCount u32, chunkCount u32, shDegree u8, flags u8,
    ///                    reserved u16, boundsMin 3 x f32, boundsMax 3 x f32
    ///   chunk table: chunkCount x 28 bytes (splatCount u32, boundsMin 3 x f32, boundsMax 3 x f32)
    ///   packed splats: splatCount x 16 bytes
    ///   SH: splatCount x shCoefficients x 3 bytes
    /// Bump Version and re-import when the layout changes; old files are refused, not migrated.
    /// </summary>
    public static class GsplatFile
    {
        public const uint Magic = 0x43505347; // "GSPC" as little-endian bytes
        public const uint Version = 1;
        public const int HeaderSize = 44;
        public const int ChunkEntrySize = 28;
        public const byte FlagAntialiased = 0x1;

        public static byte[] Serialize(GsplatData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            long size = HeaderSize + (long)data.ChunkCount * ChunkEntrySize + (long)data.SplatCount * PackedSplat.SizeInBytes + data.Sh.Length;
            if (size > int.MaxValue) throw new GsplatFileException(GsplatFileError.InvalidValue, $"The .gsplat file would be {size} bytes; the format is limited to 2 GB.");

            var bytes = new byte[size];
            Span<byte> header = bytes.AsSpan(0, HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4), Version);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8), (uint)data.SplatCount);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12), (uint)data.ChunkCount);
            header[16] = (byte)data.ShDegree;
            header[17] = data.Antialiased ? FlagAntialiased : (byte)0;
            header[18] = 0;
            header[19] = 0;
            WriteFloat3(header.Slice(20), data.BoundsMin);
            WriteFloat3(header.Slice(32), data.BoundsMax);

            int offset = HeaderSize;
            for (int chunkIndex = 0; chunkIndex < data.ChunkCount; chunkIndex++)
            {
                SplatChunkInfo chunk = data.Chunks[chunkIndex];
                Span<byte> entry = bytes.AsSpan(offset, ChunkEntrySize);
                BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)chunk.SplatCount);
                WriteFloat3(entry.Slice(4), chunk.BoundsMin);
                WriteFloat3(entry.Slice(16), chunk.BoundsMax);
                offset += ChunkEntrySize;
            }

            NativeArray<byte> packedBytes = data.Packed.Reinterpret<byte>(UnsafeUtility.SizeOf<uint4>());
            NativeArray<byte>.Copy(packedBytes, 0, bytes, offset, packedBytes.Length);
            offset += packedBytes.Length;

            if (data.Sh.Length > 0)
            {
                NativeArray<byte>.Copy(data.Sh, 0, bytes, offset, data.Sh.Length);
            }

            return bytes;
        }

        public static GsplatData Deserialize(byte[] bytes, Allocator allocator = Allocator.Persistent)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length == 0) throw new GsplatFileException(GsplatFileError.EmptyPayload, "The .gsplat payload is empty.");
            if (bytes.Length < HeaderSize) throw new GsplatFileException(GsplatFileError.TruncatedPayload, $"The .gsplat payload is {bytes.Length} bytes; the header alone is {HeaderSize}.");

            ReadOnlySpan<byte> header = bytes.AsSpan(0, HeaderSize);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (magic != Magic) throw new GsplatFileException(GsplatFileError.BadMagic, $"Not a .gsplat file: magic 0x{magic:X8}, expected 0x{Magic:X8} (\"GSPC\").");

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4));
            if (version != Version) throw new GsplatFileException(GsplatFileError.UnsupportedVersion, $".gsplat version {version} is not supported; this build reads version {Version}. Re-import the source file.");

            int splatCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8)));
            int chunkCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12)));
            int shDegree = header[16];
            bool antialiased = (header[17] & FlagAntialiased) != 0;
            float3 boundsMin = ReadFloat3(header.Slice(20));
            float3 boundsMax = ReadFloat3(header.Slice(32));

            if (shDegree > ShMath.MaxDegree) throw new GsplatFileException(GsplatFileError.InvalidValue, $".gsplat SH degree {shDegree} is invalid.");
            if (chunkCount != SplatChunkInfo.ChunkCountFor(splatCount)) throw new GsplatFileException(GsplatFileError.InvalidValue, $".gsplat has {chunkCount} chunks for {splatCount} splats; expected {SplatChunkInfo.ChunkCountFor(splatCount)}.");

            long shBytes = (long)splatCount * ShMath.CoefficientCount(shDegree) * 3;
            long expectedSize = HeaderSize + (long)chunkCount * ChunkEntrySize + (long)splatCount * PackedSplat.SizeInBytes + shBytes;
            if (bytes.Length < expectedSize) throw new GsplatFileException(GsplatFileError.TruncatedPayload, $"The .gsplat payload is {bytes.Length} bytes but {expectedSize} are needed. The file is probably cut off.");

            var data = new GsplatData(splatCount, shDegree, antialiased, boundsMin, boundsMax, allocator);
            try
            {
                int offset = HeaderSize;
                int remaining = splatCount;
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    ReadOnlySpan<byte> entry = bytes.AsSpan(offset, ChunkEntrySize);
                    int chunkSplatCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry));
                    int expectedInChunk = math.min(SplatChunkInfo.Size, remaining);
                    if (chunkSplatCount != expectedInChunk)
                    {
                        throw new GsplatFileException(GsplatFileError.InvalidValue, $".gsplat chunk {chunkIndex} claims {chunkSplatCount} splats; expected {expectedInChunk}.");
                    }

                    data.Chunks[chunkIndex] = new SplatChunkInfo(chunkSplatCount, ReadFloat3(entry.Slice(4)), ReadFloat3(entry.Slice(16)));
                    remaining -= chunkSplatCount;
                    offset += ChunkEntrySize;
                }

                NativeArray<byte> packedBytes = data.Packed.Reinterpret<byte>(UnsafeUtility.SizeOf<uint4>());
                NativeArray<byte>.Copy(bytes, offset, packedBytes, 0, packedBytes.Length);
                offset += packedBytes.Length;

                if (data.Sh.Length > 0)
                {
                    NativeArray<byte>.Copy(bytes, offset, data.Sh, 0, data.Sh.Length);
                }
            }
            catch
            {
                data.Dispose();
                throw;
            }

            return data;
        }

        private static void WriteFloat3(Span<byte> destination, float3 value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value.x));
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), BitConverter.SingleToInt32Bits(value.y));
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), BitConverter.SingleToInt32Bits(value.z));
        }

        private static float3 ReadFloat3(ReadOnlySpan<byte> source)
        {
            return new float3(
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source)),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4))),
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8))));
        }
    }
}
