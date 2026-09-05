using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class CoordinateConverterTests
    {
        [Test]
        public void RufIsIdentity()
        {
            using (SplatCloud cloud = TestClouds.Random(10, 3))
            using (SplatCloud copy = TestClouds.Random(10, 3))
            {
                CoordinateConverter.ConvertToUnity(cloud, SplatCoordinateSystem.Ruf);
                for (int splatIndex = 0; splatIndex < 10; splatIndex++)
                {
                    Assert.AreEqual(copy.Positions[splatIndex], cloud.Positions[splatIndex]);
                    Assert.AreEqual(copy.Rotations[splatIndex], cloud.Rotations[splatIndex]);
                }
            }
        }

        [Test]
        public void RubNegatesZ()
        {
            using (SplatCloud cloud = TestClouds.Single(new float3(1, 2, 3), float3.zero, new float4(0, 0, 0, 1), 1f, float3.zero))
            {
                CoordinateConverter.ConvertToUnity(cloud, SplatCoordinateSystem.Rub);
                Assert.AreEqual(new float3(1, 2, -3), cloud.Positions[0]);
            }
        }

        [Test]
        public void RdfNegatesY()
        {
            using (SplatCloud cloud = TestClouds.Single(new float3(1, 2, 3), float3.zero, new float4(0, 0, 0, 1), 1f, float3.zero))
            {
                CoordinateConverter.ConvertToUnity(cloud, SplatCoordinateSystem.Rdf);
                Assert.AreEqual(new float3(1, -2, 3), cloud.Positions[0]);
            }
        }

        [Test]
        public void MirroredRotationRotatesMirroredVectorsConsistently()
        {
            // For any vector v and rotation q: mirror(q * v) == mirror(q) * mirror(v). That is the property the
            // renderer needs: the ellipsoid axes end up where the mirrored scene has them.
            var random = new Unity.Mathematics.Random(3);
            bool3[] mirrors = { new bool3(true, false, false), new bool3(false, true, false), new bool3(false, false, true), new bool3(false, true, true) };
            foreach (bool3 mirror in mirrors)
            {
                float3 sign = math.select(new float3(1f), new float3(-1f), mirror);
                for (int sample = 0; sample < 200; sample++)
                {
                    quaternion q = math.normalize(random.NextQuaternionRotation());
                    float3 v = random.NextFloat3Direction();
                    float3 expected = math.mul(q, v) * sign;
                    quaternion mirrored = new quaternion(CoordinateConverter.FlipRotation(q.value, mirror));
                    float3 actual = math.mul(mirrored, v * sign);
                    Assert.That(math.distance(expected, actual), Is.LessThan(1e-4f), $"mirror {mirror}");
                }
            }
        }

        [Test]
        public void ShFlipsOnlyOddCoefficients()
        {
            var cloud = new SplatCloud(1, 3, false, Allocator.Persistent);
            try
            {
                cloud.Positions[0] = float3.zero;
                cloud.Rotations[0] = new float4(0, 0, 0, 1);
                for (int floatIndex = 0; floatIndex < cloud.Sh.Length; floatIndex++) cloud.Sh[floatIndex] = 1f;

                CoordinateConverter.ConvertToUnity(cloud, SplatCoordinateSystem.Rdf); // mirror Y
                // Degree 1 order (y, z, x): only y flips. Degree 2 (xy, yz, zz, xz, xx-yy): xy and yz flip.
                float[] expectedSigns = { -1, 1, 1, -1, -1, 1, 1, 1, -1, -1, -1, 1, 1, 1, 1 };
                for (int coefficient = 0; coefficient < 15; coefficient++)
                {
                    Assert.AreEqual(expectedSigns[coefficient], cloud.Sh[coefficient * 3], "coefficient " + coefficient);
                    Assert.AreEqual(expectedSigns[coefficient], cloud.Sh[coefficient * 3 + 1], "coefficient " + coefficient + " g");
                }
            }
            finally
            {
                cloud.Dispose();
            }
        }
    }
}
