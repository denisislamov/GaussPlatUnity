using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>Packs every splat of a cloud into the 16-byte GPU layout, relative to its chunk center. See <see cref="PackedSplat"/>.</summary>
    [BurstCompile]
    public struct SplatPackJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> LogScales;
        [ReadOnly] public NativeArray<float4> Rotations;
        [ReadOnly] public NativeArray<float> Alphas;
        [ReadOnly] public NativeArray<float3> Colors;
        [ReadOnly] public NativeArray<SplatChunkInfo> Chunks;
        [WriteOnly] public NativeArray<uint4> Packed;

        public void Execute(int index)
        {
            float3 chunkCenter = Chunks[index / SplatChunkInfo.Size].Center;
            Packed[index] = PackedSplat.Pack(
                Positions[index] - chunkCenter,
                LogScales[index],
                Rotations[index],
                PackedSplat.DisplayColor(Colors[index]),
                Alphas[index]);
        }
    }

    /// <summary>Quantizes the higher SH coefficients to one byte each, the SPZ way.</summary>
    [BurstCompile]
    public struct ShQuantizeJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> Sh;
        [WriteOnly] public NativeArray<byte> Quantized;

        public void Execute(int index)
        {
            Quantized[index] = SpzQuantization.EncodeSh(Sh[index]);
        }
    }
}
