using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Picks which splats survive import: drops the (nearly) transparent ones and, when a budget is set, keeps
    /// the most important ones. Importance = opacity x approximate surface area, so big visible splats stay
    /// and tiny faint "dust" goes first. Main thread only (schedules Burst jobs).
    /// </summary>
    public static class SplatFilter
    {
        /// <summary>Indices of the splats to keep, in their original order. Caller disposes.</summary>
        public static NativeArray<int> SelectIndices(SplatCloud cloud, float pruneAlphaBelow, int maxSplatCount, Allocator allocator)
        {
            if (cloud == null) throw new ArgumentNullException(nameof(cloud));

            var survivors = new NativeList<int>(cloud.Count, Allocator.TempJob);
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                if (cloud.Alphas[splatIndex] >= pruneAlphaBelow) survivors.Add(splatIndex);
            }

            bool overBudget = maxSplatCount > 0 && survivors.Length > maxSplatCount;
            if (!overBudget)
            {
                var result = new NativeArray<int>(survivors.AsArray(), allocator);
                survivors.Dispose();
                return result;
            }

            // Sort key: descending importance in the high 32 bits, original index in the low 32 bits. Positive
            // floats keep their order when compared as uint bit patterns, so ~bits gives a descending sort.
            var keys = new NativeArray<ulong>(survivors.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            WriteImportanceKeys(cloud, survivors.AsArray(), keys);
            keys.SortJob().Schedule().Complete();

            var kept = new NativeArray<int>(maxSplatCount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int keptIndex = 0; keptIndex < maxSplatCount; keptIndex++)
            {
                kept[keptIndex] = (int)(keys[keptIndex] & 0xFFFFFFFF);
            }

            keys.Dispose();
            survivors.Dispose();

            // Back to file order so the later spatial sort starts from a deterministic input.
            kept.Sort();
            return kept;
        }

        /// <summary>
        /// For each candidate: descending importance in the high 32 bits, the splat index in the low 32, so an ascending
        /// sort of the keys lists the most important splats first. Shared with the chunk ordering of P3.
        /// </summary>
        public static void WriteImportanceKeys(SplatCloud cloud, NativeArray<int> candidates, NativeArray<ulong> keys)
        {
            var keyJob = new ImportanceKeyJob
            {
                Candidates = candidates,
                Alphas = cloud.Alphas,
                LogScales = cloud.LogScales,
                Keys = keys
            };
            keyJob.Schedule(candidates.Length, 4096).Complete();
        }

        [BurstCompile]
        private struct ImportanceKeyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> Candidates;
            [ReadOnly] public NativeArray<float> Alphas;
            [ReadOnly] public NativeArray<float3> LogScales;
            [WriteOnly] public NativeArray<ulong> Keys;

            public void Execute(int index)
            {
                int splatIndex = Candidates[index];
                float3 scale = math.exp(LogScales[splatIndex]);
                // Sum of the three axis-pair products ~ surface area of the ellipsoid up to a constant.
                float area = scale.x * scale.y + scale.y * scale.z + scale.z * scale.x;
                float importance = Alphas[splatIndex] * area;
                uint descending = ~math.asuint(math.max(importance, 0f));
                Keys[index] = ((ulong)descending << 32) | (uint)splatIndex;
            }
        }
    }
}
