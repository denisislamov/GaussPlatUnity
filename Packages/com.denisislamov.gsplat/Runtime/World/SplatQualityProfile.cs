using System;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Per-device caps (TZ E8-T1 / 1.1 F-08): what the viewer should ask for on this hardware. Two named profiles
    /// plus a device-based pick; every field can still be edited in the inspector.
    /// </summary>
    [Serializable]
    public sealed class SplatQualityProfile
    {
        [Tooltip("Largest level the viewer loads. 0 = no limit (desktop full-res). InnerTest caps phones at 500k.")]
        public int MaxSplatCount = 500000;

        [Tooltip("Quad reach in standard deviations. sqrt(8) desktop, sqrt(5) on phones (Spark's advice).")]
        public float MaxStdDev = GaussianSplatRenderer.DefaultMaxStdDev;

        [Tooltip("View-dependent color detail to render. 0 on phones.")]
        public int ShDegree = 0;

        [Tooltip("Seconds of crossfade when a better level replaces the current one (InnerTest: about 3).")]
        public float CrossfadeSeconds = 3f;

        [Tooltip("Application.targetFrameRate for the viewer. 0 leaves it alone.")]
        public int TargetFrameRate = 0;

        public static SplatQualityProfile Desktop()
        {
            return new SplatQualityProfile { MaxSplatCount = 0, MaxStdDev = GaussianSplatRenderer.DefaultMaxStdDev, ShDegree = ShMath.MaxDegree, CrossfadeSeconds = 3f, TargetFrameRate = 0 };
        }

        public static SplatQualityProfile Mobile()
        {
            return new SplatQualityProfile { MaxSplatCount = 500000, MaxStdDev = 2.236f, ShDegree = 0, CrossfadeSeconds = 3f, TargetFrameRate = 60 };
        }

        /// <summary>Mobile on phones and on the web (browser memory is the tightest budget we have), desktop elsewhere. Low-memory phones get a smaller cap.</summary>
        public static SplatQualityProfile ForThisDevice()
        {
            bool web = Application.platform == RuntimePlatform.WebGLPlayer;
            if (!Application.isMobilePlatform && !web) return Desktop();

            SplatQualityProfile profile = Mobile();
            // Under 3 GB of RAM (Redmi Note 12 class) 500k splats plus the app is not safe; 300k is (TZ E6-T1).
            if (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 3000) profile.MaxSplatCount = 300000;
            return profile;
        }

        public SplatQualityProfile Clone()
        {
            return (SplatQualityProfile)MemberwiseClone();
        }
    }
}
