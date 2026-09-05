using System;
using Unity.Collections;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Decoded Gaussian splats in memory, one flat array per attribute, in whatever coordinate system the
    /// file used (see <see cref="CoordinateConverter"/> to bring them into Unity space).
    /// NativeArrays because the Burst jobs of the packer read them directly without a copy. Dispose when done.
    /// </summary>
    public sealed class SplatCloud : IDisposable
    {
        public readonly int Count;
        public readonly int ShDegree;

        /// <summary>The scene was trained with mip-splatting style antialiasing; the renderer must dilate 2D covariance the same way.</summary>
        public readonly bool Antialiased;

        /// <summary>Ellipsoid centers.</summary>
        public NativeArray<float3> Positions;

        /// <summary>Natural log of the ellipsoid half-axes, the unit SPZ and 3DGS PLY use. exp() happens when packing.</summary>
        public NativeArray<float3> LogScales;

        /// <summary>Rotation quaternion as xyzw, normalized.</summary>
        public NativeArray<float4> Rotations;

        /// <summary>Opacity in [0, 1]. The sigmoid of the PLY "opacity" logit is already applied.</summary>
        public NativeArray<float> Alphas;

        /// <summary>Zeroth SH coefficient per channel. Display color = 0.5 + ShMath.Sh0Scale * value.</summary>
        public NativeArray<float3> Colors;

        /// <summary>
        /// SH coefficients above degree 0: ShCoefficientCount * 3 floats per splat, coefficient-major with RGB
        /// interleaved (r0 g0 b0 r1 g1 b1 ...), the SPZ order. Length 0 for degree 0.
        /// </summary>
        public NativeArray<float> Sh;

        public int ShCoefficientCount => ShMath.CoefficientCount(ShDegree);

        /// <summary>Floats of SH data per splat (0 for degree 0).</summary>
        public int ShFloatsPerSplat => ShCoefficientCount * 3;

        public SplatCloud(int count, int shDegree, bool antialiased, Allocator allocator = Allocator.Persistent)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (shDegree < 0 || shDegree > ShMath.MaxDegree) throw new ArgumentOutOfRangeException(nameof(shDegree));

            Count = count;
            ShDegree = shDegree;
            Antialiased = antialiased;

            Positions = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
            LogScales = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
            Rotations = new NativeArray<float4>(count, allocator, NativeArrayOptions.UninitializedMemory);
            Alphas = new NativeArray<float>(count, allocator, NativeArrayOptions.UninitializedMemory);
            Colors = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
            Sh = new NativeArray<float>(count * ShFloatsPerSplat, allocator, NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>Axis-aligned bounds of all centers. Splat extents are ignored; callers pad if they need to.</summary>
        public void ComputeBounds(out float3 min, out float3 max)
        {
            min = new float3(float.MaxValue);
            max = new float3(float.MinValue);
            for (int splatIndex = 0; splatIndex < Count; splatIndex++)
            {
                min = math.min(min, Positions[splatIndex]);
                max = math.max(max, Positions[splatIndex]);
            }

            if (Count == 0)
            {
                min = float3.zero;
                max = float3.zero;
            }
        }

        public void Dispose()
        {
            if (Positions.IsCreated) Positions.Dispose();
            if (LogScales.IsCreated) LogScales.Dispose();
            if (Rotations.IsCreated) Rotations.Dispose();
            if (Alphas.IsCreated) Alphas.Dispose();
            if (Colors.IsCreated) Colors.Dispose();
            if (Sh.IsCreated) Sh.Dispose();
        }
    }
}
