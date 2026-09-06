using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
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
                float3 position = random.NextFloat3(0f, 1f); // fraction of the chunk bounds
                float3 logScale = random.NextFloat3(-7f, 1f);
                float4 rotation = math.normalize(random.NextFloat4(-1f, 1f));
                float3 color = random.NextFloat3(0f, 1f);
                float alpha = random.NextFloat(0f, 1f);

                uint4 packed = PackedSplat.Pack(position, logScale, rotation, color, alpha);
                PackedSplat.Unpack(packed, out float3 p, out float3 s, out float4 r, out float3 c, out float a);

                // 16-bit fractions: half a step is 1 / 131070 of the chunk extent.
                Assert.That(math.cmax(math.abs(position - p)), Is.LessThan(0.5f / 65535f + 1e-7f), "position");
                Assert.That(math.cmax(math.abs(logScale - s)), Is.LessThan(1f / 32f + 1e-4f), "scale");
                Assert.That(math.abs(math.dot(rotation, r)), Is.GreaterThan(0.97f), "rotation");
                Assert.That(math.cmax(math.abs(color - c)), Is.LessThan(1f / 510f + 1e-5f), "color");
                Assert.AreEqual(alpha, a, 1f / 510f + 1e-5f, "alpha");
            }
        }

        [Test]
        public void ChunkOfTwentyMetersKeepsSubMillimeterPrecision()
        {
            // Precision is extent / 65535: a 20 m chunk resolves 0.3 mm anywhere inside it.
            var chunk = new SplatChunkInfo(1, new float3(-10f), new float3(10f), 2f);
            float3 position = new float3(1.2345f, -0.6789f, 9.999f);
            float3 normalized = (position - chunk.PositionMin) / chunk.PositionExtent;
            uint4 packed = PackedSplat.Pack(normalized, float3.zero, new float4(0, 0, 0, 1), float3.zero, 1f);
            PackedSplat.Unpack(packed, out float3 p, out _, out _, out _, out _);
            Assert.That(math.distance(position, chunk.PositionOf(p)), Is.LessThan(0.0004f));
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
            Assert.AreEqual(0u, packed.x, "pos.x/pos.y = 0");
            Assert.AreEqual(0u, packed.y, "pos.z = 0, log scale -10 -> byte 0");
            // scale.z byte 0; rotation xyz = 0 -> 127.5 rounds to 128 (0x80) each.
            Assert.AreEqual(0x80808000u, packed.z);
            Assert.AreEqual(0xFF0000FFu, packed.w, "r = 255, g = b = 0, alpha = 255");
        }

        [Test]
        public void ChunkInfoIs48BytesWithFloat3sOn16ByteBoundaries()
        {
            // The chunk GraphicsBuffer is declared with this stride on the HLSL side; the reserved fillers keep it.
            Assert.AreEqual(48, UnsafeUtility.SizeOf<SplatChunkInfo>());
            Assert.AreEqual(16, UnsafeUtility.GetFieldOffset(typeof(SplatChunkInfo).GetField("BoundsMin")));
            Assert.AreEqual(32, UnsafeUtility.GetFieldOffset(typeof(SplatChunkInfo).GetField("BoundsMax")));
        }
    }
}
