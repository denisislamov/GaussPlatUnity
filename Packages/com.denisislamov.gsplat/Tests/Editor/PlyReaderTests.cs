using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class PlyReaderTests
    {
        /// <summary>Writes a 3DGS-style PLY (binary or ascii) from a cloud, the way trainers do: log scales, logit opacity, wxyz rotation, channel-major f_rest.</summary>
        public static byte[] WritePly(SplatCloud cloud, bool ascii)
        {
            int coefficients = cloud.ShCoefficientCount;
            var header = new StringBuilder();
            header.Append("ply\n");
            header.Append(ascii ? "format ascii 1.0\n" : "format binary_little_endian 1.0\n");
            header.Append("comment written by GSplat tests\n");
            header.Append("element vertex ").Append(cloud.Count).Append('\n');
            var names = new List<string> { "x", "y", "z", "nx", "ny", "nz", "f_dc_0", "f_dc_1", "f_dc_2" };
            for (int restIndex = 0; restIndex < coefficients * 3; restIndex++) names.Add("f_rest_" + restIndex);
            names.AddRange(new[] { "opacity", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3" });
            foreach (string name in names) header.Append("property float ").Append(name).Append('\n');
            header.Append("end_header\n");

            var values = new float[names.Count];
            using (var stream = new MemoryStream())
            {
                byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
                stream.Write(headerBytes, 0, headerBytes.Length);
                for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
                {
                    int column = 0;
                    float3 p = cloud.Positions[splatIndex];
                    values[column++] = p.x; values[column++] = p.y; values[column++] = p.z;
                    values[column++] = 0f; values[column++] = 0f; values[column++] = 0f;
                    float3 c = cloud.Colors[splatIndex];
                    values[column++] = c.x; values[column++] = c.y; values[column++] = c.z;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        for (int coefficient = 0; coefficient < coefficients; coefficient++)
                        {
                            values[column++] = cloud.Sh[splatIndex * cloud.ShFloatsPerSplat + coefficient * 3 + channel];
                        }
                    }

                    float alpha = cloud.Alphas[splatIndex];
                    values[column++] = math.log(alpha / (1f - alpha)); // logit
                    float3 s = cloud.LogScales[splatIndex];
                    values[column++] = s.x; values[column++] = s.y; values[column++] = s.z;
                    float4 q = cloud.Rotations[splatIndex];
                    values[column++] = q.w; values[column++] = q.x; values[column++] = q.y; values[column++] = q.z;

                    if (ascii)
                    {
                        var line = new StringBuilder();
                        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                        {
                            if (valueIndex > 0) line.Append(' ');
                            line.Append(values[valueIndex].ToString("R", CultureInfo.InvariantCulture));
                        }

                        line.Append('\n');
                        byte[] lineBytes = Encoding.ASCII.GetBytes(line.ToString());
                        stream.Write(lineBytes, 0, lineBytes.Length);
                    }
                    else
                    {
                        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                        {
                            stream.Write(BitConverter.GetBytes(values[valueIndex]), 0, 4);
                        }
                    }
                }

                return stream.ToArray();
            }
        }

        [Test]
        public void BinaryPlyRoundTrips([Values(0, 1, 3)] int shDegree)
        {
            using (SplatCloud original = TestClouds.Random(300, shDegree, 99))
            {
                byte[] bytes = WritePly(original, false);
                PlyHeader header = PlyReader.ReadHeader(bytes);
                Assert.AreEqual(PlyFormat.BinaryLittleEndian, header.Format);
                Assert.AreEqual(300, header.VertexCount);
                Assert.AreEqual(shDegree, PlyReader.ShDegreeOf(header));

                using (SplatCloud decoded = PlyReader.Read(bytes))
                {
                    AssertExact(original, decoded);
                }
            }
        }

        [Test]
        public void AsciiPlyRoundTrips()
        {
            using (SplatCloud original = TestClouds.Random(50, 1, 5))
            {
                using (SplatCloud decoded = PlyReader.Read(WritePly(original, true)))
                {
                    AssertExact(original, decoded);
                }
            }
        }

        [Test]
        public void MissingPropertyIsTyped()
        {
            string text = "ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\nend_header\n";
            byte[] bytes = Encoding.ASCII.GetBytes(text + new string('\0', 12));
            PlyException e = Assert.Throws<PlyException>(() => PlyReader.Read(bytes));
            Assert.AreEqual(PlyError.MissingProperty, e.Code);
            StringAssert.Contains("scale_0", e.Message);
        }

        [Test]
        public void TruncatedBodyIsTyped()
        {
            using (SplatCloud original = TestClouds.Random(20, 0))
            {
                byte[] bytes = WritePly(original, false);
                var cut = new byte[bytes.Length - 40];
                Array.Copy(bytes, cut, cut.Length);
                PlyException e = Assert.Throws<PlyException>(() => PlyReader.Read(cut));
                Assert.AreEqual(PlyError.TruncatedPayload, e.Code);
            }
        }

        [Test]
        public void NotAPlyIsBadMagic()
        {
            PlyException e = Assert.Throws<PlyException>(() => PlyReader.Read(Encoding.ASCII.GetBytes("hello world, not a ply\n")));
            Assert.AreEqual(PlyError.BadMagic, e.Code);
        }

        [Test]
        public void BigEndianIsUnsupported()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("ply\nformat binary_big_endian 1.0\nelement vertex 0\nproperty float x\nend_header\n");
            PlyException e = Assert.Throws<PlyException>(() => PlyReader.Read(bytes));
            Assert.AreEqual(PlyError.UnsupportedFormat, e.Code);
        }

        private static void AssertExact(SplatCloud expected, SplatCloud actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            Assert.AreEqual(expected.ShDegree, actual.ShDegree);
            for (int splatIndex = 0; splatIndex < expected.Count; splatIndex++)
            {
                Assert.That(math.distance(expected.Positions[splatIndex], actual.Positions[splatIndex]), Is.LessThan(1e-5f), "position " + splatIndex);
                Assert.That(math.distance(expected.LogScales[splatIndex], actual.LogScales[splatIndex]), Is.LessThan(1e-5f), "scale " + splatIndex);
                Assert.That(math.distance(expected.Colors[splatIndex], actual.Colors[splatIndex]), Is.LessThan(1e-5f), "color " + splatIndex);
                Assert.AreEqual(expected.Alphas[splatIndex], actual.Alphas[splatIndex], 1e-4f, "alpha " + splatIndex);
                Assert.That(math.abs(math.dot(expected.Rotations[splatIndex], actual.Rotations[splatIndex])), Is.GreaterThan(0.99999f), "rotation " + splatIndex);
            }

            for (int floatIndex = 0; floatIndex < expected.Sh.Length; floatIndex++)
            {
                Assert.AreEqual(expected.Sh[floatIndex], actual.Sh[floatIndex], 1e-5f, "sh " + floatIndex);
            }
        }
    }
}
