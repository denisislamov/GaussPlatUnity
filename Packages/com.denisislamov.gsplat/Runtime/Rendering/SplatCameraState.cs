using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// What a <see cref="GaussianSplatRenderer"/> keeps per camera: which chunks that camera sees and when it last
    /// needed a sort. Without this the Scene View and the Game View camera would take turns invalidating each
    /// other's order every frame.
    /// </summary>
    internal sealed class SplatCameraState
    {
        public readonly SplatSortPolicy Policy = new SplatSortPolicy();
        public NativeArray<int> VisibleChunks; // not readonly: NativeArray is a struct and its indexer writes through the field

        /// <summary>The visible list for the compute sorter; null where there are no compute shaders (see SplatGpuData.ChunkBuffer).</summary>
        public readonly GraphicsBuffer VisibleChunkBuffer;
        public int VisibleChunkCount;

        /// <summary>Hash of the visible set currently in <see cref="VisibleChunkBuffer"/>; the buffer is re-uploaded only when it changes.</summary>
        public int UploadedVisibleHash;

        /// <summary>P3: per visible chunk, the number of splats it may draw this frame (parallel to VisibleChunks).</summary>
        public NativeArray<int> ChunkBudgets;
        public readonly GraphicsBuffer ChunkBudgetBuffer;

        public SplatCameraState(int chunkCount)
        {
            VisibleChunks = new NativeArray<int>(math.max(1, chunkCount), Allocator.Persistent);
            ChunkBudgets = new NativeArray<int>(math.max(1, chunkCount), Allocator.Persistent);
            if (SystemInfo.supportsComputeShaders)
            {
                VisibleChunkBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(1, chunkCount), sizeof(int)) { name = "GSplat Visible Chunks" };
                ChunkBudgetBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(1, chunkCount), sizeof(int)) { name = "GSplat Chunk Budgets" };
            }
        }

        public void Dispose()
        {
            if (VisibleChunks.IsCreated) VisibleChunks.Dispose();
            if (ChunkBudgets.IsCreated) ChunkBudgets.Dispose();
            VisibleChunkBuffer?.Dispose();
            ChunkBudgetBuffer?.Dispose();
        }
    }
}
