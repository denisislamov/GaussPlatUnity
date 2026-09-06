using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// The camera as the sorters see it: in the splat object's local space (so packed local positions need no
    /// transform per splat) plus what the key pass needs to cull and to build the 16-bit key. One struct so the
    /// Burst job and the compute shader receive the same block, and a new camera parameter is added in one place.
    /// Plain data, no managed fields: it sits inside a Burst job.
    /// </summary>
    public struct SplatCameraView
    {
        public float3 PositionLocal;
        public float3 ForwardLocal;

        /// <summary>Order by distance to the camera (Spark) instead of view depth (classic 3DGS). See SplatSortKeys.SortMetric.</summary>
        public bool Radial;

        /// <summary>Sort metric of the nearest and farthest point of the visible chunk bounds; the 16-bit key spans this range.</summary>
        public float MinDepth;
        public float MaxDepth;

        /// <summary>
        /// When set, the key pass drops splats that cannot produce pixels (behind the camera, off screen, below
        /// <see cref="MinPixelRadius"/>), see <see cref="SplatVisibility"/>. Off for orthographic cameras and tests.
        /// </summary>
        public bool CullInKeys;

        /// <summary>projection x view x localToWorld, for <see cref="CullInKeys"/>.</summary>
        public float4x4 LocalToClip;

        /// <summary>|P[1][1]| x screen height / 2: pixels per unit at depth 1.</summary>
        public float FocalPixelsY;

        /// <summary>Render target size in pixels, to turn a pixel radius into NDC for the off-screen test.</summary>
        public float2 ScreenSize;

        public float MaxStdDev;

        /// <summary>Splats whose own radius (before dilation) projects below this many pixels are skipped.</summary>
        public float MinPixelRadius;
    }
}
