using System;

namespace GSplat
{
    /// <summary>
    /// How a sorter is built. Changing any of these means a new sorter (buffers are sized by the key width), so the
    /// renderer compares the struct and recreates when it differs.
    /// </summary>
    public struct SplatSorterOptions : IEquatable<SplatSorterOptions>
    {
        public SplatSorterKind Kind;

        /// <summary>16 = 65 536 depth buckets (default), 12 = 4 096: a 16x shorter prefix scan and a histogram that fits in cache.</summary>
        public int KeyBits;

        /// <summary>CPU sorter only: spread one sort over several frames, <see cref="SlotsPerFrame"/> slots each (P6).</summary>
        public bool TimeSliced;
        public int SlotsPerFrame;

        public static SplatSorterOptions Default(SplatSorterKind kind)
        {
            return new SplatSorterOptions { Kind = kind, KeyBits = SplatSortKeys.KeyBits, TimeSliced = false, SlotsPerFrame = 131072 };
        }

        public bool Equals(SplatSorterOptions other)
        {
            return Kind == other.Kind && KeyBits == other.KeyBits && TimeSliced == other.TimeSliced && SlotsPerFrame == other.SlotsPerFrame;
        }

        public override bool Equals(object obj)
        {
            return obj is SplatSorterOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Kind * 397) ^ (KeyBits * 31) ^ (TimeSliced ? 1 : 0) ^ SlotsPerFrame;
        }
    }
}
