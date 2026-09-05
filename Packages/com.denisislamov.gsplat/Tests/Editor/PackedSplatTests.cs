using NUnit.Framework;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class PackedSplatTests
    {
        [Test]
        public void PackUnpackRoundTripsWithinQuantization()
        {
            var random = new Unity.Mathematics.Random(11);
            for (int sample = 0; sample < 1000; sample++)
            {
                float3 position = random.NextFloat3(-30f, 30f);
                float3 logScale = random.NextFloat3(-7f, 1f);
                float4 rotation = math.normalize(random.NextFloat4(-1f, 1f));
                float3 color = random.NextFloat3(0f, 1f);
                float alpha = random.NextFloat(0f, 1f);

                uint4 packed = PackedSplat.Pack(position, logScale, rotation, color, alpha);
                PackedSplat.Unpack(packed, out float3 p, out float3 s, out float4 r, out float3 c, out float a);

                // float16 has 10 mantissa bits: relative error 2^-11, absolute ~0.015 at 30 m.
                Assert.That(math.distance(position, p), Is.LessThan(0.03f), "position");
                Assert.That(math.cmax(math.abs(logScale - s)), Is.LessThan(1f / 32f + 1e-4f), "scale");
                Assert.That(math.abs(math.dot(rotation, r)), Is.GreaterThan(0.97f), "rotation");
                Assert.That(math.cmax(math.abs(color - c)), Is.LessThan(1f / 510f + 1e-5f), "color");
                Assert.AreEqual(alpha, a, 1f / 510f + 1e-5f, "alpha");
            }
        }

        [Test]
        public void SmallPositionsKeepMillimeterPrecision()
        {
            // Splats near their chunk center are the common case; float16 below 2 m has steps of 1 mm or finer.
            float3 position = new float3(1.2345f, -0.6789f, 0.001f);
            uint4 packed = PackedSplat.Pack(position, float3.zero, new float4(0, 0, 0, 1), float3.zero, 1f);
            PackedSplat.Unpack(packed, out float3 p, out _, out _, out _, out _);
            Assert.That(math.distance(position, p), Is.LessThan(0.002f));
        }

        [Test]
        public void DisplayColorMapsZeroToMidGrey()
        {
            float3 grey = PackedSplat.DisplayColor(float3.zero);
            Assert.AreEqual(0.5f, grey.x, 1e-6f);
            Assert.AreEqual(1f, PackedSplat.DisplayColor(new float3(10f)).x, "clamped high");
            Assert.AreEqual(0f, PackedSplat.DisplayColor(new float3(-10f)).x, "clamped low");
        }

        [Test]
        public void LayoutIsTheDocumentedOne()
        {
            uint4 packed = PackedSplat.Pack(float3.zero, new float3(-10f, -10f, -10f), new float4(0, 0, 0, 1), new float3(1f, 0f, 0f), 1f);
            Assert.AreEqual(0u, packed.x, "pos.x/pos.y half = 0");
            Assert.AreEqual(0u, packed.y, "pos.z = 0, log scale -10 -> byte 0");
            // scale.z byte 0; rotation xyz = 0 -> 127.5 rounds to 128 (0x80) each.
            Assert.AreEqual(0x80808000u, packed.z);
            Assert.AreEqual(0xFF0000FFu, packed.w, "r = 255, g = b = 0, alpha = 255");
        }
    }
}
