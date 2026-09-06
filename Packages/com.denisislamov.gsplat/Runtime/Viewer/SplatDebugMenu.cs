using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GSplat
{
    /// <summary>
    /// The debug menu: a small button in the top-right corner (the overlay text keeps clear of it) opens a panel with
    /// every knob of <see cref="SplatDebugSettings"/> as a toggle, a slider or a choice, plus the benchmark. Built in
    /// code like the viewer UI, on its own canvas so it works in any scene that has the applier.
    /// </summary>
    [AddComponentMenu("GSplat/Debug Menu")]
    public sealed class SplatDebugMenu : MonoBehaviour
    {
        /// <summary>Width of the corner button in dp; the overlay leaves this much room on the right.</summary>
        public const float ButtonSizeDp = 44f;

        private const float PanelWidthDp = 340f;
        private const float RowHeightDp = 34f;
        private const int LabelFontSize = 14;

        private GameObject panel;
        private Text benchmarkStatus;
        private readonly List<Action> refreshers = new List<Action>();
        private bool suppressCallbacks;

        private void Start()
        {
            Build();
            SplatDebugSettings.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            SplatDebugSettings.Changed -= Refresh;
        }

        private void Build()
        {
            UiFactory.EnsureEventSystem(transform);
            Canvas canvas = UiFactory.CreateCanvas("GSplat Debug Menu", transform, 120);
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = VirtualJoystick.DpToPixels(1f);
            RectTransform safeArea = UiFactory.CreateSafeArea(canvas.transform);

            Button open = UiFactory.CreateButton(safeArea, "Menu Button", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(ButtonSizeDp, ButtonSizeDp), 0.7f, "≡", 24);
            open.onClick.AddListener(TogglePanel);

            Image background = UiFactory.CreatePanel(safeArea, "Panel", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-PanelWidthDp - 8f, 8f), new Vector2(-8f, -8f - ButtonSizeDp - 8f), new Color(0f, 0f, 0f, 0.85f));
            panel = background.gameObject;
            RectTransform content = UiFactory.CreateScrollView(panel.transform, 4f, new RectOffset(10, 10, 10, 10));

            AddHeader(content, $"GSplat {GSplatVersion.Current} debug");
            AddChoice(content, "Sorter", new[] { "Auto", "GPU", "CPU" }, () => (int)SplatDebugSettings.Current.SorterKind, v => SplatDebugSettings.Current.SorterKind = (SplatSorterKind)v);
            AddChoice(content, "Vertices per splat (P2)", new[] { "4 (quad)", "3 (triangle)" }, () => SplatDebugSettings.Current.VerticesPerSplat == 3 ? 1 : 0, v => SplatDebugSettings.Current.VerticesPerSplat = v == 1 ? 3 : 4);
            AddSlider(content, "Min pixel radius", 0f, 3f, false, () => SplatDebugSettings.Current.MinPixelRadius, v => SplatDebugSettings.Current.MinPixelRadius = v, "F2");
            AddChoice(content, "Quad reach", new[] { "sqrt(8)", "sqrt(5)" }, () => SplatDebugSettings.Current.MaxStdDev < 2.5f ? 1 : 0, v => SplatDebugSettings.Current.MaxStdDev = v == 1 ? 2.236f : GaussianSplatRenderer.DefaultMaxStdDev);
            AddSlider(content, "SH degree", 0f, ShMath.MaxDegree, true, () => SplatDebugSettings.Current.ShDegree, v => SplatDebugSettings.Current.ShDegree = (int)v, "F0");
            AddSlider(content, "Render scale", 0.5f, 1f, false, () => SplatDebugSettings.Current.RenderScale, v => SplatDebugSettings.Current.RenderScale = v, "F2");

            AddHeader(content, "Chunk budget (P3, needs importance-ordered data)");
            AddSlider(content, "Splats per pixel (0 = off)", 0f, 4f, false, () => SplatDebugSettings.Current.ChunkBudgetSplatsPerPixel, v => SplatDebugSettings.Current.ChunkBudgetSplatsPerPixel = v, "F2");
            AddSlider(content, "Budget floor", 0f, 20000f, true, () => SplatDebugSettings.Current.ChunkBudgetFloor, v => SplatDebugSettings.Current.ChunkBudgetFloor = (int)v, "F0");

            AddHeader(content, "Fragment (P9)");
            AddToggle(content, "Cheap Gaussian", () => SplatDebugSettings.Current.CheapGaussian, v => SplatDebugSettings.Current.CheapGaussian = v);
            AddToggle(content, "Clip alpha < 1/255", () => SplatDebugSettings.Current.ClipLowAlpha, v => SplatDebugSettings.Current.ClipLowAlpha = v);

            AddHeader(content, "Sorting (P5, P6)");
            AddChoice(content, "Key bits", new[] { "16", "12" }, () => SplatDebugSettings.Current.SortKeyBits == 12 ? 1 : 0, v => SplatDebugSettings.Current.SortKeyBits = v == 1 ? 12 : 16);
            AddToggle(content, "Time-sliced CPU sort", () => SplatDebugSettings.Current.TimeSlicedCpuSort, v => SplatDebugSettings.Current.TimeSlicedCpuSort = v);
            AddSlider(content, "Slots per frame (k)", 16f, 512f, true, () => SplatDebugSettings.Current.CpuSortSlotsPerFrame / 1024f, v => SplatDebugSettings.Current.CpuSortSlotsPerFrame = (int)v * 1024, "F0");
            AddSlider(content, "Re-sort after move (m)", 0f, 0.5f, false, () => SplatDebugSettings.Current.SortMoveThreshold, v => SplatDebugSettings.Current.SortMoveThreshold = v, "F3");
            AddSlider(content, "Re-sort after turn (deg)", 0f, 10f, false, () => SplatDebugSettings.Current.SortAngleThreshold, v => SplatDebugSettings.Current.SortAngleThreshold = v, "F1");

            AddHeader(content, "Quality controller (P4)");
            AddToggle(content, "Enabled", () => SplatDebugSettings.Current.QualityControllerEnabled, v => SplatDebugSettings.Current.QualityControllerEnabled = v);
            AddToggle(content, "Primitives first ladder", () => SplatDebugSettings.Current.PrimitivesFirstLadder, v => SplatDebugSettings.Current.PrimitivesFirstLadder = v);
            AddToggle(content, "Step up when fast", () => SplatDebugSettings.Current.StepUpWhenFast, v => SplatDebugSettings.Current.StepUpWhenFast = v);

            AddHeader(content, "Loading (P7)");
            AddToggle(content, "Staged build", () => SplatDebugSettings.Current.StagedBuild, v => SplatDebugSettings.Current.StagedBuild = v);

            AddHeader(content, "Benchmark (P1)");
            AddAction(content, "Run 20 s with current settings", () => SplatBenchmark.Run(SplatBenchmark.SingleVariant()));
            AddAction(content, "Run the knob matrix", () => SplatBenchmark.Run(SplatBenchmark.KnobMatrix()));
            benchmarkStatus = AddLabel(content, "");

            AddHeader(content, "");
            AddToggle(content, "Show overlay", () => SplatDebugSettings.Current.ShowOverlay, v => SplatDebugSettings.Current.ShowOverlay = v);
            AddAction(content, "Reset to device profile", () =>
            {
                var applier = FindFirstObjectByType<SplatSettingsApplier>();
                SplatDebugSettings.ResetTo(applier != null ? applier.Profile : SplatQualityProfile.ForThisDevice());
            });

            panel.SetActive(false);
        }

        private void Update()
        {
            if (benchmarkStatus != null && panel.activeSelf) benchmarkStatus.text = SplatBenchmark.Status;
        }

        private void TogglePanel()
        {
            panel.SetActive(!panel.activeSelf);
            if (panel.activeSelf) Refresh();
        }

        private void Refresh()
        {
            suppressCallbacks = true;
            foreach (Action refresher in refreshers) refresher();
            suppressCallbacks = false;
        }

        private void Changed()
        {
            if (!suppressCallbacks) SplatDebugSettings.NotifyChanged();
        }

        // ---- Rows

        private static RectTransform AddRow(Transform content, string name, float height)
        {
            var rowObject = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            rowObject.transform.SetParent(content, false);
            rowObject.GetComponent<LayoutElement>().preferredHeight = height;
            return (RectTransform)rowObject.transform;
        }

        private static Text AddRowLabel(RectTransform row, string label)
        {
            var text = UiFactory.CreateText(row, "Label", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(170f, RowHeightDp), LabelFontSize, TextAnchor.MiddleLeft);
            text.text = label;
            return text;
        }

        private void AddHeader(Transform content, string title)
        {
            RectTransform row = AddRow(content, "Header", RowHeightDp * 0.8f);
            Text text = UiFactory.CreateText(row, "Title", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(PanelWidthDp - 20f, RowHeightDp), LabelFontSize, TextAnchor.LowerLeft);
            text.text = title;
            text.color = new Color(1f, 0.85f, 0.4f);
        }

        private Text AddLabel(Transform content, string value)
        {
            RectTransform row = AddRow(content, "Info", RowHeightDp);
            Text text = UiFactory.CreateText(row, "Info", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(PanelWidthDp - 20f, RowHeightDp), LabelFontSize - 2, TextAnchor.MiddleLeft);
            text.text = value;
            return text;
        }

        private void AddToggle(Transform content, string label, Func<bool> get, Action<bool> set)
        {
            RectTransform row = AddRow(content, label, RowHeightDp);
            AddRowLabel(row, label);
            Button button = UiFactory.CreateButton(row, "Toggle", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(90f, RowHeightDp - 6f), 0.8f, "", LabelFontSize);
            Text buttonLabel = button.GetComponentInChildren<Text>();
            button.onClick.AddListener(() => { set(!get()); Changed(); });
            refreshers.Add(() => buttonLabel.text = get() ? "On" : "Off");
        }

        private void AddChoice(Transform content, string label, string[] names, Func<int> get, Action<int> set)
        {
            RectTransform row = AddRow(content, label, RowHeightDp);
            AddRowLabel(row, label);
            Button button = UiFactory.CreateButton(row, "Choice", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(140f, RowHeightDp - 6f), 0.8f, "", LabelFontSize);
            Text buttonLabel = button.GetComponentInChildren<Text>();
            button.onClick.AddListener(() => { set((get() + 1) % names.Length); Changed(); });
            refreshers.Add(() => buttonLabel.text = names[Mathf.Clamp(get(), 0, names.Length - 1)]);
        }

        private void AddSlider(Transform content, string label, float min, float max, bool wholeNumbers, Func<float> get, Action<float> set, string format)
        {
            RectTransform row = AddRow(content, label, RowHeightDp);
            AddRowLabel(row, label);
            Text value = UiFactory.CreateText(row, "Value", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(56f, RowHeightDp), LabelFontSize, TextAnchor.MiddleRight);
            Slider slider = UiFactory.CreateSlider(row, "Slider", min, max, wholeNumbers, new Vector2(84f, RowHeightDp - 6f));
            var sliderRect = (RectTransform)slider.transform;
            sliderRect.anchorMin = new Vector2(1f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.pivot = new Vector2(1f, 0.5f);
            sliderRect.anchoredPosition = new Vector2(-60f, 0f);
            slider.onValueChanged.AddListener(v =>
            {
                if (suppressCallbacks) return;
                set(v);
                value.text = v.ToString(format);
                Changed();
            });
            refreshers.Add(() => { slider.SetValueWithoutNotify(get()); value.text = get().ToString(format); });
        }

        private void AddAction(Transform content, string label, Action action)
        {
            RectTransform row = AddRow(content, label, RowHeightDp);
            Button button = UiFactory.CreateButton(row, "Action", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidthDp - 24f, RowHeightDp - 6f), 0.8f, label, LabelFontSize);
            button.onClick.AddListener(() => action());
        }
    }
}
