using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// A run of up to <see cref="SplatChunkInfo.Size"/> consecutive splats with its bounds. Packed positions are relative
    /// to <see cref="Center"/>. Chunks are the unit of frustum culling and of incremental GPU upload.
    /// Blittable so the same struct can go into a GraphicsBuffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SplatChunkInfo
    {
        /// <summary>65 536 splats: four RGBA8 texels each fill exactly 64 rows of a 4096-wide texture, so a chunk is a texture row range.</summary>
        public const int Size = 65536;

        public int SplatCount;
        private int padding0;
        private int padding1;
        private int padding2;
        public float3 BoundsMin;
        private float padding3;
        public float3 BoundsMax;
        private float padding4;

        public float3 Center => (BoundsMin + BoundsMax) * 0.5f;

        public SplatChunkInfo(int splatCount, float3 boundsMin, float3 boundsMax)
        {
            SplatCount = splatCount;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            padding0 = 0;
            padding1 = 0;
            padding2 = 0;
            padding3 = 0f;
            padding4 = 0f;
        }

        public static int ChunkCountFor(int splatCount)
        {
            return (splatCount + Size - 1) / Size;
        }
    }
}
