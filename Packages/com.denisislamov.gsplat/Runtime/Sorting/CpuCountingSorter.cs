using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GSplat
{
    /// <summary>
    /// Depth sort on the CPU with Burst: a parallel pass makes 16-bit keys from the packed positions of the
    /// visible chunks, then one counting sort (histogram, prefix sum, scatter). Counting sort because it is O(n)
    /// with a 64k table and needs no comparisons; Spark's worker sorts the same way. This is the path for WebGL2
    /// and for GPUs without compute.
    /// Asynchronous: the job scheduled in one call is collected in the next, so the order lags the camera by a
    /// frame - Spark accepts the same lag and it is not visible in practice. The uploaded order is always a
    /// complete, consistent one (never half of the previous and half of the next).
    /// </summary>
    public sealed class CpuCountingSorter : ISplatSorter
    {
        private Texture2D orderTexture;
        private NativeArray<uint> keys;
        private NativeArray<uint> order;
        private NativeArray<int> histogram;
        private NativeArray<int> visibleChunksCopy;
        private NativeArray<int> sortedCount;
        private JobHandle pendingJob;
        private bool jobInFlight;

        public Texture OrderTexture => orderTexture;
        public int OrderedSplatCount { get; private set; }
        public bool NeedsCompute => false;
        public UnityEngine.ComputeBuffer DrawArgs => null;

        /// <summary>True while a sort is scheduled and not yet collected; useful for the debug overlay.</summary>
        public bool IsSorting => jobInFlight;

        public CpuCountingSorter(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            orderTexture = new Texture2D(SplatOrderTexture.Width, SplatOrderTexture.RowsFor(capacity), GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "GSplat Order (CPU)",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            keys = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            order = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            histogram = new NativeArray<int>(SplatSortKeys.BucketCount, Allocator.Persistent);
            sortedCount = new NativeArray<int>(1, Allocator.Persistent);
        }

        public void PrepareOnMainThread(in SplatSortInput input, bool resort)
        {
            if (jobInFlight)
            {
                if (!pendingJob.IsCompleted && !resort) return;
                // Either the job is done, or the caller needs a fresh sort anyway: finishing it costs at most the
                // remainder of one sort and keeps the "one job at a time" rule simple.
                CollectPendingJob();
            }

            if (!resort) return;
            Schedule(input);
        }

        public void RecordCompute(UnityEngine.Rendering.CommandBuffer commands)
        {
            // Nothing to do: the order is uploaded from the CPU in PrepareOnMainThread.
        }

        /// <summary>Blocks until the current job is done and uploaded. Tests and the editor preview use it; the renderer does not.</summary>
        public void CompleteNow()
        {
            if (jobInFlight) CollectPendingJob();
        }

        private void Schedule(in SplatSortInput input)
        {
            int slotCount = input.VisibleChunks.Length * SplatChunkInfo.Size;
            if (slotCount > keys.Length)
            {
                // More visible chunks than the capacity accounts for can only happen if the data grew; grow with it.
                keys.Dispose();
                keys = new NativeArray<uint>(slotCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            if (visibleChunksCopy.IsCreated) visibleChunksCopy.Dispose();
            visibleChunksCopy = new NativeArray<int>(input.VisibleChunks, Allocator.Persistent);

            SplatSortKeys.LogRange(input.MinDepth, input.MaxDepth, out float logMinDepth, out float inverseLogDepthRange);
            var keyJob = new KeyJob
            {
                Packed = input.Data.Packed,
                Chunks = input.Data.Chunks,
                VisibleChunks = visibleChunksCopy,
                CameraPosition = input.CameraPositionLocal,
                CameraForward = input.CameraForwardLocal,
                Radial = input.Radial,
                LogMinDepth = logMinDepth,
                InverseLogDepthRange = inverseLogDepthRange,
                CullInKeys = input.CullInKeys,
                LocalToClip = input.LocalToClip,
                FocalPixelsY = input.FocalPixelsY,
                ScreenSize = input.ScreenSize,
                MaxStdDev = input.MaxStdDev,
                MinPixelRadius = input.MinPixelRadius,
                Keys = keys
            };
            JobHandle keyHandle = keyJob.Schedule(slotCount, 8192);

            var sortJob = new CountingSortJob
            {
                Keys = keys,
                SlotCount = slotCount,
                VisibleChunks = visibleChunksCopy,
                Histogram = histogram,
                Order = order,
                SortedCount = sortedCount
            };
            pendingJob = sortJob.Schedule(keyHandle);
            jobInFlight = true;
            JobHandle.ScheduleBatchedJobs();
        }

        private void CollectPendingJob()
        {
            pendingJob.Complete();
            jobInFlight = false;

            // The texture stores one uint per RGBA8 texel, so the order array is the texel array (little-endian: byte 0 -> R).
            int count = sortedCount[0];
            NativeArray<uint> texels = orderTexture.GetPixelData<uint>(0);
            NativeArray<uint>.Copy(order, 0, texels, 0, count);
            orderTexture.Apply(false, false);
            OrderedSplatCount = count;
        }

        public void Dispose()
        {
            if (jobInFlight) pendingJob.Complete();
            jobInFlight = false;
            if (keys.IsCreated) keys.Dispose();
            if (order.IsCreated) order.Dispose();
            if (histogram.IsCreated) histogram.Dispose();
            if (visibleChunksCopy.IsCreated) visibleChunksCopy.Dispose();
            if (sortedCount.IsCreated) sortedCount.Dispose();
            if (orderTexture != null)
            {
                SplatObjectUtility.Destroy(orderTexture);
                orderTexture = null;
            }
        }

        /// <summary>One thread slot per (visible chunk, local index); slots past a partial chunk's end get EmptyKey.</summary>
        [BurstCompile]
        private struct KeyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<uint4> Packed;
            [ReadOnly] public NativeArray<SplatChunkInfo> Chunks;
            [ReadOnly] public NativeArray<int> VisibleChunks;
            public float3 CameraPosition;
            public float3 CameraForward;
            public bool Radial;
            public float LogMinDepth;
            public float InverseLogDepthRange;
            public bool CullInKeys;
            public float4x4 LocalToClip;
            public float FocalPixelsY;
            public float2 ScreenSize;
            public float MaxStdDev;
            public float MinPixelRadius;
            [WriteOnly] public NativeArray<uint> Keys;

            public void Execute(int slot)
            {
                int chunkIndex = VisibleChunks[slot / SplatChunkInfo.Size];
                int local = slot % SplatChunkInfo.Size;
                SplatChunkInfo chunk = Chunks[chunkIndex];
                if (local >= chunk.SplatCount)
                {
                    Keys[slot] = SplatSortKeys.EmptyKey;
                    return;
                }

                int splatIndex = chunkIndex * SplatChunkInfo.Size + local;
                PackedSplat.Unpack(Packed[splatIndex], out float3 normalized, out float3 logScale, out _, out _, out _);
                float3 position = chunk.PositionOf(normalized);
                if (CullInKeys && !SplatVisibility.IsVisible(position, math.exp(logScale), LocalToClip, FocalPixelsY, ScreenSize, MaxStdDev, MinPixelRadius))
                {
                    Keys[slot] = SplatSortKeys.EmptyKey;
                    return;
                }

                float metric = SplatSortKeys.SortMetric(position, CameraPosition, CameraForward, Radial);
                Keys[slot] = SplatSortKeys.DepthToKey(metric, LogMinDepth, InverseLogDepthRange);
            }
        }

        [BurstCompile]
        private struct CountingSortJob : IJob
        {
            [ReadOnly] public NativeArray<uint> Keys;
            public int SlotCount;
            [ReadOnly] public NativeArray<int> VisibleChunks;
            public NativeArray<int> Histogram;
            [WriteOnly] public NativeArray<uint> Order;
            [WriteOnly] public NativeArray<int> SortedCount;

            public void Execute()
            {
                for (int bucket = 0; bucket < Histogram.Length; bucket++) Histogram[bucket] = 0;
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    uint key = Keys[slot];
                    if (key != SplatSortKeys.EmptyKey) Histogram[(int)key]++;
                }

                // Exclusive prefix sum: Histogram[b] becomes the first output position of bucket b.
                int runningTotal = 0;
                for (int bucket = 0; bucket < Histogram.Length; bucket++)
                {
                    int bucketCount = Histogram[bucket];
                    Histogram[bucket] = runningTotal;
                    runningTotal += bucketCount;
                }

                SortedCount[0] = runningTotal;

                for (int slot = 0; slot < SlotCount; slot++)
                {
                    uint key = Keys[slot];
                    if (key == SplatSortKeys.EmptyKey) continue;

                    int splatIndex = VisibleChunks[slot / SplatChunkInfo.Size] * SplatChunkInfo.Size + slot % SplatChunkInfo.Size;
                    Order[Histogram[(int)key]] = (uint)splatIndex;
                    Histogram[(int)key]++;
                }
            }
        }
    }
}
