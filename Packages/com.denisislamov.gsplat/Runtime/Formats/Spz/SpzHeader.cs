using System;
using System.Buffers.Binary;

namespace GSplat
{
    /// <summary>
    /// Header of a Niantic .spz file (https://github.com/nianticlabs/spz). Versions 1-3: a 16-byte header and the
    /// body, gzip-compressed together (the file on disk starts with the gzip signature, not with the magic);
    /// version 4: a plain-text 32-byte header, optional extension bytes, a table of contents and one zstd stream
    /// per attribute. All integers are little-endian.
    /// </summary>
    public readonly struct SpzHeader
    {
        public const uint Magic = 0x5053474E; // "NGSP" read as little-endian bytes 'N','G','S','P'
        public const int LegacyHeaderSize = 16;
        public const int V4HeaderSize = 32;
        public const int MinVersion = 1;
        public const int MaxVersion = 4;
        public const byte FlagAntialiased = 0x1;
        public const byte FlagHasExtensions = 0x2;

        /// <summary>From this version on rotations are "smallest three" 10-bit; before they are xyz 8-bit with w recovered.</summary>
        public const int MinSmallestThreeVersion = 3;

        /// <summary>From this version on the body is zstd streams; before it is one gzip stream.</summary>
        public const int MinZstdVersion = 4;

        public readonly int Version;
        public readonly int PointCount;
        public readonly int ShDegree;

        /// <summary>Positions are 24-bit fixed point with this many fractional bits (12 in practice = 0.24 mm).</summary>
        public readonly int FractionalBits;

        public readonly byte Flags;

        /// <summary>Version 4 only: number of zstd streams.</summary>
        public readonly int StreamCount;

        /// <summary>Version 4 only: byte offset of the table of contents (header + extension bytes).</summary>
        public readonly int TocByteOffset;

        public bool Antialiased => (Flags & FlagAntialiased) != 0;
        public bool HasExtensions => (Flags & FlagHasExtensions) != 0;
        public int HeaderSize => Version >= MinZstdVersion ? V4HeaderSize : LegacyHeaderSize;
        public bool UsesSmallestThreeRotation => Version >= MinSmallestThreeVersion;

        /// <summary>Bytes per rotation in the packed body: 3 (xyz 8-bit) before version 3, 4 (smallest three) after.</summary>
        public int RotationBytes => UsesSmallestThreeRotation ? 4 : 3;

        /// <summary>Version 1 stored positions as three float16 (6 bytes), everything later as three 24-bit ints (9 bytes).</summary>
        public int PositionBytes => Version == 1 ? 6 : 9;

        public SpzHeader(int version, int pointCount, int shDegree, int fractionalBits, byte flags, int streamCount, int tocByteOffset)
        {
            Version = version;
            PointCount = pointCount;
            ShDegree = shDegree;
            FractionalBits = fractionalBits;
            Flags = flags;
            StreamCount = streamCount;
            TocByteOffset = tocByteOffset;
        }

        /// <summary>Parses and validates the header. Throws <see cref="SpzException"/> with a typed code.</summary>
        public static SpzHeader Parse(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                throw new SpzException(SpzError.EmptyPayload, "The SPZ payload is empty.");
            }

            if (bytes.Length < LegacyHeaderSize)
            {
                throw new SpzException(SpzError.TruncatedPayload, $"The SPZ payload is {bytes.Length} bytes; even the header needs {LegacyHeaderSize}.");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (magic != Magic)
            {
                throw new SpzException(SpzError.BadMagic, $"Not an SPZ file: magic 0x{magic:X8}, expected 0x{Magic:X8} (\"NGSP\").");
            }

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4));
            if (version < MinVersion || version > MaxVersion)
            {
                throw new SpzException(SpzError.UnsupportedVersion, $"SPZ version {version} is not supported; this build reads versions {MinVersion}-{MaxVersion}.");
            }

            uint pointCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8));
            // A hard cap keeps a corrupted count from asking for gigabytes; nothing we render is near it.
            const uint maxPoints = 64 * 1024 * 1024;
            if (pointCount > maxPoints)
            {
                throw new SpzException(SpzError.TooManyPoints, $"SPZ header claims {pointCount} points; the limit is {maxPoints}.");
            }

            byte shDegree = bytes[12];
            byte fractionalBits = bytes[13];
            byte flags = bytes[14];
            if (shDegree > 4)
            {
                throw new SpzException(SpzError.UnsupportedShDegree, $"SPZ SH degree {shDegree} is invalid; the format allows 0-4.");
            }

            if (fractionalBits > 24)
            {
                throw new SpzException(SpzError.InvalidValue, $"SPZ fractional bits {fractionalBits} is invalid; positions are 24-bit fixed point.");
            }

            int streamCount = 0;
            int tocByteOffset = 0;
            if (version >= MinZstdVersion)
            {
                if (bytes.Length < V4HeaderSize)
                {
                    throw new SpzException(SpzError.TruncatedPayload, $"SPZ version {version} needs a {V4HeaderSize}-byte header; the payload has {bytes.Length} bytes.");
                }

                streamCount = bytes[15];
                tocByteOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(16)));
            }

            return new SpzHeader((int)version, (int)pointCount, shDegree, fractionalBits, flags, streamCount, tocByteOffset);
        }

        /// <summary>Writes a legacy (version 1-3) header.</summary>
        public void WriteLegacy(Span<byte> destination)
        {
            if (Version >= MinZstdVersion) throw new InvalidOperationException("WriteLegacy is only for versions 1-3.");
            if (destination.Length < LegacyHeaderSize) throw new ArgumentException("Destination is too small for a header.", nameof(destination));

            BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4), (uint)Version);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), (uint)PointCount);
            destination[12] = (byte)ShDegree;
            destination[13] = (byte)FractionalBits;
            destination[14] = Flags;
            destination[15] = 0;
        }
    }
}
