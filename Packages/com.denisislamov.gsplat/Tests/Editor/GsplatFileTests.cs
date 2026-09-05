using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class GsplatFileTests
    {
        private static GsplatData BuildSample(int count, int shDegree)
        {
            using (SplatCloud cloud = TestClouds.Random(count, shDegree, 21))
            {
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f, TargetShDegree = shDegree };
                return GsplatBuilder.Build(cloud, options);
            }
        }

        [Test]
        public void SerializeDeserializeIsLossless([Values(0, 2)] int shDegree)
        {
            using (GsplatData original = BuildSample(70000, shDegree))
            {
                byte[] bytes = GsplatFile.Serialize(original);
                Assert.AreEqual(GsplatFile.HeaderSize + 2 * GsplatFile.ChunkEntrySize + 70000 * 16 + original.Sh.Length, bytes.Length);

                using (GsplatData loaded = GsplatFile.Deserialize(bytes))
                {
                    Assert.AreEqual(original.SplatCount, loaded.SplatCount);
                    Assert.AreEqual(original.ShDegree, loaded.ShDegree);
                    Assert.AreEqual(original.BoundsMin, loaded.BoundsMin);
                    Assert.AreEqual(original.BoundsMax, loaded.BoundsMax);
                    for (int chunkIndex = 0; chunkIndex < original.ChunkCount; chunkIndex++)
                    {
                        Assert.AreEqual(original.Chunks[chunkIndex].SplatCount, loaded.Chunks[chunkIndex].SplatCount);
                        Assert.AreEqual(original.Chunks[chunkIndex].BoundsMin, loaded.Chunks[chunkIndex].BoundsMin);
                    }

                    for (int splatIndex = 0; splatIndex < original.SplatCount; splatIndex++)
                    {
                        Assert.AreEqual(original.Packed[splatIndex], loaded.Packed[splatIndex]);
                    }

                    for (int byteIndex = 0; byteIndex < original.Sh.Length; byteIndex++)
                    {
                        Assert.AreEqual(original.Sh[byteIndex], loaded.Sh[byteIndex]);
                    }
                }
            }
        }

        [Test]
        public void TruncatedFileIsTyped()
        {
            using (GsplatData original = BuildSample(100, 0))
            {
                byte[] bytes = GsplatFile.Serialize(original);
                var cut = new byte[bytes.Length - 16];
                Array.Copy(bytes, cut, cut.Length);
                GsplatFileException e = Assert.Throws<GsplatFileException>(() => GsplatFile.Deserialize(cut));
                Assert.AreEqual(GsplatFileError.TruncatedPayload, e.Code);
            }
        }

        [Test]
        public void OtherVersionIsRefused()
        {
            using (GsplatData original = BuildSample(10, 0))
            {
                byte[] bytes = GsplatFile.Serialize(original);
                bytes[4] = 99;
                GsplatFileException e = Assert.Throws<GsplatFileException>(() => GsplatFile.Deserialize(bytes));
                Assert.AreEqual(GsplatFileError.UnsupportedVersion, e.Code);
            }
        }

        [Test]
        public void FileKindIsDetectedFromMagic()
        {
            using (GsplatData original = BuildSample(10, 0))
            using (SplatCloud cloud = TestClouds.Random(10, 0))
            {
                Assert.AreEqual(SplatFileKind.Gsplat, SplatFileKindDetector.Detect(GsplatFile.Serialize(original)));
                Assert.AreEqual(SplatFileKind.Spz, SplatFileKindDetector.Detect(SpzWriter.Write(cloud)));
                Assert.AreEqual(SplatFileKind.Ply, SplatFileKindDetector.Detect(PlyReaderTests.WritePly(cloud, false)));
                Assert.AreEqual(SplatFileKind.Unknown, SplatFileKindDetector.Detect(new byte[] { 1, 2, 3, 4, 5 }));
            }
        }
    }
}
