using Unity.Collections;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// The depth key both sorters use. Splats are alpha-blended back to front, so the key must sort far splats
    /// first. 16 bits (65 536 steps over the visible depth range) is what the counting sort needs and is finer
    /// than the blending can show; Spark and the WebGL viewers use the same precision.
    /// </summary>
    public static class SplatSortKeys
    {
        public const int KeyBits = 16;
        public const int BucketCount = 1 << KeyBits;
        public const uint MaxKey = BucketCount - 1;

        /// <summary>Marks a thread slot that maps to no splat (the tail of a partial chunk); never counted or scattered.</summary>
        public const uint EmptyKey = 0xFFFFFFFF;

        /// <summary>Depth along the view direction, in world units in front of the camera.</summary>
        public static float ViewDepth(float3 position, float3 cameraPosition, float3 cameraForward)
        {
            return math.dot(position - cameraPosition, cameraForward);
        }

        /// <summary>0 for the farthest splat, MaxKey for the nearest, so ascending order = back to front.</summary>
        public static uint DepthToKey(float depth, float minDepth, float inverseDepthRange)
        {
            float normalized = math.saturate((depth - minDepth) * inverseDepthRange);
            return MaxKey - (uint)(normalized * MaxKey + 0.5f);
        }

        /// <summary>View-depth range of the corners of the visible chunk bounds: the span the 16-bit key is stretched over.</summary>
        public static void DepthRange(NativeArray<SplatChunkInfo> chunks, NativeArray<int> visibleChunks, float3 cameraPosition, float3 cameraForward, out float minDepth, out float maxDepth)
        {
            minDepth = float.MaxValue;
            maxDepth = float.MinValue;
            for (int visibleIndex = 0; visibleIndex < visibleChunks.Length; visibleIndex++)
            {
                SplatChunkInfo chunk = chunks[visibleChunks[visibleIndex]];
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new float3(
                        (corner & 1) != 0 ? chunk.BoundsMax.x : chunk.BoundsMin.x,
                        (corner & 2) != 0 ? chunk.BoundsMax.y : chunk.BoundsMin.y,
                        (corner & 4) != 0 ? chunk.BoundsMax.z : chunk.BoundsMin.z);
                    float depth = ViewDepth(point, cameraPosition, cameraForward);
                    minDepth = math.min(minDepth, depth);
                    maxDepth = math.max(maxDepth, depth);
                }
            }

            if (visibleChunks.Length == 0)
            {
                minDepth = 0f;
                maxDepth = 1f;
            }
        }
    }
}
