using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class SpzRoundTripTests
    {
        [Test]
        public void WriterOutputIsReadBack([Values(0, 1, 2, 3)] int shDegree)
        {
            using (SplatCloud original = TestClouds.Random(1000, shDegree))
            {
                byte[] bytes = SpzWriter.Write(original);
                SpzHeader header = SpzReader.ReadHeader(bytes);
                Assert.AreEqual(3, header.Version);
                Assert.AreEqual(1000, header.PointCount);
                Assert.AreEqual(shDegree, header.ShDegree);

                using (SplatCloud decoded = SpzReader.Read(bytes))
                {
                    AssertCloudsMatch(original, decoded);
                }
            }
        }

        [Test]
        public void AntialiasedFlagSurvives()
        {
            var cloud = new SplatCloud(2, 0, true);
            try
            {
                for (int splatIndex = 0; splatIndex < 2; splatIndex++)
                {
                    cloud.Positions[splatIndex] = new float3(splatIndex);
                    cloud.Rotations[splatIndex] = new float4(0, 0, 0, 1);
                    cloud.Alphas[splatIndex] = 1f;
                }

                using (SplatCloud decoded = SpzReader.Read(SpzWriter.Write(cloud)))
                {
                    Assert.IsTrue(decoded.Antialiased);
                }
            }
            finally
            {
                cloud.Dispose();
            }
        }

        [Test]
        public void TruncatedFileIsReportedAsTruncated()
        {
            using (SplatCloud original = TestClouds.Random(5000, 0))
            {
                byte[] bytes = SpzWriter.Write(original);
                var cut = new byte[bytes.Length / 2];
                Array.Copy(bytes, cut, cut.Length);

                SpzException e = Assert.Throws<SpzException>(() => SpzReader.Read(cut));
                Assert.That(e.Code, Is.EqualTo(SpzError.TruncatedPayload).Or.EqualTo(SpzError.CorruptedCompression));
            }
        }

        [Test]
        public void GarbageBodyIsReportedAsCorrupted()
        {
            using (SplatCloud original = TestClouds.Random(5000, 0))
            {
                byte[] bytes = SpzWriter.Write(original);
                // Keep the gzip signature and the first deflate bytes (they carry the 16-byte header), wreck the rest.
                for (int byteIndex = 40; byteIndex < bytes.Length; byteIndex++) bytes[byteIndex] = 0xAB;

                SpzException e = Assert.Throws<SpzException>(() => SpzReader.Read(bytes));
                Assert.That(e.Code, Is.EqualTo(SpzError.CorruptedCompression).Or.EqualTo(SpzError.TruncatedPayload));
            }
        }

        [Test]
        public void Version4IsRefusedWithAClearMessage()
        {
            var bytes = new byte[SpzHeader.V4HeaderSize + 16];
            BitConverter.GetBytes(SpzHeader.Magic).CopyTo(bytes, 0);
            BitConverter.GetBytes(4u).CopyTo(bytes, 4);
            BitConverter.GetBytes(1u).CopyTo(bytes, 8);
            bytes[13] = 12;
            bytes[15] = 6;
            BitConverter.GetBytes(32u).CopyTo(bytes, 16);

            SpzException e = Assert.Throws<SpzException>(() => SpzReader.Read(bytes));
            Assert.AreEqual(SpzError.UnsupportedCompression, e.Code);
            StringAssert.Contains("zstd", e.Message);
        }

        public static void AssertCloudsMatch(SplatCloud expected, SplatCloud actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            Assert.AreEqual(expected.ShDegree, actual.ShDegree);
            float positionStep = 1f / (1 << SpzWriter.DefaultFractionalBits);
            for (int splatIndex = 0; splatIndex < expected.Count; splatIndex++)
            {
                AssertClose(expected.Positions[splatIndex], actual.Positions[splatIndex], positionStep, "position " + splatIndex);
                AssertClose(expected.LogScales[splatIndex], actual.LogScales[splatIndex], 1f / 32f + 1e-4f, "scale " + splatIndex);
                Assert.AreEqual(expected.Alphas[splatIndex], actual.Alphas[splatIndex], 1f / 255f, "alpha " + splatIndex);
                AssertClose(expected.Colors[splatIndex], actual.Colors[splatIndex], 1f / (255f * SpzQuantization.ColorScale) + 1e-4f, "color " + splatIndex);
                float dot = math.abs(math.dot(expected.Rotations[splatIndex], actual.Rotations[splatIndex]));
                Assert.That(dot, Is.GreaterThan(0.997f), "rotation " + splatIndex);
            }

            for (int floatIndex = 0; floatIndex < expected.Sh.Length; floatIndex++)
            {
                Assert.AreEqual(expected.Sh[floatIndex], actual.Sh[floatIndex], 1f / 256f + 1e-5f, "sh " + floatIndex);
            }
        }

        private static void AssertClose(float3 expected, float3 actual, float tolerance, string what)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, what + ".x");
            Assert.AreEqual(expected.y, actual.y, tolerance, what + ".y");
            Assert.AreEqual(expected.z, actual.z, tolerance, what + ".z");
        }
    }
}
