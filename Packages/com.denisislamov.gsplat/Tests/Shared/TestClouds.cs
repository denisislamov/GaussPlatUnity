using Unity.Collections;
using Unity.Mathematics;

namespace GSplat.Tests
{
    /// <summary>Small deterministic clouds shared by the EditMode and PlayMode tests. Values stay inside the quantization ranges of SPZ.</summary>
    public static class TestClouds
    {
        public static SplatCloud Random(int count, int shDegree, uint seed = 1234, float extent = 8f)
        {
            var random = new Unity.Mathematics.Random(seed);
            var cloud = new SplatCloud(count, shDegree, false, Allocator.Persistent);
            for (int splatIndex = 0; splatIndex < count; splatIndex++)
            {
                cloud.Positions[splatIndex] = random.NextFloat3(-extent, extent);
                cloud.LogScales[splatIndex] = random.NextFloat3(-6f, 0.5f);
                cloud.Rotations[splatIndex] = math.normalize(random.NextFloat4(-1f, 1f));
                cloud.Alphas[splatIndex] = random.NextFloat(0.02f, 1f);
                cloud.Colors[splatIndex] = random.NextFloat3(-1.5f, 1.5f);
            }

            for (int floatIndex = 0; floatIndex < cloud.Sh.Length; floatIndex++)
            {
                cloud.Sh[floatIndex] = random.NextFloat(-0.9f, 0.9f);
            }

            return cloud;
        }

        public static SplatCloud Single(float3 position, float3 logScale, float4 rotation, float alpha, float3 color)
        {
            var cloud = new SplatCloud(1, 0, false, Allocator.Persistent);
            cloud.Positions[0] = position;
            cloud.LogScales[0] = logScale;
            cloud.Rotations[0] = math.normalize(rotation);
            cloud.Alphas[0] = alpha;
            cloud.Colors[0] = color;
            return cloud;
        }
    }
}
