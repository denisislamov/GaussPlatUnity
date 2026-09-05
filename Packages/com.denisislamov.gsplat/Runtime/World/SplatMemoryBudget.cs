using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Rough but honest accounting (TZ E6-T4): what a scene costs in RAM and VRAM, and how much this device can
    /// spend. Used before a level is loaded (refuse or downgrade) and by the debug overlay.
    /// </summary>
    public static class SplatMemoryBudget
    {
        /// <summary>Per splat: packed CPU copy 16 + splat texture 16 + order texture 4 + sort keys 4 (+ SH twice: CPU bytes and texture).</summary>
        public static long EstimateBytes(int splatCount, int shDegree)
        {
            long perSplat = 16 + 16 + 4 + 4 + 2L * ShMath.CoefficientCount(shDegree) * 3;
            // Decoding needs the float cloud briefly: positions 12 + scales 12 + rotations 16 + alpha 4 + colors 12.
            long transientPerSplat = 56;
            return splatCount * (perSplat + transientPerSplat);
        }

        /// <summary>
        /// Bytes a splat scene may take. Phones: a quarter of RAM, capped at 350 MB (TZ N-04); web: 512 MB heap is
        /// typical, keep well under it; desktop: 2 GB.
        /// </summary>
        public static long DeviceBudgetBytes()
        {
            const long megabyte = 1024 * 1024;
            if (Application.platform == RuntimePlatform.WebGLPlayer) return 300 * megabyte;
            if (Application.isMobilePlatform)
            {
                long quarter = (long)SystemInfo.systemMemorySize * megabyte / 4;
                return System.Math.Min(quarter, 350 * megabyte);
            }

            return 2048 * megabyte;
        }

        public static bool CanAfford(int splatCount, int shDegree)
        {
            return EstimateBytes(splatCount, shDegree) <= DeviceBudgetBytes();
        }
    }
}
