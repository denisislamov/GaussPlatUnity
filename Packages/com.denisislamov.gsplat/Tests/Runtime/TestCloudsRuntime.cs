using Unity.Collections;
using Unity.Mathematics;

namespace GSplat.Tests
{
    /// <summary>Runtime-test copy of the editor helper (test assemblies cannot reference each other across Editor/Runtime).</summary>
    public static class TestCloudsRuntime
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
    }
}
