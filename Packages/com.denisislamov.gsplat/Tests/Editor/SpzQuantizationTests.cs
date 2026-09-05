using NUnit.Framework;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class SpzQuantizationTests
    {
        [Test]
        public void LogScaleRoundTripsWithinOneStep()
        {
            for (float logScale = -9.9f; logScale < 5.9f; logScale += 0.37f)
            {
                float decoded = SpzQuantization.DecodeLogScale(SpzQuantization.EncodeLogScale(logScale));
                Assert.AreEqual(logScale, decoded, 1f / 32f + 1e-5f, $"log scale {logScale}");
            }
        }

        [Test]
        public void LogScaleClampsOutsideTheRange()
        {
            Assert.AreEqual(0, SpzQuantization.EncodeLogScale(-50f));
            Assert.AreEqual(255, SpzQuantization.EncodeLogScale(50f));
        }

        [Test]
        public void AlphaRoundTripsWithinOneStep()
        {
            for (float alpha = 0f; alpha <= 1f; alpha += 0.05f)
            {
                Assert.AreEqual(alpha, SpzQuantization.DecodeAlpha(SpzQuantization.EncodeAlpha(alpha)), 1f / 510f + 1e-6f);
            }
        }

        [Test]
        public void ColorRoundTripsInsideTheDisplayableRange()
        {
            // Displayable colors are 0.5 + Sh0Scale * c in [0, 1], so c in [-1.77, 1.77].
            for (float coefficient = -1.7f; coefficient <= 1.7f; coefficient += 0.1f)
            {
                float decoded = SpzQuantization.DecodeColor(SpzQuantization.EncodeColor(coefficient));
                Assert.AreEqual(coefficient, decoded, 1f / (255f * ShMath.Sh0Scale) + 1e-5f, $"coefficient {coefficient}");
            }
        }

        [Test]
        public void ColorOfZeroCoefficientIsMidGrey()
        {
            // 0 * scale * 255 + 127.5 rounds to 128 -> 128/255 = 0.502 display; back to coefficient ~0.007.
            byte encoded = SpzQuantization.EncodeColor(0f);
            Assert.AreEqual(128, encoded);
        }

        [Test]
        public void ShRoundTripsWithinOneStep()
        {
            for (float coefficient = -0.99f; coefficient < 0.99f; coefficient += 0.013f)
            {
                Assert.AreEqual(coefficient, SpzQuantization.DecodeSh(SpzQuantization.EncodeSh(coefficient)), 1f / 256f + 1e-6f);
            }
        }

        [Test]
        public void PositionRoundTripsWithinHalfAStep()
        {
            const int fractionalBits = 12;
            float step = 1f / (1 << fractionalBits);
            float[] samples = { 0f, 0.5f, -0.5f, 123.456f, -2047.999f, 2047.999f, 1e-5f, -1e-5f };
            foreach (float value in samples)
            {
                int encoded = SpzQuantization.EncodePosition(value, fractionalBits);
                float decoded = SpzQuantization.DecodePosition((byte)(encoded & 0xFF), (byte)((encoded >> 8) & 0xFF), (byte)((encoded >> 16) & 0xFF), fractionalBits);
                Assert.AreEqual(value, decoded, step * 0.5f + 1e-6f, $"position {value}");
            }
        }

        [Test]
        public void NegativePositionSignExtends()
        {
            // -1.0 at 12 fractional bits = -4096 = 0xFFF000 in 24 bits.
            float decoded = SpzQuantization.DecodePosition(0x00, 0xF0, 0xFF, 12);
            Assert.AreEqual(-1f, decoded, 1e-6f);
        }

        [Test]
        public void SmallestThreeRotationRoundTrips()
        {
            var random = new Unity.Mathematics.Random(42);
            for (int sample = 0; sample < 2000; sample++)
            {
                float4 q = math.normalize(random.NextFloat4(-1f, 1f));
                float4 decoded = SpzQuantization.DecodeRotationSmallestThree(SpzQuantization.EncodeRotationSmallestThree(q));
                AssertSameRotation(q, decoded, 0.003f);
            }
        }

        [Test]
        public void SmallestThreeBitLayoutMatchesTheReference()
        {
            // Identity: w is largest (index 3), the other three are +0 -> [11][0000000000]x3 = 0xC0000000.
            uint identity = SpzQuantization.EncodeRotationSmallestThree(new float4(0f, 0f, 0f, 1f));
            Assert.AreEqual(0xC0000000u, identity);

            // 90 degrees about X: q = (sin45, 0, 0, cos45): x and w are equal, the first strictly greater wins -> x (index 0)
            // stays largest; stored y = 0, z = 0, w = +0.7071 -> magnitude 511 in the lowest 10 bits.
            uint aboutX = SpzQuantization.EncodeRotationSmallestThree(new float4(math.SQRT2 / 2f, 0f, 0f, math.SQRT2 / 2f));
            Assert.AreEqual(0u, aboutX >> 30, "largest index");
            Assert.AreEqual(511u, aboutX & 0x3FF, "w magnitude");
        }

        [Test]
        public void FirstThreeRotationRoundTrips()
        {
            var random = new Unity.Mathematics.Random(7);
            for (int sample = 0; sample < 2000; sample++)
            {
                float4 q = math.normalize(random.NextFloat4(-1f, 1f));
                SpzQuantization.EncodeRotationFirstThree(q, out byte x, out byte y, out byte z);
                float4 decoded = SpzQuantization.DecodeRotationFirstThree(x, y, z);
                // 8 bits per component and the recovered w amplifies the error near w = 0.
                AssertSameRotation(q, decoded, 0.03f);
            }
        }

        private static void AssertSameRotation(float4 expected, float4 actual, float tolerance)
        {
            // q and -q are the same rotation.
            float dot = math.abs(math.dot(expected, actual));
            Assert.That(dot, Is.GreaterThan(1f - tolerance), $"expected {expected} got {actual}");
        }
    }
}
