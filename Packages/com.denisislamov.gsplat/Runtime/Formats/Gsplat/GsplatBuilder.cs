using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Turns a decoded <see cref="SplatCloud"/> into GPU-ready <see cref="GsplatData"/>:
    /// convert axes -> drop transparent / over-budget splats -> Morton order -> chunk bounds -> pack.
    /// Main thread only because it schedules Burst jobs. The input cloud is modified in place (axis conversion)
    /// and is not disposed; the caller owns it.
    /// </summary>
    public static class GsplatBuilder
    {
        public static GsplatData Build(SplatCloud cloud, SplatImportOptions options, Allocator allocator = Allocator.Persistent)
        {
            if (cloud == null) throw new ArgumentNullException(nameof(cloud));
            if (options == null) throw new ArgumentNullException(nameof(options));

            // The same four steps SplatLoader runs with a frame between them (P7); here back to back.
            NativeArray<int> order = SelectAndOrder(cloud, options);
            SplatCloud ordered = Reorder(cloud, order, options);
            try
            {
                if (options.OrderChunksByImportance)
                {
                    SplatCloud byImportance = OrderChunksByImportance(ordered, Allocator.TempJob);
                    ordered.Dispose();
                    ordered = byImportance;
                }

                return Pack(ordered, allocator, options.OrderChunksByImportance);
            }
            finally
            {
                ordered.Dispose();
            }
        }

        /// <summary>Step 1: axis conversion (in place), pruning and budget, Morton order. Returns the order of the kept splats (TempJob, caller disposes through <see cref="Reorder"/>).</summary>
        public static NativeArray<int> SelectAndOrder(SplatCloud cloud, SplatImportOptions options)
        {
            CoordinateConverter.ConvertToUnity(cloud, options.SourceCoordinateSystem);

            NativeArray<int> kept = SplatFilter.SelectIndices(cloud, options.PruneAlphaBelow, options.MaxSplatCount, Allocator.TempJob);
            if (!options.SpatialSort) return kept;

            NativeArray<int> order = SplatSpatialSort.Order(cloud, kept, Allocator.TempJob);
            kept.Dispose();
            return order;
        }

        /// <summary>Step 2: the kept splats in Morton order as a new cloud (SH capped at the target degree). Disposes <paramref name="order"/>.</summary>
        public static SplatCloud Reorder(SplatCloud cloud, NativeArray<int> order, SplatImportOptions options)
        {
            SplatCloud ordered = SplatReorder.Apply(cloud, order, options.TargetShDegree, Allocator.TempJob);
            order.Dispose();
            return ordered;
        }

        /// <summary>
        /// P3: keeps every splat in its chunk (Morton order decided the chunks) but sorts the splats of each chunk by
        /// importance, most important first. A chunk's first k splats are then its best k-splat approximation, which
        /// is what the per-chunk budget in the key pass draws.
        /// </summary>
        public static SplatCloud OrderChunksByImportance(SplatCloud ordered, Allocator allocator)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));

            var identity = new NativeArray<int>(ordered.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int splatIndex = 0; splatIndex < identity.Length; splatIndex++) identity[splatIndex] = splatIndex;
            var keys = new NativeArray<ulong>(ordered.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            SplatFilter.WriteImportanceKeys(ordered, identity, keys);
            identity.Dispose();

            // Sort each chunk's keys on their own: the high bits order by importance, the low bits are the source index.
            for (int chunkIndex = 0; chunkIndex < SplatChunkInfo.ChunkCountFor(ordered.Count); chunkIndex++)
            {
                int first = chunkIndex * SplatChunkInfo.Size;
                int count = math.min(SplatChunkInfo.Size, ordered.Count - first);
                keys.GetSubArray(first, count).Sort();
            }

            var newOrder = new NativeArray<int>(ordered.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int splatIndex = 0; splatIndex < newOrder.Length; splatIndex++) newOrder[splatIndex] = (int)(keys[splatIndex] & 0xFFFFFFFF);
            keys.Dispose();

            SplatCloud result = SplatReorder.Apply(ordered, newOrder, ordered.ShDegree, allocator);
            newOrder.Dispose();
            return result;
        }

        /// <summary>Packs an already ordered cloud. Exposed for tests that want to control the order.</summary>
        public static GsplatData Pack(SplatCloud ordered, Allocator allocator = Allocator.Persistent, bool importanceOrdered = false)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));

            ordered.ComputeBounds(out float3 boundsMin, out float3 boundsMax);
            var data = new GsplatData(ordered.Count, ordered.ShDegree, ordered.Antialiased, boundsMin, boundsMax, allocator, importanceOrdered);
            try
            {
                ComputeChunkBounds(ordered, data);

                var packJob = new SplatPackJob
                {
                    Positions = ordered.Positions,
                    LogScales = ordered.LogScales,
                    Rotations = ordered.Rotations,
                    Alphas = ordered.Alphas,
                    Colors = ordered.Colors,
                    Chunks = data.Chunks,
                    Packed = data.Packed
                };
                JobHandle packHandle = packJob.Schedule(ordered.Count, 4096);

                JobHandle shHandle = default;
                if (data.Sh.Length > 0)
                {
                    var shJob = new ShQuantizeJob { Sh = ordered.Sh, Quantized = data.Sh };
                    shHandle = shJob.Schedule(data.Sh.Length, 16384);
                }

                JobHandle.CombineDependencies(packHandle, shHandle).Complete();
            }
            catch
            {
                data.Dispose();
                throw;
            }

            return data;
        }

        private static void ComputeChunkBounds(SplatCloud ordered, GsplatData data)
        {
            for (int chunkIndex = 0; chunkIndex < data.ChunkCount; chunkIndex++)
            {
                int first = chunkIndex * SplatChunkInfo.Size;
                int count = math.min(SplatChunkInfo.Size, ordered.Count - first);
                float3 min = new float3(float.MaxValue);
                float3 max = new float3(float.MinValue);
                float largestScale = 0f;
                for (int splatIndex = first; splatIndex < first + count; splatIndex++)
                {
                    min = math.min(min, ordered.Positions[splatIndex]);
                    max = math.max(max, ordered.Positions[splatIndex]);
                    largestScale = math.max(largestScale, math.cmax(math.exp(ordered.LogScales[splatIndex])));
                }

                // Centers alone under-estimate what the chunk draws: a splat reaches ~3 standard deviations out. The
                // padding is kept separately so positions can be packed against the tight (unpadded) bounds.
                data.Chunks[chunkIndex] = new SplatChunkInfo(count, min, max, 3f * largestScale);
            }
        }
    }
}
