using System;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Every knob the debug menu can turn, in one place. <see cref="Current"/> is what the running viewer uses:
    /// <see cref="SplatSettingsApplier"/> pushes it onto the renderers, the sorters, the URP asset and the quality
    /// controller every frame, so a change here is visible on the next frame without touching each component.
    /// Serializable so it can be saved to PlayerPrefs as JSON between runs (a phone test session survives a restart).
    /// The defaults are the desktop profile; <see cref="InitializeFrom"/> takes the device profile on start.
    /// </summary>
    [Serializable]
    public sealed class SplatDebugSettings
    {
        private const string PrefsKey = "GSplat.DebugSettings";

        // ---- Rendering
        public SplatSorterKind SorterKind = SplatSorterKind.Auto;

        /// <summary>P2: 4 = a quad (two triangles), 3 = one triangle that contains the same ellipse.</summary>
        public int VerticesPerSplat = 4;
        public float MinPixelRadius = 0.5f;
        public float MaxStdDev = GaussianSplatRenderer.DefaultMaxStdDev;
        public int ShDegree = ShMath.MaxDegree;
        public float RenderScale = 1f;

        /// <summary>P3: splats a chunk may draw per pixel of its projected area; 0 = no budget (draw everything).</summary>
        public float ChunkBudgetSplatsPerPixel = 0f;

        /// <summary>P3: a chunk never gets less than this many splats, so a far chunk still shows something.</summary>
        public int ChunkBudgetFloor = 2000;

        /// <summary>P9: a polynomial falloff instead of exp in the fragment shader.</summary>
        public bool CheapGaussian = false;

        /// <summary>P9: discard fragments under 1/255 alpha (the Mali TODO: may cost more than it saves on some tilers).</summary>
        public bool ClipLowAlpha = true;

        // ---- Sorting
        /// <summary>P5/P6: 16 (65 536 buckets) or 12 (4 096 buckets: a 16x shorter prefix scan, a cache-resident histogram).</summary>
        public int SortKeyBits = 16;

        /// <summary>P6: spread the CPU sort over several frames instead of one job per frame.</summary>
        public bool TimeSlicedCpuSort = false;
        public int CpuSortSlotsPerFrame = 131072;

        /// <summary>P5: camera movement (local units) and turn (degrees) that force a new sort.</summary>
        public float SortMoveThreshold = 0.02f;
        public float SortAngleThreshold = 0.5f;

        // ---- Quality controller (P4)
        public bool QualityControllerEnabled = true;

        /// <summary>P4: step minPixelRadius and the chunk budget before render scale (tile-based GPUs pay per primitive).</summary>
        public bool PrimitivesFirstLadder = false;

        /// <summary>P4: climb back up the ladder when the frame time has been comfortably low for a while.</summary>
        public bool StepUpWhenFast = false;

        // ---- Loading (P7)
        /// <summary>P7: a frame between the build stages so a 500k load does not freeze the app for hundreds of ms.</summary>
        public bool StagedBuild = true;

        public bool ShowOverlay = true;

        public static SplatDebugSettings Current { get; private set; } = new SplatDebugSettings();

        /// <summary>Raised after <see cref="NotifyChanged"/>; the menu redraws, the applier pushes the values.</summary>
        public static event Action Changed;

        /// <summary>Call after editing fields; saves and tells everyone.</summary>
        public static void NotifyChanged()
        {
            Save();
            Changed?.Invoke();
        }

        /// <summary>Start values from a quality profile (the device profile on a phone, desktop in the editor).</summary>
        public void InitializeFrom(SplatQualityProfile profile)
        {
            if (profile == null) return;
            MinPixelRadius = profile.MinPixelRadius;
            MaxStdDev = profile.MaxStdDev;
            ShDegree = profile.ShDegree;
        }

        public static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(Current));
            PlayerPrefs.Save();
        }

        /// <summary>Loads saved values over <paramref name="fallback"/>; returns false when nothing was saved.</summary>
        public static bool LoadOrUse(SplatDebugSettings fallback)
        {
            Current = fallback ?? new SplatDebugSettings();
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                JsonUtility.FromJsonOverwrite(json, Current);
                return true;
            }
            catch (Exception e)
            {
                // A settings file from an older build: ignore it rather than crash the viewer.
                Debug.LogWarning("GSplat: saved debug settings were unreadable and are ignored: " + e.Message);
                return false;
            }
        }

        /// <summary>Forget the saved values and go back to the profile.</summary>
        public static void ResetTo(SplatQualityProfile profile)
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            Current = new SplatDebugSettings();
            Current.InitializeFrom(profile);
            NotifyChanged();
        }

        /// <summary>One-line summary for logs and the benchmark JSON.</summary>
        public string Describe()
        {
            return $"sorter {SorterKind}, verts {VerticesPerSplat}, minPx {MinPixelRadius:F2}, reach {MaxStdDev:F2}, sh {ShDegree}, scale {RenderScale:F2}, " +
                   $"budget {ChunkBudgetSplatsPerPixel:F2}/{ChunkBudgetFloor}, cheapGauss {CheapGaussian}, clip {ClipLowAlpha}, keyBits {SortKeyBits}, " +
                   $"sliced {TimeSlicedCpuSort}/{CpuSortSlotsPerFrame}, move {SortMoveThreshold:F3}, angle {SortAngleThreshold:F2}";
        }
    }
}
