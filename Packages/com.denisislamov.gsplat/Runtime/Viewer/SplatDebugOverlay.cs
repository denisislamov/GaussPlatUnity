using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace GSplat
{
    /// <summary>
    /// TZ E10-T1: the numbers you need when a phone is slow, drawn with OnGUI so it works in any scene without UI
    /// setup. Toggle with the F3 key or the 'Visible' field.
    /// </summary>
    [AddComponentMenu("GSplat/Debug Overlay")]
    public sealed class SplatDebugOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private SplatQualityController qualityController;
        [SerializeField] private WorldLoader loader;

        private readonly StringBuilder text = new StringBuilder(512);
        private float smoothedFrameMs;
        private float nextRefresh;
        private string cachedText = "";

        public bool Visible { get => visible; set => visible = value; }

        private void Start()
        {
            if (qualityController == null) qualityController = FindFirstObjectByType<SplatQualityController>();
            if (loader == null) loader = FindFirstObjectByType<WorldLoader>();
        }

        private void Update()
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;
            smoothedFrameMs = smoothedFrameMs <= 0f ? frameMs : Mathf.Lerp(smoothedFrameMs, frameMs, 0.1f);

            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f3Key.wasPressedThisFrame) visible = !visible;
            if (!visible || !SplatDebugSettings.Current.ShowOverlay || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            cachedText = Compose();
        }

        private string Compose()
        {
            text.Clear();
            text.Append($"{1000f / Mathf.Max(smoothedFrameMs, 0.01f):F0} FPS  {smoothedFrameMs:F1} ms");
            if (qualityController != null) text.Append($"  p95 {qualityController.FrameTimeP95:F1} ms  quality step {qualityController.Step}");
            text.AppendLine();
            text.AppendLine($"GSplat {GSplatVersion.Current}  {SystemInfo.graphicsDeviceType}  {Screen.width}x{Screen.height}  compute {(SystemInfo.supportsComputeShaders ? "yes" : "no")}");

            long total = 0;
            foreach (GaussianSplatRenderer renderer in GaussianSplatRenderer.Active)
            {
                total += renderer.SplatCount;
                string sorter = renderer.Sorter is GpuCountingSorter ? "GPU" : renderer.Sorter is CpuCountingSorter cpu ? $"CPU {cpu.LastSortMilliseconds:F1} ms" : "none";
                string upload = renderer.Gpu != null && !renderer.Gpu.IsFullyUploaded ? $" uploading {renderer.Gpu.UploadedChunkCount}/{renderer.Gpu.ChunkCount}" : "";
                text.AppendLine($"{renderer.name}: {renderer.LastDrawnSplatCount:N0} / {renderer.SplatCount:N0} splats, {renderer.LastVisibleChunkCount} chunks, sort {sorter}{upload}");
            }

            if (loader != null) text.AppendLine($"World: {loader.State}  {loader.LastStatus.Message}");
            text.AppendLine($"Memory: managed {Profiler.GetMonoUsedSizeLong() / (1024 * 1024)} MB, total {Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)} MB, GPU {Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024)} MB, budget {SplatMemoryBudget.DeviceBudgetBytes() / (1024 * 1024)} MB, estimate {SplatMemoryBudget.EstimateBytes((int)total, 0) / (1024 * 1024)} MB");
            return text.ToString();
        }

        private void OnGUI()
        {
            if (!visible || !SplatDebugSettings.Current.ShowOverlay) return;

            // Inside the safe area (notches, rounded corners) and as tall as the text needs.
            Rect safe = SafeAreaPanel.GuiRect(Screen.safeArea, Screen.height);
            GUIStyle style = GUI.skin.label;
            style.fontSize = Mathf.Max(12, Screen.height / 80);
            style.wordWrap = true;
            // Leaves the top-right corner to the debug menu button, so the two never overlap.
            float menuButtonPixels = VirtualJoystick.DpToPixels(SplatDebugMenu.ButtonSizeDp + 16f);
            float x = safe.xMin + 10f;
            float y = safe.yMin + 10f;
            float width = safe.width - 20f - menuButtonPixels;
            float height = style.CalcHeight(new GUIContent(cachedText), width) + 4f;

            GUI.color = Color.black;
            GUI.Label(new Rect(x + 1f, y + 1f, width, height), cachedText, style);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, width, height), cachedText, style);
        }
    }
}
