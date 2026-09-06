using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// A run of up to <see cref="SplatChunkInfo.Size"/> consecutive splats with its bounds. BoundsMin/Max are padded by
    /// <see cref="Padding"/> (3 sigma of the largest splat) for culling; packed positions are 16-bit fractions of the
    /// unpadded range [PositionMin, PositionMin + PositionExtent]. Chunks are the unit of frustum culling and of
    /// incremental GPU upload. Blittable (48 bytes) so the same struct goes into a GraphicsBuffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SplatChunkInfo
    {
        /// <summary>65 536 splats: four RGBA8 texels each fill exactly 64 rows of a 4096-wide texture, so a chunk is a texture row range.</summary>
        public const int Size = 65536;

        public int SplatCount;

        /// <summary>Meters added on every side of the center bounds so the padded bounds contain the splats' extents.</summary>
        public float Padding;

        // Layout fillers only (unrelated to Padding above): they keep every float3 on a 16-byte boundary, which is what
        // the HLSL side of the chunk buffer expects. Always zero.
        private int reserved1;
        private int reserved2;
        public float3 BoundsMin;
        private float reserved3;
        public float3 BoundsMax;
        private float reserved4;

        public float3 Center => (BoundsMin + BoundsMax) * 0.5f;

        /// <summary>Lower corner of the unpadded bounds: where a packed position of 0 lands.</summary>
        public float3 PositionMin => BoundsMin + Padding;

        /// <summary>Size of the unpadded bounds; a packed position of 65535 lands at PositionMin + PositionExtent. Never zero (see the constructor).</summary>
        public float3 PositionExtent => math.max(BoundsMax - Padding - PositionMin, 1e-6f);

        /// <summary><paramref name="boundsMin"/> / <paramref name="boundsMax"/> are the unpadded (center) bounds; padding is added here.</summary>
        public SplatChunkInfo(int splatCount, float3 boundsMin, float3 boundsMax, float padding = 0f)
        {
            SplatCount = splatCount;
            Padding = padding;
            BoundsMin = boundsMin - padding;
            BoundsMax = boundsMax + padding;
            reserved1 = 0;
            reserved2 = 0;
            reserved3 = 0f;
            reserved4 = 0f;
        }

        /// <summary>Position from the 16-bit fractions stored in a packed splat.</summary>
        public float3 PositionOf(float3 normalized)
        {
            return PositionMin + normalized * PositionExtent;
        }

        public static int ChunkCountFor(int splatCount)
        {
            return (splatCount + Size - 1) / Size;
        }
    }
}
