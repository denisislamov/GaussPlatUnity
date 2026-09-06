using System;
using System.Collections.Generic;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Watches the frame time and steps quality down a ladder when the device cannot keep up (TZ E8-T3), and
    /// optionally back up when it has been comfortably fast for a while (P4). The rungs edit
    /// <see cref="SplatDebugSettings.Current"/>, which <see cref="SplatSettingsApplier"/> pushes onto the renderers and
    /// the URP asset; a scene without an applier gets the values pushed directly. Two ladders:
    /// - classic: render scale 1.0 -> 0.85 -> 0.7, then quad reach sqrt(5), then SH off;
    /// - primitives first (P4): minPixelRadius 1 -> 1.5 -> 2, then the chunk budget, then render scale, reach, SH.
    ///   On tile-based GPUs the number of primitives is what costs (ADR-002), so this ladder attacks that first.
    /// Nothing changes within <see cref="holdSeconds"/> of the last step so it cannot oscillate.
    /// </summary>
    [AddComponentMenu("GSplat/Quality Controller")]
    public sealed class SplatQualityController : MonoBehaviour
    {
        [SerializeField, Tooltip("Frame time (ms) whose 95th percentile over the window triggers a step down. 40 ms = below 25 FPS.")]
        private float slowFrameMilliseconds = 40f;
        [SerializeField, Tooltip("Step back up when the 95th percentile is under this fraction of the slow threshold (P4, only with Step Up When Fast).")]
        private float fastFraction = 0.6f;
        [SerializeField, Tooltip("Seconds of frames the percentile is taken over.")]
        private float windowSeconds = 3f;
        [SerializeField, Tooltip("After a step, nothing changes for this long (hysteresis).")]
        private float holdSeconds = 30f;
        [SerializeField, Tooltip("Frames to ignore after start or after a level change (uploads and shader warm-up are slow and not representative).")]
        private int warmupFrames = 60;
        [SerializeField, Tooltip("P4: attack primitives (minPixelRadius, chunk budget) before fill (render scale).")]
        private bool primitivesFirst;
        [SerializeField, Tooltip("P4: climb back up the ladder when the frame time stays low.")]
        private bool stepUpWhenFast;

        private readonly List<float> frameTimes = new List<float>(512);
        private readonly List<float> sortedScratch = new List<float>(512);
        private int step;
        private float lastStepTime = float.NegativeInfinity;
        private int framesSeen;
        private SplatDebugSettings before;

        /// <summary>0 = full quality; each step is one rung down the ladder.</summary>
        public int Step => step;
        public bool PrimitivesFirst { get => primitivesFirst; set => primitivesFirst = value; }
        public bool StepUpWhenFast { get => stepUpWhenFast; set => stepUpWhenFast = value; }

        /// <summary>Text for the UI: why quality changed the last time.</summary>
        public event Action<string> QualityReduced;

        /// <summary>The 95th percentile frame time (ms) over the window, for the debug overlay.</summary>
        public float FrameTimeP95 { get; private set; }

        /// <summary>The rungs of the active ladder, top to bottom; each applies one change to the settings.</summary>
        private IReadOnlyList<Rung> Ladder => primitivesFirst ? PrimitivesFirstLadder : ClassicLadder;

        private static readonly Rung[] ClassicLadder =
        {
            new Rung("render scale 0.85", s => s.RenderScale = 0.85f),
            new Rung("render scale 0.7", s => s.RenderScale = 0.7f),
            new Rung("splat reach sqrt(5)", s => s.MaxStdDev = 2.236f),
            new Rung("view-dependent color off", s => s.ShDegree = 0)
        };

        private static readonly Rung[] PrimitivesFirstLadder =
        {
            new Rung("min pixel radius 1.5", s => s.MinPixelRadius = Mathf.Max(s.MinPixelRadius, 1.5f)),
            new Rung("min pixel radius 2", s => s.MinPixelRadius = Mathf.Max(s.MinPixelRadius, 2f)),
            new Rung("chunk budget 0.5 splats per pixel", s => s.ChunkBudgetSplatsPerPixel = 0.5f),
            new Rung("render scale 0.85", s => s.RenderScale = 0.85f),
            new Rung("render scale 0.7", s => s.RenderScale = 0.7f),
            new Rung("splat reach sqrt(5)", s => s.MaxStdDev = 2.236f),
            new Rung("view-dependent color off", s => s.ShDegree = 0)
        };

        private void Update()
        {
            frameTimes.Add(Time.unscaledDeltaTime * 1000f);
            framesSeen++;
            int windowFrames = Mathf.Max(30, Mathf.RoundToInt(windowSeconds / Mathf.Max(Time.unscaledDeltaTime, 1e-3f)));
            if (frameTimes.Count > windowFrames) frameTimes.RemoveRange(0, frameTimes.Count - windowFrames);
            FrameTimeP95 = Percentile95();

            if (framesSeen < warmupFrames || frameTimes.Count < 30) return;
            if (Time.unscaledTime - lastStepTime < holdSeconds) return;

            if (FrameTimeP95 > slowFrameMilliseconds)
            {
                if (StepDown()) lastStepTime = Time.unscaledTime;
            }
            else if (stepUpWhenFast && step > 0 && FrameTimeP95 < slowFrameMilliseconds * fastFraction)
            {
                StepUp();
                lastStepTime = Time.unscaledTime;
            }
        }

        /// <summary>Forget the history, e.g. after a new level arrives.</summary>
        public void ResetWindow()
        {
            frameTimes.Clear();
            framesSeen = 0;
        }

        private bool StepDown()
        {
            IReadOnlyList<Rung> ladder = Ladder;
            if (step >= ladder.Count) return false; // bottom of the ladder; the level fallback belongs to WorldLoader (low memory)

            if (step == 0) before = JsonUtility.FromJson<SplatDebugSettings>(JsonUtility.ToJson(SplatDebugSettings.Current)); // to climb back to
            ladder[step].Apply(SplatDebugSettings.Current);
            step++;
            Announce($"Frame time {FrameTimeP95:F0} ms: {ladder[step - 1].Name}");
            return true;
        }

        /// <summary>One rung up: the settings from before the descent, with the rungs still below re-applied.</summary>
        private void StepUp()
        {
            step--;
            SplatDebugSettings restored = JsonUtility.FromJson<SplatDebugSettings>(JsonUtility.ToJson(before ?? SplatDebugSettings.Current));
            IReadOnlyList<Rung> ladder = Ladder;
            for (int rung = 0; rung < step && rung < ladder.Count; rung++) ladder[rung].Apply(restored);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(restored), SplatDebugSettings.Current);
            Announce($"Frame time {FrameTimeP95:F0} ms: quality back up to step {step}");
        }

        private void Announce(string reason)
        {
            Debug.Log("GSplat: " + reason);
            SplatDebugSettings.NotifyChanged();
            if (FindFirstObjectByType<SplatSettingsApplier>() == null) SplatSettingsApplier.Apply(SplatDebugSettings.Current, this);
            QualityReduced?.Invoke(reason);
        }

        private float Percentile95()
        {
            if (frameTimes.Count == 0) return 0f;
            sortedScratch.Clear();
            sortedScratch.AddRange(frameTimes);
            sortedScratch.Sort();
            return sortedScratch[Mathf.Clamp(Mathf.RoundToInt((sortedScratch.Count - 1) * 0.95f), 0, sortedScratch.Count - 1)];
        }

        private sealed class Rung
        {
            public readonly string Name;
            public readonly Action<SplatDebugSettings> Apply;

            public Rung(string name, Action<SplatDebugSettings> apply)
            {
                Name = name;
                Apply = apply;
            }
        }
    }
}
