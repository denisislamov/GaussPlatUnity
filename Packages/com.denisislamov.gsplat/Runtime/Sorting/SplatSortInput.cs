using Unity.Collections;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Everything a sorter needs for one camera: the scene data, which chunks are in view, and the camera as a
    /// <see cref="SplatCameraView"/>.
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

        /// <summary>
        /// P3: for each visible chunk (same order as <see cref="VisibleChunks"/>), how many of its splats may be drawn;
        /// the key pass drops local indices at or above it. Only meaningful when the data is importance-ordered
        /// inside the chunks; an empty array means no budget.
        /// </summary>
        public NativeArray<int> ChunkBudgets;

        /// <summary>The same budgets for compute; null when the sorter runs on the CPU or there is no budget.</summary>
        public GraphicsBuffer ChunkBudgetBuffer;

        public SplatCameraView View;
    }
}
