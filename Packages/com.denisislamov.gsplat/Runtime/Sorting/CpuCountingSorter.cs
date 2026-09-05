using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Depth sort on the CPU with Burst: one pass to find the depth range, one to make 16-bit keys, one counting
    /// sort (histogram, prefix sum, scatter). Counting sort because it is O(n) with a 64k table and needs no
    /// comparisons; Spark's worker sorts the same way. This is the path for WebGL2 and for GPUs without compute.
    /// Main thread only (schedules jobs); the result is ready when <see cref="Sort"/> returns.
    /// TODO(E4): make it asynchronous - schedule here, complete next frame - so the main thread never waits.
    /// </summary>
    public sealed class CpuCountingSorter : IDisposable
    {
        private NativeArray<uint> keys;
        private NativeArray<int> histogram;
        private NativeArray<float> depthRange;

        public CpuCountingSorter()
        {
            histogram = new NativeArray<int>(SplatSortKeys.BucketCount, Allocator.Persistent);
            depthRange = new NativeArray<float>(2, Allocator.Persistent);
        }

        /// <summary>Writes into <paramref name="order"/> the splat indices back to front. Arrays must have the same length.</summary>
        public void Sort(NativeArray<float3> positions, float3 cameraPosition, float3 cameraForward, NativeArray<uint> order)
        {
            if (order.Length != positions.Length) throw new ArgumentException("order must have one slot per position.", nameof(order));
            EnsureKeyCapacity(positions.Length);

            var rangeJob = new DepthRangeJob { Positions = positions, CameraPosition = cameraPosition, CameraForward = cameraForward, Range = depthRange };
            JobHandle rangeHandle = rangeJob.Schedule();

            var keyJob = new KeyJob { Positions = positions, CameraPosition = cameraPosition, CameraForward = cameraForward, Range = depthRange, Keys = keys };
            JobHandle keyHandle = keyJob.Schedule(positions.Length, 8192, rangeHandle);

            var sortJob = new CountingSortJob { Keys = keys, Count = positions.Length, Histogram = histogram, Order = order };
            sortJob.Schedule(keyHandle).Complete();
        }

        private void EnsureKeyCapacity(int count)
        {
            if (keys.IsCreated && keys.Length >= count) return;
            if (keys.IsCreated) keys.Dispose();
            keys = new NativeArray<uint>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public void Dispose()
        {
            if (keys.IsCreated) keys.Dispose();
            if (histogram.IsCreated) histogram.Dispose();
            if (depthRange.IsCreated) depthRange.Dispose();
        }

        [BurstCompile]
        private struct DepthRangeJob : IJob
        {
            [ReadOnly] public NativeArray<float3> Positions;
            public float3 CameraPosition;
            public float3 CameraForward;
            [WriteOnly] public NativeArray<float> Range;

            public void Execute()
            {
                float min = float.MaxValue;
                float max = float.MinValue;
                for (int splatIndex = 0; splatIndex < Positions.Length; splatIndex++)
                {
                    float depth = SplatSortKeys.ViewDepth(Positions[splatIndex], CameraPosition, CameraForward);
                    min = math.min(min, depth);
                    max = math.max(max, depth);
                }

                Range[0] = min;
                Range[1] = max;
            }
        }

        [BurstCompile]
        private struct KeyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Positions;
            public float3 CameraPosition;
            public float3 CameraForward;
            [ReadOnly] public NativeArray<float> Range;
            [WriteOnly] public NativeArray<uint> Keys;

            public void Execute(int index)
            {
                float inverseRange = 1f / math.max(Range[1] - Range[0], 1e-6f);
                float depth = SplatSortKeys.ViewDepth(Positions[index], CameraPosition, CameraForward);
                Keys[index] = SplatSortKeys.DepthToKey(depth, Range[0], inverseRange);
            }
        }

        [BurstCompile]
        private struct CountingSortJob : IJob
        {
            [ReadOnly] public NativeArray<uint> Keys;
            public int Count;
            public NativeArray<int> Histogram;
            [WriteOnly] public NativeArray<uint> Order;

            public void Execute()
            {
                for (int bucket = 0; bucket < Histogram.Length; bucket++) Histogram[bucket] = 0;
                for (int splatIndex = 0; splatIndex < Count; splatIndex++) Histogram[(int)Keys[splatIndex]]++;

                // Exclusive prefix sum: Histogram[b] becomes the first output slot of bucket b.
                int runningTotal = 0;
                for (int bucket = 0; bucket < Histogram.Length; bucket++)
                {
                    int bucketCount = Histogram[bucket];
                    Histogram[bucket] = runningTotal;
                    runningTotal += bucketCount;
                }

                for (int splatIndex = 0; splatIndex < Count; splatIndex++)
                {
                    int bucket = (int)Keys[splatIndex];
                    Order[Histogram[bucket]] = (uint)splatIndex;
                    Histogram[bucket]++;
                }
            }
        }
    }
}
