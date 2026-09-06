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
        /// <summary>The default: 65 536 buckets. 12 bits (4 096) is the cheaper option for phones and the web, see BucketCountFor.</summary>
        public const int KeyBits = 16;
        public const int MinKeyBits = 8;
        public const int MaxKeyBits = 16;
        public const int BucketCount = 1 << KeyBits;
        public const uint MaxKey = BucketCount - 1;

        public static int BucketCountFor(int keyBits)
        {
            return 1 << math.clamp(keyBits, MinKeyBits, MaxKeyBits);
        }

        public static uint MaxKeyFor(int keyBits)
        {
            return (uint)BucketCountFor(keyBits) - 1;
        }

        /// <summary>Marks a thread slot that maps to no splat (the tail of a partial chunk); never counted or scattered.</summary>
        public const uint EmptyKey = 0xFFFFFFFF;

        /// <summary>Depth along the view direction, in world units in front of the camera.</summary>
        public static float ViewDepth(float3 position, float3 cameraPosition, float3 cameraForward)
        {
            return math.dot(position - cameraPosition, cameraForward);
        }

        /// <summary>
        /// The value splats are ordered by. Radial (distance to the camera) is what Spark uses and it has a
        /// practical bonus: turning the camera does not change the order, only moving does. View depth is the
        /// classic 3DGS choice; the two differ for splats far off the view axis.
        /// </summary>
        public static float SortMetric(float3 position, float3 cameraPosition, float3 cameraForward, bool radial)
        {
            return radial ? math.length(position - cameraPosition) : math.dot(position - cameraPosition, cameraForward);
        }

        /// <summary>Depths below this are treated as this: the log mapping needs a positive lower bound.</summary>
        public const float MinKeyDepth = 0.01f;

        /// <summary>
        /// 0 for the farthest splat, MaxKey for the nearest, so ascending order = back to front. The key is linear in
        /// log(depth): with a scene 300 m deep a linear key has 0.5 cm buckets everywhere, while surfaces 2 m away
        /// need millimeters (thin overlapping splats in one bucket blend in arbitrary order and speckle). Log spacing
        /// gives 0.3 mm at 2 m and 4 cm at 300 m, where nothing is thin.
        /// </summary>
        public static uint DepthToKey(float depth, float logMinDepth, float inverseLogDepthRange)
        {
            return DepthToKey(depth, logMinDepth, inverseLogDepthRange, MaxKey);
        }

        /// <summary>Same mapping onto a narrower key: <paramref name="maxKey"/> = buckets - 1 (see <see cref="MaxKeyFor"/>).</summary>
        public static uint DepthToKey(float depth, float logMinDepth, float inverseLogDepthRange, uint maxKey)
        {
            float normalized = math.saturate((math.log(math.max(depth, MinKeyDepth)) - logMinDepth) * inverseLogDepthRange);
            return maxKey - (uint)(normalized * maxKey + 0.5f);
        }

        /// <summary>The two numbers DepthToKey needs, from a depth range.</summary>
        public static void LogRange(float minDepth, float maxDepth, out float logMinDepth, out float inverseLogDepthRange)
        {
            logMinDepth = math.log(math.max(minDepth, MinKeyDepth));
            float logMaxDepth = math.log(math.max(maxDepth, MinKeyDepth * 2f));
            inverseLogDepthRange = 1f / math.max(logMaxDepth - logMinDepth, 1e-6f);
        }

        /// <summary>Range of the sort metric over the visible chunk bounds: the span the 16-bit key is stretched over.</summary>
        public static void DepthRange(NativeArray<SplatChunkInfo> chunks, NativeArray<int> visibleChunks, float3 cameraPosition, float3 cameraForward, bool radial, out float minDepth, out float maxDepth)
        {
            minDepth = float.MaxValue;
            maxDepth = float.MinValue;
            for (int visibleIndex = 0; visibleIndex < visibleChunks.Length; visibleIndex++)
            {
                SplatChunkInfo chunk = chunks[visibleChunks[visibleIndex]];
                if (radial)
                {
                    // Nearest point of the box to the camera (0 when the camera is inside it) and the farthest corner.
                    float3 closest = math.clamp(cameraPosition, chunk.BoundsMin, chunk.BoundsMax);
                    minDepth = math.min(minDepth, math.distance(closest, cameraPosition));
                }

                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new float3(
                        (corner & 1) != 0 ? chunk.BoundsMax.x : chunk.BoundsMin.x,
                        (corner & 2) != 0 ? chunk.BoundsMax.y : chunk.BoundsMin.y,
                        (corner & 4) != 0 ? chunk.BoundsMax.z : chunk.BoundsMin.z);
                    float metric = SortMetric(point, cameraPosition, cameraForward, radial);
                    if (!radial) minDepth = math.min(minDepth, metric);
                    maxDepth = math.max(maxDepth, metric);
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
