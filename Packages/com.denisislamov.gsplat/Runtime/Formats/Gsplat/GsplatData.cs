using System;
using Unity.Collections;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// A splat scene ready for the GPU: packed 16-byte splats grouped in spatial chunks, plus optional quantized
    /// SH. This is what the importer stores (see <see cref="GsplatFile"/>) and what the runtime loader produces,
    /// so both paths end in the same object. Dispose releases the native memory.
    /// </summary>
    public sealed class GsplatData : IDisposable
    {
        public readonly int SplatCount;
        public readonly int ShDegree;
        public readonly bool Antialiased;

        /// <summary>P3: inside every chunk the splats are ordered by importance (opacity x area) descending, so a prefix of a chunk is a valid lower level of detail.</summary>
        public readonly bool ImportanceOrdered;
        public readonly float3 BoundsMin;
        public readonly float3 BoundsMax;

        /// <summary>One entry per chunk, in splat order. Chunk i covers splats [i * Size, i * Size + SplatCount).</summary>
        public NativeArray<SplatChunkInfo> Chunks;

        /// <summary>One <see cref="PackedSplat"/> per splat.</summary>
        public NativeArray<uint4> Packed;

        /// <summary>SH above degree 0, quantized like SPZ (SpzQuantization.EncodeSh), ShCoefficientCount * 3 bytes per splat. Empty for degree 0.</summary>
        public NativeArray<byte> Sh;

        public int ChunkCount => Chunks.Length;
        public int ShCoefficientCount => ShMath.CoefficientCount(ShDegree);
        public int ShBytesPerSplat => ShCoefficientCount * 3;

        /// <summary>Native memory held by this object, for the memory budget (E6-T4).</summary>
        public long NativeMemoryBytes => (long)Packed.Length * PackedSplat.SizeInBytes + Sh.Length + (long)Chunks.Length * 48;

        public GsplatData(int splatCount, int shDegree, bool antialiased, float3 boundsMin, float3 boundsMax, Allocator allocator = Allocator.Persistent, bool importanceOrdered = false)
        {
            ImportanceOrdered = importanceOrdered;
            if (splatCount < 0) throw new ArgumentOutOfRangeException(nameof(splatCount));
            if (shDegree < 0 || shDegree > ShMath.MaxDegree) throw new ArgumentOutOfRangeException(nameof(shDegree));

            SplatCount = splatCount;
            ShDegree = shDegree;
            Antialiased = antialiased;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            Chunks = new NativeArray<SplatChunkInfo>(SplatChunkInfo.ChunkCountFor(splatCount), allocator, NativeArrayOptions.ClearMemory);
            Packed = new NativeArray<uint4>(splatCount, allocator, NativeArrayOptions.UninitializedMemory);
            Sh = new NativeArray<byte>(splatCount * ShBytesPerSplat, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public void Dispose()
        {
            if (Chunks.IsCreated) Chunks.Dispose();
            if (Packed.IsCreated) Packed.Dispose();
            if (Sh.IsCreated) Sh.Dispose();
        }
    }
}
