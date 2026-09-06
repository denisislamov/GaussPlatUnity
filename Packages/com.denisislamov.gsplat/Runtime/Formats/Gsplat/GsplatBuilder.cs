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

            CoordinateConverter.ConvertToUnity(cloud, options.SourceCoordinateSystem);

            NativeArray<int> kept = SplatFilter.SelectIndices(cloud, options.PruneAlphaBelow, options.MaxSplatCount, Allocator.TempJob);
            NativeArray<int> order = kept;
            if (options.SpatialSort)
            {
                order = SplatSpatialSort.Order(cloud, kept, Allocator.TempJob);
                kept.Dispose();
            }

            SplatCloud ordered = SplatReorder.Apply(cloud, order, options.TargetShDegree, Allocator.TempJob);
            order.Dispose();
            try
            {
                return Pack(ordered, allocator);
            }
            finally
            {
                ordered.Dispose();
            }
        }

        /// <summary>Packs an already ordered cloud. Exposed for tests that want to control the order.</summary>
        public static GsplatData Pack(SplatCloud ordered, Allocator allocator = Allocator.Persistent)
        {
            if (ordered == null) throw new ArgumentNullException(nameof(ordered));

            ordered.ComputeBounds(out float3 boundsMin, out float3 boundsMax);
            var data = new GsplatData(ordered.Count, ordered.ShDegree, ordered.Antialiased, boundsMin, boundsMax, allocator);
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
