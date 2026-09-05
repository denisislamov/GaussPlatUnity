using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GSplat
{
    /// <summary>
    /// Depth sort on the GPU with the compute shader GSplatCountingSort.compute (see the comments there for why
    /// it avoids wave intrinsics). Records into a CommandBuffer so the renderer can put it right before the draw.
    /// TODO(E4): read positions from the packed splat texture + chunk table instead of a separate position buffer.
    /// </summary>
    public sealed class GpuCountingSorter : IDisposable
    {
        private const int Threads = 128;

        private readonly ComputeShader shader;
        private readonly int clearKernel;
        private readonly int keysKernel;
        private readonly int prefixKernel;
        private readonly int scatterKernel;

        private GraphicsBuffer keys;
        private GraphicsBuffer histogram;

        public static bool IsSupported => SystemInfo.supportsComputeShaders;

        public GpuCountingSorter(ComputeShader countingSortShader)
        {
            shader = countingSortShader ?? throw new ArgumentNullException(nameof(countingSortShader));
            if (!IsSupported) throw new NotSupportedException("This device has no compute shaders; use CpuCountingSorter.");

            clearKernel = shader.FindKernel("ClearHistogram");
            keysKernel = shader.FindKernel("ComputeKeys");
            prefixKernel = shader.FindKernel("PrefixSum");
            scatterKernel = shader.FindKernel("Scatter");
            histogram = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SplatSortKeys.BucketCount, sizeof(uint)) { name = "GSplat Sort Histogram" };
        }

        /// <summary>
        /// Records the four dispatches. <paramref name="positions"/> holds float4 per splat (xyz used), <paramref name="order"/>
        /// receives splat indices back to front. The depth range is given by the caller (from the object bounds) because
        /// a GPU min/max reduction would cost another two dispatches for no visible gain.
        /// </summary>
        public void Record(CommandBuffer commands, GraphicsBuffer positions, int splatCount, GraphicsBuffer order, float3 cameraPosition, float3 cameraForward, float minDepth, float maxDepth)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (order.count < splatCount) throw new ArgumentException("order is smaller than splatCount.", nameof(order));
            EnsureKeyCapacity(splatCount);

            int splatGroups = (splatCount + Threads - 1) / Threads;
            int bucketGroups = SplatSortKeys.BucketCount / Threads;

            commands.SetComputeIntParam(shader, "_SplatCount", splatCount);
            commands.SetComputeVectorParam(shader, "_CameraPosition", (Vector3)cameraPosition);
            commands.SetComputeVectorParam(shader, "_CameraForward", (Vector3)cameraForward);
            commands.SetComputeFloatParam(shader, "_MinDepth", minDepth);
            commands.SetComputeFloatParam(shader, "_InverseDepthRange", 1f / math.max(maxDepth - minDepth, 1e-6f));

            commands.SetComputeBufferParam(shader, clearKernel, "_Histogram", histogram);
            commands.DispatchCompute(shader, clearKernel, bucketGroups, 1, 1);

            commands.SetComputeBufferParam(shader, keysKernel, "_Positions", positions);
            commands.SetComputeBufferParam(shader, keysKernel, "_Keys", keys);
            commands.SetComputeBufferParam(shader, keysKernel, "_Histogram", histogram);
            commands.DispatchCompute(shader, keysKernel, splatGroups, 1, 1);

            commands.SetComputeBufferParam(shader, prefixKernel, "_Histogram", histogram);
            commands.DispatchCompute(shader, prefixKernel, 1, 1, 1);

            commands.SetComputeBufferParam(shader, scatterKernel, "_Keys", keys);
            commands.SetComputeBufferParam(shader, scatterKernel, "_Histogram", histogram);
            commands.SetComputeBufferParam(shader, scatterKernel, "_Order", order);
            commands.DispatchCompute(shader, scatterKernel, splatGroups, 1, 1);
        }

        private void EnsureKeyCapacity(int count)
        {
            if (keys != null && keys.count >= count) return;
            keys?.Dispose();
            keys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(count, 1), sizeof(uint)) { name = "GSplat Sort Keys" };
        }

        public void Dispose()
        {
            keys?.Dispose();
            keys = null;
            histogram?.Dispose();
            histogram = null;
        }
    }
}
