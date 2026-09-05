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

        /// <summary>View depth of the nearest and farthest point of the visible chunk bounds; the 16-bit key spans this range.</summary>
        public float MinDepth;
        public float MaxDepth;
    }
}
