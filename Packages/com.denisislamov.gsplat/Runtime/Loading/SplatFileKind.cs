using System.Buffers.Binary;

namespace GSplat
{
    public enum SplatFileKind
    {
        Unknown = 0,
        Spz,
        Ply,
        Gsplat
    }

    /// <summary>Tells the three supported formats apart by their first bytes, so URLs do not need a correct extension.</summary>
    public static class SplatFileKindDetector
    {
        public static SplatFileKind Detect(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return SplatFileKind.Unknown;

            // SPZ versions 1-3 are gzip files with the header inside the stream; only the gzip signature is visible.
            if (SpzReader.IsGzip(bytes)) return SplatFileKind.Spz;

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (magic == SpzHeader.Magic) return SplatFileKind.Spz; // version 4: plain-text header
            if (magic == GsplatFile.Magic) return SplatFileKind.Gsplat;
            if (bytes[0] == (byte)'p' && bytes[1] == (byte)'l' && bytes[2] == (byte)'y' && (bytes[3] == (byte)'\n' || bytes[3] == (byte)'\r')) return SplatFileKind.Ply;
            return SplatFileKind.Unknown;
        }
    }
}
