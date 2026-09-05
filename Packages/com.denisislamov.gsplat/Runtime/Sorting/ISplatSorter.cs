using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GSplat
{
    /// <summary>
    /// Produces the back-to-front draw order of the visible splats as an RGBA8 "order texture": slot s (texel
    /// s % 4096, s / 4096) holds the splat index as four bytes. A texture, not a buffer, because the WebGL2 /
    /// GLES 3.0 vertex shader can read nothing else, and one shader for every platform is the whole point.
    /// Two implementations: <see cref="CpuCountingSorter"/> (Burst jobs, result one frame late) and
    /// <see cref="GpuCountingSorter"/> (compute, result in the same frame).
    /// </summary>
    public interface ISplatSorter : IDisposable
    {
        /// <summary>The order texture. Its content is valid for the first <see cref="OrderedSplatCount"/> slots.</summary>
        Texture OrderTexture { get; }

        /// <summary>
        /// Slots that hold a valid order. The CPU sorter knows the exact count; the GPU sorter only knows an upper
        /// bound here (the exact count is in <see cref="DrawArgs"/>, on the GPU).
        /// </summary>
        int OrderedSplatCount { get; }

        /// <summary>
        /// GPU sorter: indirect draw arguments (6 indices, instanceCount written by the sort, 0, 0, 0) so the draw
        /// covers exactly the splats that got a slot without a CPU round trip. Null for the CPU sorter.
        /// </summary>
        ComputeBuffer DrawArgs { get; }

        /// <summary>True when <see cref="RecordCompute"/> must run before drawing (GPU sorter).</summary>
        bool NeedsCompute { get; }

        /// <summary>
        /// Main-thread step, once per camera per frame. The CPU sorter finishes the previous job here and starts a new
        /// one when <paramref name="resort"/> is set; the GPU sorter only remembers the input for RecordCompute.
        /// </summary>
        void PrepareOnMainThread(in SplatSortInput input, bool resort);

        /// <summary>
        /// GPU step: records the compute dispatches into the frame. No-op for the CPU sorter. A plain CommandBuffer
        /// because the order texture is not a RenderGraph resource and the graph's compute wrapper only binds handles.
        /// </summary>
        void RecordCompute(CommandBuffer commands);
    }

    /// <summary>Shared sizing of order textures.</summary>
    public static class SplatOrderTexture
    {
        public const int Width = 4096;

        public static int RowsFor(int capacity)
        {
            return Mathf.Max(1, (capacity + Width - 1) / Width);
        }
    }
}
