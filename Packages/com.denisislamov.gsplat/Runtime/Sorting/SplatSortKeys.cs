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
    }
}
