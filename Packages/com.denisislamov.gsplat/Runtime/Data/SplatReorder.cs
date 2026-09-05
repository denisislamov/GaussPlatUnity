using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>Builds a new cloud that holds source[order[i]] at position i, optionally with fewer SH degrees. Main thread only.</summary>
    public static class SplatReorder
    {
        public static SplatCloud Apply(SplatCloud source, NativeArray<int> order, int targetShDegree, Allocator allocator = Allocator.Persistent)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int shDegree = math.min(source.ShDegree, targetShDegree);
            var result = new SplatCloud(order.Length, shDegree, source.Antialiased, allocator);

            var job = new ReorderJob
            {
                Order = order,
                SourcePositions = source.Positions,
                SourceLogScales = source.LogScales,
                SourceRotations = source.Rotations,
                SourceAlphas = source.Alphas,
                SourceColors = source.Colors,
                SourceSh = source.Sh,
                SourceShFloats = source.ShFloatsPerSplat,
                TargetShFloats = result.ShFloatsPerSplat,
                Positions = result.Positions,
                LogScales = result.LogScales,
                Rotations = result.Rotations,
                Alphas = result.Alphas,
                Colors = result.Colors,
                Sh = result.Sh
            };
            job.Schedule(order.Length, 2048).Complete();
            return result;
        }

        [BurstCompile]
        private struct ReorderJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> Order;
            [ReadOnly] public NativeArray<float3> SourcePositions;
            [ReadOnly] public NativeArray<float3> SourceLogScales;
            [ReadOnly] public NativeArray<float4> SourceRotations;
            [ReadOnly] public NativeArray<float> SourceAlphas;
            [ReadOnly] public NativeArray<float3> SourceColors;
            [ReadOnly] public NativeArray<float> SourceSh;
            public int SourceShFloats;
            public int TargetShFloats;

            [WriteOnly] public NativeArray<float3> Positions;
            [WriteOnly] public NativeArray<float3> LogScales;
            [WriteOnly] public NativeArray<float4> Rotations;
            [WriteOnly] public NativeArray<float> Alphas;
            [WriteOnly] public NativeArray<float3> Colors;
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Sh;

            public void Execute(int index)
            {
                int sourceIndex = Order[index];
                Positions[index] = SourcePositions[sourceIndex];
                LogScales[index] = SourceLogScales[sourceIndex];
                Rotations[index] = SourceRotations[sourceIndex];
                Alphas[index] = SourceAlphas[sourceIndex];
                Colors[index] = SourceColors[sourceIndex];

                // Coefficients are ordered by degree, so keeping a lower degree is keeping a prefix.
                int sourceBase = sourceIndex * SourceShFloats;
                int targetBase = index * TargetShFloats;
                for (int floatIndex = 0; floatIndex < TargetShFloats; floatIndex++)
                {
                    Sh[targetBase + floatIndex] = SourceSh[sourceBase + floatIndex];
                }
            }
        }
    }
}
