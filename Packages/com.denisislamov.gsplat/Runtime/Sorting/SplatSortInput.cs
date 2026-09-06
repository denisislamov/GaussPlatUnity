using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Everything a sorter needs for one camera: the scene data, which chunks are in view, and the camera in the
    /// splat object's local space (so packed local positions can be used without a transform per splat).
    /// </summary>
    public struct SplatSortInput
    {
        /// <summary>CPU-side data; the CPU sorter reads positions from Packed and the chunk table.</summary>
        public GsplatData Data;

        /// <summary>GPU-side data; the GPU sorter reads the same positions from the splat texture.</summary>
        public SplatGpuData Gpu;

        /// <summary>Indices of chunks that passed frustum culling, in any order.</summary>
        public NativeArray<int> VisibleChunks;

        /// <summary>The same list uploaded for compute; may be null when the sorter runs on the CPU.</summary>
        public GraphicsBuffer VisibleChunkBuffer;

        /// <summary>Sum of SplatCount over the visible chunks: the number of order slots the sort produces.</summary>
        public int VisibleSplatCount;

        public float3 CameraPositionLocal;
        public float3 CameraForwardLocal;

        /// <summary>Order by distance to the camera (Spark/InnerTest) instead of view depth (classic 3DGS). See SplatSortKeys.SortMetric.</summary>
        public bool Radial;

        /// <summary>View depth of the nearest and farthest point of the visible chunk bounds; the 16-bit key spans this range.</summary>
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
