using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class CpuCountingSorterTests
    {
        [Test]
        public void OrderIsBackToFrontAndAPermutation([Values(1, 1000, 200000)] int count)
        {
            var random = new Unity.Mathematics.Random(77);
            // TempJob, not Temp: Temp containers cannot be handed to jobs.
            var positions = new NativeArray<float3>(count, Allocator.TempJob);
            var order = new NativeArray<uint>(count, Allocator.TempJob);
            for (int splatIndex = 0; splatIndex < count; splatIndex++) positions[splatIndex] = random.NextFloat3(-50f, 50f);

            float3 cameraPosition = new float3(0f, 0f, -100f);
            float3 cameraForward = math.normalize(new float3(0.2f, -0.1f, 1f));
            using (var sorter = new CpuCountingSorter())
            {
                sorter.Sort(positions, cameraPosition, cameraForward, order);
            }

            SortAssertions.AssertBackToFront(positions, order, cameraPosition, cameraForward);
            positions.Dispose();
            order.Dispose();
        }
    }

    public static class SortAssertions
    {
        /// <summary>Depth must not increase along the order (farthest first), and every index must appear exactly once.</summary>
        public static void AssertBackToFront(NativeArray<float3> positions, NativeArray<uint> order, float3 cameraPosition, float3 cameraForward)
        {
            int count = positions.Length;
            var seen = new bool[count];
            float minDepth = float.MaxValue;
            float maxDepth = float.MinValue;
            for (int splatIndex = 0; splatIndex < count; splatIndex++)
            {
                float depth = SplatSortKeys.ViewDepth(positions[splatIndex], cameraPosition, cameraForward);
                minDepth = math.min(minDepth, depth);
                maxDepth = math.max(maxDepth, depth);
            }

            // Depths inside one 16-bit bucket are unordered by design, so allow one bucket of slack.
            float bucketSize = (maxDepth - minDepth) / SplatSortKeys.MaxKey;
            float previousDepth = float.MaxValue;
            for (int slot = 0; slot < count; slot++)
            {
                uint splatIndex = order[slot];
                Assert.That(splatIndex, Is.LessThan((uint)count), "index out of range at slot " + slot);
                Assert.IsFalse(seen[splatIndex], "index appears twice: " + splatIndex);
                seen[splatIndex] = true;

                float depth = SplatSortKeys.ViewDepth(positions[(int)splatIndex], cameraPosition, cameraForward);
                Assert.That(depth, Is.LessThanOrEqualTo(previousDepth + bucketSize * 1.01f + 1e-4f), "order breaks at slot " + slot);
                previousDepth = depth;
            }
        }
    }
}
