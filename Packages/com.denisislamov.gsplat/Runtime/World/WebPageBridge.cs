using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// The three entry points the WebGL page calls through SendMessage (see Assets/WebGLTemplates/GSplatViewer/index.html).
    /// Lives next to the <see cref="WorldLoader"/> on the same GameObject so the page keeps addressing "World"; kept
    /// apart from the loader so the loader only loads.
    /// </summary>
    [RequireComponent(typeof(WorldLoader))]
    [AddComponentMenu("GSplat/Web Page Bridge")]
    public sealed class WebPageBridge : MonoBehaviour
    {
        private WorldLoader loader;

        private void Awake()
        {
            loader = GetComponent<WorldLoader>();
        }

        /// <summary>The page sends ?world=... once Unity is up.</summary>
        public void LoadFromPage(string url)
        {
            loader.WorldUrl = url;
            _ = loader.LoadAsync(url);
        }

        /// <summary>Page hidden: render at 1 fps to save battery and avoid GPU context churn.</summary>
        public void PauseFromPage(string unused)
        {
            Application.targetFrameRate = 1;
        }

        /// <summary>?bench=single or ?bench=matrix: run the benchmark once the world is ready (the runner waits for it).</summary>
        public void RunBenchmarkFromPage(string kind)
        {
            SplatBenchmark.Run(kind == "matrix" ? SplatBenchmark.KnobMatrix() : SplatBenchmark.SingleVariant());
        }

        public void ResumeFromPage(string unused)
        {
            SplatQualityProfile profile = loader.ActiveProfile;
            Application.targetFrameRate = profile != null && profile.TargetFrameRate > 0 ? profile.TargetFrameRate : -1;
        }
    }
}
