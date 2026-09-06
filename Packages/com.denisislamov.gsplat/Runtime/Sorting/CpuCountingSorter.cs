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
    /// Depth sort on the CPU with Burst: a parallel pass makes keys from the packed positions of the visible chunks,
    /// then one counting sort (histogram, prefix sum, scatter). Counting sort because it is O(n) with a small table
    /// and needs no comparisons; Spark's worker sorts the same way. This is the path for WebGL2 and for GPUs
    /// without compute.
    /// Two ways to run it:
    /// - one job chain per sort, scheduled in one frame and collected in the next (the order lags the camera by a
    ///   frame - Spark accepts the same lag and it is not visible in practice);
    /// - time-sliced (P6, for the web where jobs run on the main thread without Burst): the same work cut into
    ///   pieces of <see cref="SplatSorterOptions.SlotsPerFrame"/> slots, one piece per frame, so no frame pays for
    ///   the whole sort. The order then lags by as many frames as the sort takes.
    /// In both cases the uploaded order is always a complete, consistent one (never half of the previous and half
    /// of the next).
    /// </summary>
    public sealed class CpuCountingSorter : ISplatSorter
    {
        private enum SlicePhase { Idle, Keys, Histogram, Prefix, Scatter }

        private readonly SplatSorterOptions options;
        private readonly int bucketCount;
        private readonly uint maxKey;

        private Texture2D orderTexture;
        private NativeArray<uint> keys;
        private NativeArray<uint> order;
        private NativeArray<int> histogram;
        private NativeArray<int> visibleChunksCopy;
        private NativeArray<int> budgetsCopy;
        private NativeArray<int> sortedCount;

        // One-shot mode
        private JobHandle pendingJob;
        private bool jobInFlight;

        // Time-sliced mode
        private SlicePhase phase = SlicePhase.Idle;
        private int sliceCursor;
        private int sliceSlotCount;
        private KeyJob sliceKeyJob;

        public Texture OrderTexture => orderTexture;
        public int OrderedSplatCount { get; private set; }
        public UnityEngine.ComputeBuffer DrawArgs => null;
        public SplatSorterOptions Options => options;

        /// <summary>True while a sort is scheduled and not yet collected (or a sliced sort is in progress); useful for the debug overlay.</summary>
        public bool IsSorting => jobInFlight || phase != SlicePhase.Idle;

        /// <summary>Main-thread milliseconds the last collected sort cost (scheduling, waiting and the upload), for the benchmark.</summary>
        public float LastSortMilliseconds { get; private set; }

        public CpuCountingSorter(int capacity) : this(capacity, SplatSorterOptions.Default(SplatSorterKind.Cpu))
        {
        }

        public CpuCountingSorter(int capacity, SplatSorterOptions sorterOptions)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            options = sorterOptions;
            bucketCount = SplatSortKeys.BucketCountFor(options.KeyBits);
            maxKey = SplatSortKeys.MaxKeyFor(options.KeyBits);

            orderTexture = new Texture2D(SplatOrderTexture.Width, SplatOrderTexture.RowsFor(capacity), GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "GSplat Order (CPU)",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            keys = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            order = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            histogram = new NativeArray<int>(bucketCount, Allocator.Persistent);
            sortedCount = new NativeArray<int>(1, Allocator.Persistent);
        }

        public void Sort(in SplatSortInput input, bool resort)
        {
            if (options.TimeSliced)
            {
                SortSliced(input, resort);
                return;
            }

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
            // Nothing to do: the order is uploaded from the CPU in Sort.
        }

        /// <summary>Blocks until the current sort is done and uploaded. Tests and the editor preview use it; the renderer does not.</summary>
        public void CompleteNow()
        {
            if (jobInFlight) CollectPendingJob();
            while (phase != SlicePhase.Idle) AdvanceSlice(int.MaxValue);
        }

        // ---- One-shot mode

        private void Schedule(in SplatSortInput input)
        {
            float started = Time.realtimeSinceStartup;
            int slotCount = PrepareInput(input);

            JobHandle keyHandle = sliceKeyJob.Schedule(slotCount, 8192);
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
            LastSortMilliseconds = (Time.realtimeSinceStartup - started) * 1000f;
        }

        private void CollectPendingJob()
        {
            float started = Time.realtimeSinceStartup;
            pendingJob.Complete();
            jobInFlight = false;
            UploadOrder(sortedCount[0]);
            LastSortMilliseconds += (Time.realtimeSinceStartup - started) * 1000f;
        }

        // ---- Time-sliced mode

        private void SortSliced(in SplatSortInput input, bool resort)
        {
            if (phase == SlicePhase.Idle)
            {
                if (!resort) return;
                sliceSlotCount = PrepareInput(input);
                sliceCursor = 0;
                phase = SlicePhase.Keys;
                LastSortMilliseconds = 0f;
            }

            // A resort request while a pass is running is not honored: the pass finishes with the camera it started
            // with, and the next frame with a request starts a new one. Order lags, never tears.
            AdvanceSlice(options.SlotsPerFrame);
        }

        /// <summary>Runs up to <paramref name="slotBudget"/> slots of the current phase; moves to the next phase when the cursor reaches the end.</summary>
        private void AdvanceSlice(int slotBudget)
        {
            float started = Time.realtimeSinceStartup;
            int count = math.min(slotBudget, sliceSlotCount - sliceCursor);
            switch (phase)
            {
                case SlicePhase.Keys:
                    sliceKeyJob.SlotOffset = sliceCursor;
                    sliceKeyJob.Schedule(count, 8192).Complete();
                    sliceCursor += count;
                    if (sliceCursor >= sliceSlotCount) { phase = SlicePhase.Histogram; sliceCursor = 0; for (int bucket = 0; bucket < histogram.Length; bucket++) histogram[bucket] = 0; }
                    break;

                case SlicePhase.Histogram:
                    new HistogramRangeJob { Keys = keys, Start = sliceCursor, End = sliceCursor + count, Histogram = histogram }.Run();
                    sliceCursor += count;
                    if (sliceCursor >= sliceSlotCount) { phase = SlicePhase.Prefix; sliceCursor = 0; }
                    break;

                case SlicePhase.Prefix:
                    new PrefixSumJob { Histogram = histogram, SortedCount = sortedCount }.Run();
                    phase = SlicePhase.Scatter;
                    break;

                case SlicePhase.Scatter:
                    new ScatterRangeJob { Keys = keys, Start = sliceCursor, End = sliceCursor + count, VisibleChunks = visibleChunksCopy, Histogram = histogram, Order = order }.Run();
                    sliceCursor += count;
                    if (sliceCursor >= sliceSlotCount)
                    {
                        UploadOrder(sortedCount[0]);
                        phase = SlicePhase.Idle;
                    }
                    break;
            }

            LastSortMilliseconds += (Time.realtimeSinceStartup - started) * 1000f;
        }

        // ---- Shared

        /// <summary>Copies what the jobs read from the input (the caller's arrays change every frame) and fills the key job. Returns the slot count.</summary>
        private int PrepareInput(in SplatSortInput input)
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
            if (budgetsCopy.IsCreated) budgetsCopy.Dispose();
            bool hasBudgets = input.ChunkBudgets.IsCreated && input.ChunkBudgets.Length == input.VisibleChunks.Length;
            budgetsCopy = hasBudgets ? new NativeArray<int>(input.ChunkBudgets, Allocator.Persistent) : new NativeArray<int>(1, Allocator.Persistent);

            SplatSortKeys.LogRange(input.View.MinDepth, input.View.MaxDepth, out float logMinDepth, out float inverseLogDepthRange);
            sliceKeyJob = new KeyJob
            {
                Packed = input.Data.Packed,
                Chunks = input.Data.Chunks,
                VisibleChunks = visibleChunksCopy,
                Budgets = budgetsCopy,
                UseBudgets = hasBudgets,
                View = input.View,
                LogMinDepth = logMinDepth,
                InverseLogDepthRange = inverseLogDepthRange,
                MaxKey = maxKey,
                SlotOffset = 0,
                Keys = keys
            };
            return slotCount;
        }

        private void UploadOrder(int count)
        {
            // The texture stores one uint per RGBA8 texel, so the order array is the texel array (little-endian: byte 0 -> R).
            NativeArray<uint> texels = orderTexture.GetPixelData<uint>(0);
            NativeArray<uint>.Copy(order, 0, texels, 0, count);
            orderTexture.Apply(false, false);
            OrderedSplatCount = count;
        }

        public void Dispose()
        {
            if (jobInFlight) pendingJob.Complete();
            jobInFlight = false;
            phase = SlicePhase.Idle;
            if (keys.IsCreated) keys.Dispose();
            if (order.IsCreated) order.Dispose();
            if (histogram.IsCreated) histogram.Dispose();
            if (visibleChunksCopy.IsCreated) visibleChunksCopy.Dispose();
            if (budgetsCopy.IsCreated) budgetsCopy.Dispose();
            if (sortedCount.IsCreated) sortedCount.Dispose();
            if (orderTexture != null)
            {
                SplatObjectUtility.Destroy(orderTexture);
                orderTexture = null;
            }
        }

        /// <summary>
        /// One thread slot per (visible chunk, local index); slots past a partial chunk's end, culled splats and splats
        /// over the chunk's budget (P3) get EmptyKey. <see cref="SlotOffset"/> lets the sliced mode run a sub-range.
        /// </summary>
        [BurstCompile]
        private struct KeyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<uint4> Packed;
            [ReadOnly] public NativeArray<SplatChunkInfo> Chunks;
            [ReadOnly] public NativeArray<int> VisibleChunks;
            [ReadOnly] public NativeArray<int> Budgets;
            public bool UseBudgets;
            public SplatCameraView View;
            public float LogMinDepth;
            public float InverseLogDepthRange;
            public uint MaxKey;
            public int SlotOffset;
            [NativeDisableParallelForRestriction] public NativeArray<uint> Keys;

            public void Execute(int index)
            {
                int slot = SlotOffset + index;
                int visibleIndex = slot / SplatChunkInfo.Size;
                int chunkIndex = VisibleChunks[visibleIndex];
                int local = slot % SplatChunkInfo.Size;
                SplatChunkInfo chunk = Chunks[chunkIndex];
                int limit = UseBudgets ? math.min(chunk.SplatCount, Budgets[visibleIndex]) : chunk.SplatCount;
                if (local >= limit)
                {
                    Keys[slot] = SplatSortKeys.EmptyKey;
                    return;
                }

                int splatIndex = chunkIndex * SplatChunkInfo.Size + local;
                PackedSplat.Unpack(Packed[splatIndex], out float3 normalized, out float3 logScale, out _, out _, out _);
                float3 position = chunk.PositionOf(normalized);
                if (View.CullInKeys && !SplatVisibility.IsVisible(position, math.exp(logScale), View))
                {
                    Keys[slot] = SplatSortKeys.EmptyKey;
                    return;
                }

                float metric = SplatSortKeys.SortMetric(position, View.PositionLocal, View.ForwardLocal, View.Radial);
                Keys[slot] = SplatSortKeys.DepthToKey(metric, LogMinDepth, InverseLogDepthRange, MaxKey);
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
                CountingSortSteps.Count(Keys, 0, SlotCount, Histogram);
                SortedCount[0] = CountingSortSteps.ExclusivePrefixSum(Histogram);
                CountingSortSteps.Scatter(Keys, 0, SlotCount, VisibleChunks, Histogram, Order);
            }
        }

        [BurstCompile]
        private struct HistogramRangeJob : IJob
        {
            [ReadOnly] public NativeArray<uint> Keys;
            public int Start;
            public int End;
            public NativeArray<int> Histogram;

            public void Execute()
            {
                CountingSortSteps.Count(Keys, Start, End, Histogram);
            }
        }

        [BurstCompile]
        private struct PrefixSumJob : IJob
        {
            public NativeArray<int> Histogram;
            [WriteOnly] public NativeArray<int> SortedCount;

            public void Execute()
            {
                SortedCount[0] = CountingSortSteps.ExclusivePrefixSum(Histogram);
            }
        }

        [BurstCompile]
        private struct ScatterRangeJob : IJob
        {
            [ReadOnly] public NativeArray<uint> Keys;
            public int Start;
            public int End;
            [ReadOnly] public NativeArray<int> VisibleChunks;
            public NativeArray<int> Histogram;
            [WriteOnly] public NativeArray<uint> Order;

            public void Execute()
            {
                CountingSortSteps.Scatter(Keys, Start, End, VisibleChunks, Histogram, Order);
            }
        }
    }

    /// <summary>The three steps of the counting sort as plain functions, so the one-shot job and the sliced jobs share them.</summary>
    [BurstCompile]
    internal static class CountingSortSteps
    {
        public static void Count(NativeArray<uint> keys, int start, int end, NativeArray<int> histogram)
        {
            for (int slot = start; slot < end; slot++)
            {
                uint key = keys[slot];
                if (key != SplatSortKeys.EmptyKey) histogram[(int)key]++;
            }
        }

        /// <summary>Histogram[b] becomes the first output position of bucket b; returns the total = splats that got a slot.</summary>
        public static int ExclusivePrefixSum(NativeArray<int> histogram)
        {
            int runningTotal = 0;
            for (int bucket = 0; bucket < histogram.Length; bucket++)
            {
                int bucketCount = histogram[bucket];
                histogram[bucket] = runningTotal;
                runningTotal += bucketCount;
            }

            return runningTotal;
        }

        public static void Scatter(NativeArray<uint> keys, int start, int end, NativeArray<int> visibleChunks, NativeArray<int> histogram, NativeArray<uint> order)
        {
            for (int slot = start; slot < end; slot++)
            {
                uint key = keys[slot];
                if (key == SplatSortKeys.EmptyKey) continue;

                int splatIndex = visibleChunks[slot / SplatChunkInfo.Size] * SplatChunkInfo.Size + slot % SplatChunkInfo.Size;
                order[histogram[(int)key]] = (uint)splatIndex;
                histogram[(int)key]++;
            }
        }
    }
}
