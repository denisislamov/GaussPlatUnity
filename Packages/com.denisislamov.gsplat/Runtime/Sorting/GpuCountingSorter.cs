using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GSplat
{
    /// <summary>
    /// Depth sort on the GPU with GSplatCountingSort.compute (see the comments there for why it avoids wave
    /// intrinsics). Writes the order texture (a RenderTexture with random write) inside the frame, right before the
    /// splats are drawn, so the order is never stale.
    /// </summary>
    public sealed class GpuCountingSorter : ISplatSorter
    {
        private const int Threads = 128;

        private static readonly int SplatsId = Shader.PropertyToID("_Splats");
        private static readonly int ChunksId = Shader.PropertyToID("_Chunks");
        private static readonly int VisibleChunksId = Shader.PropertyToID("_VisibleChunks");
        private static readonly int KeysId = Shader.PropertyToID("_Keys");
        private static readonly int HistogramId = Shader.PropertyToID("_Histogram");
        private static readonly int OrderId = Shader.PropertyToID("_Order");
        private static readonly int SlotCountId = Shader.PropertyToID("_SlotCount");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        private static readonly int CameraForwardId = Shader.PropertyToID("_CameraForward");
        private static readonly int LogMinDepthId = Shader.PropertyToID("_LogMinDepth");
        private static readonly int InverseLogDepthRangeId = Shader.PropertyToID("_InverseLogDepthRange");
        private static readonly int DrawArgsId = Shader.PropertyToID("_DrawArgs");
        private static readonly int CullInKeysId = Shader.PropertyToID("_CullInKeys");
        private static readonly int SortRadialId = Shader.PropertyToID("_SortRadial");
        private static readonly int LocalToClipId = Shader.PropertyToID("_LocalToClip");
        private static readonly int FocalPixelsYId = Shader.PropertyToID("_FocalPixelsY");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");
        private static readonly int MaxStdDevId = Shader.PropertyToID("_MaxStdDev");
        private static readonly int MinPixelRadiusId = Shader.PropertyToID("_MinPixelRadius");

        private readonly ComputeShader shader;
        private readonly int clearKernel;
        private readonly int keysKernel;
        private readonly int prefixKernel;
        private readonly int scatterKernel;

        private RenderTexture orderTexture;
        private GraphicsBuffer keys;
        private GraphicsBuffer histogram;
        private ComputeBuffer drawArgs;
        private SplatSortInput input;
        private bool hasInput;

        public Texture OrderTexture => orderTexture;
        public int OrderedSplatCount { get; private set; }
        public ComputeBuffer DrawArgs => drawArgs;

        public static bool IsSupported => SystemInfo.supportsComputeShaders;

        /// <summary>Loads the compute shader from Resources; null when compute is unavailable so callers fall back to the CPU sorter.</summary>
        public static ComputeShader LoadShader()
        {
            return IsSupported ? Resources.Load<ComputeShader>("GSplatCountingSort") : null;
        }

        public GpuCountingSorter(ComputeShader countingSortShader, int capacity)
        {
            shader = countingSortShader ?? throw new ArgumentNullException(nameof(countingSortShader));
            if (!IsSupported) throw new NotSupportedException("This device has no compute shaders; use CpuCountingSorter.");
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            clearKernel = shader.FindKernel("ClearHistogram");
            keysKernel = shader.FindKernel("ComputeKeys");
            prefixKernel = shader.FindKernel("PrefixSum");
            scatterKernel = shader.FindKernel("Scatter");

            histogram = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SplatSortKeys.BucketCount, sizeof(uint)) { name = "GSplat Sort Histogram" };
            // {indexCount, instanceCount, startIndex, baseVertex, startInstance}; the sort fills instanceCount.
            drawArgs = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "GSplat Draw Args" };
            drawArgs.SetData(new uint[] { 6, 0, 0, 0, 0 });
            // Slots are per visible chunk, so round the capacity up to whole chunks.
            int slotCapacity = SplatChunkInfo.ChunkCountFor(capacity) * SplatChunkInfo.Size;
            keys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, slotCapacity, sizeof(uint)) { name = "GSplat Sort Keys" };

            orderTexture = new RenderTexture(SplatOrderTexture.Width, SplatOrderTexture.RowsFor(capacity), 0, GraphicsFormat.R8G8B8A8_UNorm)
            {
                name = "GSplat Order (GPU)",
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            orderTexture.Create();
        }

        public void Sort(in SplatSortInput sortInput, bool resort)
        {
            // Compute is cheap enough to run whenever the renderer asks; "resort == false" only means the caller
            // would accept the old order, and RecordCompute then draws nothing new.
            input = sortInput;
            hasInput = resort;
            if (resort) OrderedSplatCount = sortInput.VisibleSplatCount;
        }

        public void RecordCompute(CommandBuffer commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (!hasInput || input.VisibleChunks.Length == 0) return;
            if (input.VisibleChunkBuffer == null) throw new InvalidOperationException("The GPU sorter needs SplatSortInput.VisibleChunkBuffer.");

            int slotCount = input.VisibleChunks.Length * SplatChunkInfo.Size;
            int slotGroups = slotCount / Threads;
            int bucketGroups = SplatSortKeys.BucketCount / Threads;

            commands.SetComputeIntParam(shader, SlotCountId, slotCount);
            SetViewParams(commands, input.View);

            commands.SetComputeBufferParam(shader, clearKernel, HistogramId, histogram);
            commands.DispatchCompute(shader, clearKernel, bucketGroups, 1, 1);

            commands.SetComputeTextureParam(shader, keysKernel, SplatsId, input.Gpu.SplatTexture);
            commands.SetComputeBufferParam(shader, keysKernel, ChunksId, input.Gpu.ChunkBuffer);
            commands.SetComputeBufferParam(shader, keysKernel, VisibleChunksId, input.VisibleChunkBuffer);
            commands.SetComputeBufferParam(shader, keysKernel, KeysId, keys);
            commands.SetComputeBufferParam(shader, keysKernel, HistogramId, histogram);
            commands.DispatchCompute(shader, keysKernel, slotGroups, 1, 1);

            commands.SetComputeBufferParam(shader, prefixKernel, HistogramId, histogram);
            commands.SetComputeBufferParam(shader, prefixKernel, DrawArgsId, drawArgs);
            commands.DispatchCompute(shader, prefixKernel, 1, 1, 1);

            commands.SetComputeBufferParam(shader, scatterKernel, VisibleChunksId, input.VisibleChunkBuffer);
            commands.SetComputeBufferParam(shader, scatterKernel, KeysId, keys);
            commands.SetComputeBufferParam(shader, scatterKernel, HistogramId, histogram);
            commands.SetComputeTextureParam(shader, scatterKernel, OrderId, orderTexture);
            commands.DispatchCompute(shader, scatterKernel, slotGroups, 1, 1);

            hasInput = false;
        }

        /// <summary>The camera block of the key kernel; the same fields the CPU KeyJob reads from SplatCameraView.</summary>
        private void SetViewParams(CommandBuffer commands, in SplatCameraView view)
        {
            commands.SetComputeVectorParam(shader, CameraPositionId, (Vector3)view.PositionLocal);
            commands.SetComputeVectorParam(shader, CameraForwardId, (Vector3)view.ForwardLocal);
            SplatSortKeys.LogRange(view.MinDepth, view.MaxDepth, out float logMinDepth, out float inverseLogDepthRange);
            commands.SetComputeFloatParam(shader, LogMinDepthId, logMinDepth);
            commands.SetComputeFloatParam(shader, InverseLogDepthRangeId, inverseLogDepthRange);
            commands.SetComputeIntParam(shader, CullInKeysId, view.CullInKeys ? 1 : 0);
            commands.SetComputeIntParam(shader, SortRadialId, view.Radial ? 1 : 0);
            commands.SetComputeMatrixParam(shader, LocalToClipId, view.LocalToClip);
            commands.SetComputeFloatParam(shader, FocalPixelsYId, view.FocalPixelsY);
            commands.SetComputeVectorParam(shader, ScreenSizeId, new Vector4(view.ScreenSize.x, view.ScreenSize.y, 0f, 0f));
            commands.SetComputeFloatParam(shader, MaxStdDevId, view.MaxStdDev);
            commands.SetComputeFloatParam(shader, MinPixelRadiusId, view.MinPixelRadius);
        }

        public void Dispose()
        {
            keys?.Dispose();
            keys = null;
            histogram?.Dispose();
            histogram = null;
            drawArgs?.Dispose();
            drawArgs = null;
            if (orderTexture != null)
            {
                orderTexture.Release();
                SplatObjectUtility.Destroy(orderTexture);
                orderTexture = null;
            }
        }
    }
}
