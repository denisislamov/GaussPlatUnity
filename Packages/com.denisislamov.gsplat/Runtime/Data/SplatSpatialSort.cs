using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Orders splats along a Morton (Z-order) curve so that consecutive splats are close in space. Trainers
    /// write splats in an arbitrary order; without this, every 65k chunk would span the whole scene, chunk
    /// culling would cull nothing and float16 positions relative to the chunk center would be too coarse.
    /// Main thread only (schedules Burst jobs).
    /// </summary>
    public static class SplatSpatialSort
    {
        /// <summary>Bits per axis in the Morton key. 10 bits x 3 axes = 30 bits, which fits a uint with room to spare.</summary>
        public const int BitsPerAxis = 10;

        /// <summary>
        /// Returns <paramref name="candidates"/> reordered along the Morton curve over the bounds of those candidates.
        /// Caller disposes the result.
        /// </summary>
        public static NativeArray<int> Order(SplatCloud cloud, NativeArray<int> candidates, Allocator allocator)
        {
            if (cloud == null) throw new ArgumentNullException(nameof(cloud));

            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                float3 position = cloud.Positions[candidates[candidateIndex]];
                min = math.min(min, position);
                max = math.max(max, position);
            }

            // One scale for all three axes, so Morton cells are cubes. Scaling each axis to its own range would make
            // a 2 m tall, 200 m wide scene sort by height first and put far-apart splats in the same chunk.
            float inverseExtent = 1f / math.max(math.cmax(max - min), 1e-6f);
            var keys = new NativeArray<ulong>(candidates.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var keyJob = new MortonKeyJob
            {
                Candidates = candidates,
                Positions = cloud.Positions,
                BoundsMin = min,
                InverseExtent = new float3(inverseExtent),
                Keys = keys
            };
            keyJob.Schedule(candidates.Length, 4096).Complete();
            keys.SortJob().Schedule().Complete();

            var order = new NativeArray<int>(candidates.Length, allocator, NativeArrayOptions.UninitializedMemory);
            for (int sortedIndex = 0; sortedIndex < order.Length; sortedIndex++)
            {
                order[sortedIndex] = (int)(keys[sortedIndex] & 0xFFFFFFFF);
            }

            keys.Dispose();
            return order;
        }

        /// <summary>Interleaves the bits of three 10-bit coordinates: x in bits 0,3,6..., y in 1,4,7..., z in 2,5,8...</summary>
        public static uint MortonCode(uint3 cell)
        {
            return SpreadBits(cell.x) | (SpreadBits(cell.y) << 1) | (SpreadBits(cell.z) << 2);
        }

        /// <summary>Puts two zero bits between every bit of a 10-bit value (the classic magic-number expansion).</summary>
        private static uint SpreadBits(uint value)
        {
            value &= 0x3FF;
            value = (value | (value << 16)) & 0x030000FF;
            value = (value | (value << 8)) & 0x0300F00F;
            value = (value | (value << 4)) & 0x030C30C3;
            value = (value | (value << 2)) & 0x09249249;
            return value;
        }

        [BurstCompile]
        private struct MortonKeyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> Candidates;
            [ReadOnly] public NativeArray<float3> Positions;
            public float3 BoundsMin;
            public float3 InverseExtent;
            [WriteOnly] public NativeArray<ulong> Keys;

            public void Execute(int index)
            {
                int splatIndex = Candidates[index];
                float3 normalized = math.saturate((Positions[splatIndex] - BoundsMin) * InverseExtent);
                uint3 cell = (uint3)math.min(normalized * ((1 << BitsPerAxis) - 1) + 0.5f, (1 << BitsPerAxis) - 1);
                uint morton = MortonCode(cell);
                // Index in the low bits makes the sort deterministic for equal cells.
                Keys[index] = ((ulong)morton << 32) | (uint)splatIndex;
            }
        }
    }
}
