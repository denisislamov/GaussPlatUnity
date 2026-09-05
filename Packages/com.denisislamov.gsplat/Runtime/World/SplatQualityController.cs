using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GSplat
{
    /// <summary>
    /// Watches the frame time and steps quality down a ladder when the device cannot keep up (TZ E8-T3):
    /// render scale 1.0 -> 0.85 -> 0.7, then maxStdDev to sqrt(5), then SH off. Quality is never raised back
    /// within <see cref="holdSeconds"/> so it cannot oscillate. Works on whatever renderers are active.
    /// </summary>
    [AddComponentMenu("GSplat/Quality Controller")]
    public sealed class SplatQualityController : MonoBehaviour
    {
        [SerializeField, Tooltip("Frame time (ms) whose 95th percentile over the window triggers a step down. 40 ms = below 25 FPS.")]
        private float slowFrameMilliseconds = 40f;

        [SerializeField, Tooltip("Seconds of frames the percentile is taken over.")]
        private float windowSeconds = 3f;

        [SerializeField, Tooltip("After a step down, nothing changes for this long (hysteresis).")]
        private float holdSeconds = 30f;

        [SerializeField, Tooltip("Frames to ignore after start or after a level change (uploads and shader warm-up are slow and not representative).")]
        private int warmupFrames = 60;

        private readonly List<float> frameTimes = new List<float>(512);
        private static readonly float[] RenderScaleLadder = { 1f, 0.85f, 0.7f };
        private int step;
        private float lastStepTime = float.NegativeInfinity;
        private int framesSeen;

        /// <summary>0 = full quality; each step is one rung down the ladder.</summary>
        public int Step => step;

        /// <summary>Text for the UI: why quality was reduced the last time.</summary>
        public event Action<string> QualityReduced;

        /// <summary>The 95th percentile frame time (ms) over the window, for the debug overlay.</summary>
        public float FrameTimeP95 { get; private set; }

        private void Update()
        {
            frameTimes.Add(Time.unscaledDeltaTime * 1000f);
            framesSeen++;
            int windowFrames = Mathf.Max(30, Mathf.RoundToInt(windowSeconds / Mathf.Max(Time.unscaledDeltaTime, 1e-3f)));
            if (frameTimes.Count > windowFrames) frameTimes.RemoveRange(0, frameTimes.Count - windowFrames);
            FrameTimeP95 = Percentile95();

            if (framesSeen < warmupFrames || frameTimes.Count < 30) return;
            if (Time.unscaledTime - lastStepTime < holdSeconds) return;
            if (FrameTimeP95 <= slowFrameMilliseconds) return;

            if (StepDown()) lastStepTime = Time.unscaledTime;
        }

        /// <summary>Forget the history, e.g. after a new level arrives.</summary>
        public void ResetWindow()
        {
            frameTimes.Clear();
            framesSeen = 0;
        }

        private bool StepDown()
        {
            step++;
            string reason;
            if (step < RenderScaleLadder.Length)
            {
                var pipeline = UniversalRenderPipeline.asset;
                if (pipeline != null) pipeline.renderScale = RenderScaleLadder[step];
                reason = $"Frame time {FrameTimeP95:F0} ms: render scale lowered to {RenderScaleLadder[step]:F2}";
            }
            else if (step == RenderScaleLadder.Length)
            {
                foreach (GaussianSplatRenderer renderer in GaussianSplatRenderer.Active) renderer.MaxStdDev = 2.236f;
                reason = $"Frame time {FrameTimeP95:F0} ms: splat reach reduced to sqrt(5)";
            }
            else if (step == RenderScaleLadder.Length + 1)
            {
                foreach (GaussianSplatRenderer renderer in GaussianSplatRenderer.Active) renderer.ShDegree = 0;
                reason = $"Frame time {FrameTimeP95:F0} ms: view-dependent color disabled";
            }
            else
            {
                step = RenderScaleLadder.Length + 1; // bottom of the ladder; the level fallback belongs to WorldLoader (low memory)
                return false;
            }

            Debug.LogWarning("GSplat: " + reason);
            QualityReduced?.Invoke(reason);
            return true;
        }

        private float Percentile95()
        {
            if (frameTimes.Count == 0) return 0f;
            var sorted = new List<float>(frameTimes);
            sorted.Sort();
            return sorted[Mathf.Clamp(Mathf.RoundToInt((sorted.Count - 1) * 0.95f), 0, sorted.Count - 1)];
        }
    }
}
