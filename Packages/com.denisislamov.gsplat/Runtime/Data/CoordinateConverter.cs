using System;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Brings a <see cref="SplatCloud"/> from its file coordinate system into Unity's (RUF) in place.
    /// A change of axis convention is a mirror along one or more axes, and a mirror affects three things:
    /// positions (negate the axis), rotations (see FlipRotation) and the SH coefficients whose basis
    /// function is odd in that axis (see the flip tables). Scales are per local axis and do not change.
    /// </summary>
    public static class CoordinateConverter
    {
        // Which SH coefficient (index inside a degree, in the 3DGS/SPZ order) changes sign when one axis is
        // mirrored. Derived from the real SH basis: a coefficient flips when its polynomial has an odd power
        // of the mirrored axis. Degree 1 basis order is (y, z, x); degree 2 is (xy, yz, 3z²-1, xz, x²-y²);
        // degree 3 is (y(3x²-y²), xyz, y(5z²-1), z(5z²-3), x(5z²-1), z(x²-y²), x(x²-3y²)).
        private static readonly bool[] FlipsWhenXMirrored =
        {
            false, false, true,                         // degree 1
            true, false, false, true, false,            // degree 2
            false, true, false, false, true, false, true // degree 3
        };

        private static readonly bool[] FlipsWhenYMirrored =
        {
            true, false, false,
            true, true, false, false, false,
            true, true, true, false, false, false, false
        };

        private static readonly bool[] FlipsWhenZMirrored =
        {
            false, true, false,
            false, true, false, true, false,
            false, true, false, true, false, true, false
        };

        /// <summary>Which axes have to be negated to go from <paramref name="source"/> to Unity (RUF).</summary>
        public static bool3 MirroredAxes(SplatCoordinateSystem source)
        {
            switch (source)
            {
                case SplatCoordinateSystem.Ruf: return new bool3(false, false, false);
                case SplatCoordinateSystem.Rub: return new bool3(false, false, true);
                case SplatCoordinateSystem.Rdf: return new bool3(false, true, false);
                case SplatCoordinateSystem.Luf: return new bool3(true, false, false);
                case SplatCoordinateSystem.Ldf: return new bool3(true, true, false);
                default: throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown coordinate system.");
            }
        }

        public static void ConvertToUnity(SplatCloud cloud, SplatCoordinateSystem source)
        {
            if (cloud == null) throw new ArgumentNullException(nameof(cloud));

            bool3 mirror = MirroredAxes(source);
            if (!math.any(mirror)) return;

            float3 positionSign = math.select(new float3(1f), new float3(-1f), mirror);
            for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
            {
                cloud.Positions[splatIndex] *= positionSign;
                cloud.Rotations[splatIndex] = FlipRotation(cloud.Rotations[splatIndex], mirror);
            }

            if (cloud.ShDegree > 0)
            {
                FlipSh(cloud, mirror);
            }
        }

        /// <summary>
        /// Mirroring the world along an axis turns a rotation about axis a by angle t into a rotation about
        /// the mirrored axis by -t. In quaternion terms that negates the two vector components that are NOT
        /// the mirrored axis and keeps w. Applied once per mirrored axis; two mirrors compose correctly.
        /// </summary>
        public static float4 FlipRotation(float4 xyzw, bool3 mirror)
        {
            float4 result = xyzw;
            if (mirror.x) result = new float4(result.x, -result.y, -result.z, result.w);
            if (mirror.y) result = new float4(-result.x, result.y, -result.z, result.w);
            if (mirror.z) result = new float4(-result.x, -result.y, result.z, result.w);
            return result;
        }

        private static void FlipSh(SplatCloud cloud, bool3 mirror)
        {
            int coefficientCount = cloud.ShCoefficientCount;
            for (int coefficient = 0; coefficient < coefficientCount; coefficient++)
            {
                bool flips = (mirror.x && FlipsWhenXMirrored[coefficient])
                    ^ (mirror.y && FlipsWhenYMirrored[coefficient])
                    ^ (mirror.z && FlipsWhenZMirrored[coefficient]);
                if (!flips) continue;

                for (int splatIndex = 0; splatIndex < cloud.Count; splatIndex++)
                {
                    int baseIndex = splatIndex * cloud.ShFloatsPerSplat + coefficient * 3;
                    cloud.Sh[baseIndex] = -cloud.Sh[baseIndex];
                    cloud.Sh[baseIndex + 1] = -cloud.Sh[baseIndex + 1];
                    cloud.Sh[baseIndex + 2] = -cloud.Sh[baseIndex + 2];
                }
            }
        }
    }
}
