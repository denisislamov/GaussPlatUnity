using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class GsplatBuilderTests
    {
        [Test]
        public void BuildKeepsEverySplatWhenNothingIsPruned()
        {
            using (SplatCloud cloud = TestClouds.Random(70000, 1))
            {
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f, TargetShDegree = 1 };
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                {
                    Assert.AreEqual(70000, data.SplatCount);
                    Assert.AreEqual(2, data.ChunkCount);
                    Assert.AreEqual(65536, data.Chunks[0].SplatCount);
                    Assert.AreEqual(70000 - 65536, data.Chunks[1].SplatCount);
                    Assert.AreEqual(1, data.ShDegree);
                    Assert.AreEqual(70000 * 9, data.Sh.Length);
                }
            }
        }

        [Test]
        public void PruningDropsTransparentSplats()
        {
            using (SplatCloud cloud = TestClouds.Random(1000, 0))
            {
                for (int splatIndex = 0; splatIndex < 1000; splatIndex += 4) cloud.Alphas[splatIndex] = 0.001f;
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0.01f };
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                {
                    Assert.AreEqual(750, data.SplatCount);
                }
            }
        }

        [Test]
        public void BudgetKeepsTheMostImportantSplats()
        {
            using (SplatCloud cloud = TestClouds.Random(1000, 0))
            {
                // Make 10 splats clearly the most important: opaque and huge.
                for (int splatIndex = 0; splatIndex < 10; splatIndex++)
                {
                    cloud.Alphas[splatIndex] = 1f;
                    cloud.LogScales[splatIndex] = new float3(2f);
                    cloud.Positions[splatIndex] = new float3(100f + splatIndex, 0f, 0f);
                }

                using (NativeArray<int> kept = SplatFilter.SelectIndices(cloud, 0f, 10, Allocator.Temp))
                {
                    Assert.AreEqual(10, kept.Length);
                    for (int keptIndex = 0; keptIndex < 10; keptIndex++) Assert.AreEqual(keptIndex, kept[keptIndex]);
                }
            }
        }

        [Test]
        public void SpatialSortMakesChunksCompact()
        {
            // Two far-apart clusters of 65536 splats each, interleaved in file order: after the Morton sort each
            // chunk must contain one cluster, so its bounds are small.
            const int perCluster = 65536;
            var random = new Unity.Mathematics.Random(5);
            var cloud = new SplatCloud(perCluster * 2, 0, false, Allocator.Persistent);
            try
            {
                for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
                {
                    float3 clusterCenter = (splatIndex % 2 == 0) ? new float3(-100f, 0f, 0f) : new float3(100f, 0f, 0f);
                    cloud.Positions[splatIndex] = clusterCenter + random.NextFloat3(-1f, 1f);
                    cloud.LogScales[splatIndex] = new float3(-5f);
                    cloud.Rotations[splatIndex] = new float4(0, 0, 0, 1);
                    cloud.Alphas[splatIndex] = 1f;
                }

                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f };
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                {
                    Assert.AreEqual(2, data.ChunkCount);
                    for (int chunkIndex = 0; chunkIndex < 2; chunkIndex++)
                    {
                        float3 size = data.Chunks[chunkIndex].BoundsMax - data.Chunks[chunkIndex].BoundsMin;
                        Assert.That(size.x, Is.LessThan(3f), $"chunk {chunkIndex} spans both clusters");
                    }
                }
            }
            finally
            {
                cloud.Dispose();
            }
        }

        [Test]
        public void PackedPositionsReconstructTheOriginals()
        {
            using (SplatCloud cloud = TestClouds.Random(5000, 0, 8, 40f))
            {
                var expected = new List<float3>();
                for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++) expected.Add(cloud.Positions[splatIndex]);

                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f, SpatialSort = false };
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                {
                    for (int splatIndex = 0; splatIndex < data.SplatCount; splatIndex++)
                    {
                        PackedSplat.Unpack(data.Packed[splatIndex], out float3 relative, out _, out _, out _, out _);
                        float3 reconstructed = relative + data.Chunks[splatIndex / SplatChunkInfo.Size].Center;
                        Assert.That(math.distance(expected[splatIndex], reconstructed), Is.LessThan(0.05f), "splat " + splatIndex);
                    }
                }
            }
        }

        [Test]
        public void MortonCodeInterleavesBits()
        {
            Assert.AreEqual(0u, SplatSpatialSort.MortonCode(new uint3(0, 0, 0)));
            Assert.AreEqual(1u, SplatSpatialSort.MortonCode(new uint3(1, 0, 0)));
            Assert.AreEqual(2u, SplatSpatialSort.MortonCode(new uint3(0, 1, 0)));
            Assert.AreEqual(4u, SplatSpatialSort.MortonCode(new uint3(0, 0, 1)));
            Assert.AreEqual(0x3FFFFFFFu, SplatSpatialSort.MortonCode(new uint3(1023, 1023, 1023)));
        }
    }
}
