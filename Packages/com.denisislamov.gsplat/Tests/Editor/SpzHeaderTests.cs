using System;
using NUnit.Framework;

namespace GSplat.Tests
{
    public sealed class SpzHeaderTests
    {
        private static byte[] LegacyHeader(uint version, uint points, byte shDegree, byte fractionalBits, byte flags)
        {
            var bytes = new byte[SpzHeader.LegacyHeaderSize];
            BitConverter.GetBytes(SpzHeader.Magic).CopyTo(bytes, 0);
            BitConverter.GetBytes(version).CopyTo(bytes, 4);
            BitConverter.GetBytes(points).CopyTo(bytes, 8);
            bytes[12] = shDegree;
            bytes[13] = fractionalBits;
            bytes[14] = flags;
            return bytes;
        }

        [Test]
        public void ParsesLegacyHeaderFields()
        {
            SpzHeader header = SpzHeader.Parse(LegacyHeader(2, 12345, 3, 12, SpzHeader.FlagAntialiased));

            Assert.AreEqual(2, header.Version);
            Assert.AreEqual(12345, header.PointCount);
            Assert.AreEqual(3, header.ShDegree);
            Assert.AreEqual(12, header.FractionalBits);
            Assert.IsTrue(header.Antialiased);
            Assert.IsFalse(header.UsesSmallestThreeRotation);
            Assert.AreEqual(3, header.RotationBytes);
            Assert.AreEqual(9, header.PositionBytes);
        }

        [Test]
        public void Version3UsesSmallestThreeRotation()
        {
            SpzHeader header = SpzHeader.Parse(LegacyHeader(3, 1, 0, 12, 0));
            Assert.IsTrue(header.UsesSmallestThreeRotation);
            Assert.AreEqual(4, header.RotationBytes);
        }

        [Test]
        public void Version1UsesFloat16Positions()
        {
            SpzHeader header = SpzHeader.Parse(LegacyHeader(1, 1, 0, 12, 0));
            Assert.AreEqual(6, header.PositionBytes);
        }

        [Test]
        public void EmptyPayloadIsTyped()
        {
            SpzException e = Assert.Throws<SpzException>(() => SpzHeader.Parse(Array.Empty<byte>()));
            Assert.AreEqual(SpzError.EmptyPayload, e.Code);
        }

        [Test]
        public void ShortPayloadIsTruncated()
        {
            SpzException e = Assert.Throws<SpzException>(() => SpzHeader.Parse(new byte[7]));
            Assert.AreEqual(SpzError.TruncatedPayload, e.Code);
        }

        [Test]
        public void WrongMagicIsBadMagic()
        {
            byte[] bytes = LegacyHeader(2, 1, 0, 12, 0);
            bytes[0] = (byte)'X';
            SpzException e = Assert.Throws<SpzException>(() => SpzHeader.Parse(bytes));
            Assert.AreEqual(SpzError.BadMagic, e.Code);
        }

        [Test]
        public void FutureVersionIsUnsupported()
        {
            SpzException e = Assert.Throws<SpzException>(() => SpzHeader.Parse(LegacyHeader(5, 1, 0, 12, 0)));
            Assert.AreEqual(SpzError.UnsupportedVersion, e.Code);
            StringAssert.Contains("version 5", e.Message);
        }

        [Test]
        public void ShDegreeAboveFourIsRejected()
        {
            SpzException e = Assert.Throws<SpzException>(() => SpzHeader.Parse(LegacyHeader(2, 1, 5, 12, 0)));
            Assert.AreEqual(SpzError.UnsupportedShDegree, e.Code);
        }

        [Test]
        public void Version4NeedsTheLongHeader()
        {
            SpzException e = Assert.Throws<SpzException>(() => SpzHeader.Parse(LegacyHeader(4, 1, 0, 12, 0)));
            Assert.AreEqual(SpzError.TruncatedPayload, e.Code);
        }

        [Test]
        public void Version4HeaderParsesStreamsAndToc()
        {
            var bytes = new byte[SpzHeader.V4HeaderSize];
            LegacyHeader(4, 10, 1, 12, 0).CopyTo(bytes, 0);
            bytes[15] = 6;
            BitConverter.GetBytes(32u).CopyTo(bytes, 16);

            SpzHeader header = SpzHeader.Parse(bytes);
            Assert.AreEqual(6, header.StreamCount);
            Assert.AreEqual(32, header.TocByteOffset);
            Assert.AreEqual(SpzHeader.V4HeaderSize, header.HeaderSize);
        }

        [Test]
        public void WriteLegacyRoundTrips()
        {
            var header = new SpzHeader(3, 777, 2, 12, SpzHeader.FlagAntialiased, 0, 0);
            var bytes = new byte[SpzHeader.LegacyHeaderSize];
            header.WriteLegacy(bytes);

            SpzHeader parsed = SpzHeader.Parse(bytes);
            Assert.AreEqual(3, parsed.Version);
            Assert.AreEqual(777, parsed.PointCount);
            Assert.AreEqual(2, parsed.ShDegree);
            Assert.IsTrue(parsed.Antialiased);
        }
    }
}
