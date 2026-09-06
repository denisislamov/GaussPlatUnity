using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace GSplat.Tests
{
    /// <summary>P3: the chunk prefix is a valid level of detail only if every chunk is importance-ordered.</summary>
    public sealed class ImportanceOrderTests
    {
        private static float Importance(SplatCloud cloud, int index)
        {
            float3 scale = math.exp(cloud.LogScales[index]);
            return cloud.Alphas[index] * (scale.x * scale.y + scale.y * scale.z + scale.z * scale.x);
        }

        [Test]
        public void EveryChunkIsOrderedByImportanceDescending()
        {
            const int count = 2 * SplatChunkInfo.Size + 5000; // three chunks, the last one partial
            using (SplatCloud cloud = TestClouds.Random(count, 0, 42, 30f))
            using (SplatCloud ordered = GsplatBuilder.OrderChunksByImportance(cloud, Allocator.Persistent))
            {
                Assert.AreEqual(count, ordered.Count);
                for (int chunkIndex = 0; chunkIndex < SplatChunkInfo.ChunkCountFor(count); chunkIndex++)
                {
                    int first = chunkIndex * SplatChunkInfo.Size;
                    int last = math.min(first + SplatChunkInfo.Size, count) - 1;
                    for (int index = first + 1; index <= last; index++)
                    {
                        // The job computes the importance in Burst; recomputing it here can differ in the last float bits.
                        float previous = Importance(ordered, index - 1);
                        float current = Importance(ordered, index);
                        Assert.GreaterOrEqual(previous, current - 1e-5f * math.max(1f, current), $"chunk {chunkIndex}, index {index}");
                    }
                }
            }
        }

        [Test]
        public void ChunkMembershipIsUnchanged()
        {
            const int count = SplatChunkInfo.Size + 777;
            using (SplatCloud cloud = TestClouds.Random(count, 0, 7, 30f))
            using (SplatCloud ordered = GsplatBuilder.OrderChunksByImportance(cloud, Allocator.Persistent))
            {
                // Same multiset of positions per chunk: sum the positions of each chunk before and after.
                for (int chunkIndex = 0; chunkIndex < 2; chunkIndex++)
                {
                    int first = chunkIndex * SplatChunkInfo.Size;
                    int end = math.min(first + SplatChunkInfo.Size, count);
                    double3 before = 0, after = 0;
                    for (int index = first; index < end; index++)
                    {
                        before += cloud.Positions[index];
                        after += ordered.Positions[index];
                    }

                    Assert.That(math.distance(before, after), Is.LessThan(1e-2), $"chunk {chunkIndex}");
                }
            }
        }

        [Test]
        public void FlagSurvivesTheFileFormat()
        {
            using (SplatCloud cloud = TestClouds.Random(3000, 1, 3))
            {
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f, TargetShDegree = 1, OrderChunksByImportance = true };
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                {
                    Assert.IsTrue(data.ImportanceOrdered);
                    using (GsplatData back = GsplatFile.Deserialize(GsplatFile.Serialize(data)))
                    {
                        Assert.IsTrue(back.ImportanceOrdered);
                    }
                }

                var plain = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f };
                using (SplatCloud again = TestClouds.Random(3000, 1, 3))
                using (GsplatData data = GsplatBuilder.Build(again, plain))
                {
                    Assert.IsFalse(data.ImportanceOrdered);
                }
            }
        }
    }
}
