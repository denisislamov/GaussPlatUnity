using UnityEngine;
using UnityEngine.UI;

namespace GSplat
{
    /// <summary>
    /// Builds the viewer overlay in code (no prefab to keep in sync): status line at the top, joystick bottom-left,
    /// Reset bottom-right, and a quality notice. Wires itself to a <see cref="WorldLoader"/>, a
    /// <see cref="SplatFlyCamera"/> and an optional <see cref="SplatQualityController"/> on the same scene.
    /// </summary>
    [AddComponentMenu("GSplat/Viewer UI")]
    public sealed class SplatViewerUi : MonoBehaviour
    {
        [SerializeField] private WorldLoader loader;
        [SerializeField] private SplatFlyCamera flyCamera;
        [SerializeField] private SplatQualityController qualityController;
        [SerializeField, Tooltip("Show the joystick and Reset only on touch devices; on desktop the keyboard does the job.")]
        private bool touchControlsOnlyOnMobile = true;

        /// <summary>Width and height of the status and notice labels.</summary>
        private static readonly Vector2 TextSize = new Vector2(600f, 40f);

        private Text statusText;
        private Text noticeText;
        private VirtualJoystick joystick;
        private float noticeUntil;

        private void Start()
        {
            if (loader == null) loader = FindFirstObjectByType<WorldLoader>();
            if (flyCamera == null) flyCamera = FindFirstObjectByType<SplatFlyCamera>();
            if (qualityController == null) qualityController = FindFirstObjectByType<SplatQualityController>();

            Build();
            if (loader != null)
            {
                loader.StateChanged += OnState;
                loader.StatusChanged += OnStatus;
                loader.SpawnKnown += OnSpawnKnown;
                OnState(loader.State);
            }

            if (qualityController != null) qualityController.QualityReduced += ShowNotice;
        }

        private void OnDestroy()
        {
            if (loader != null)
            {
                loader.StateChanged -= OnState;
                loader.StatusChanged -= OnStatus;
                loader.SpawnKnown -= OnSpawnKnown;
            }

            if (qualityController != null) qualityController.QualityReduced -= ShowNotice;
        }

        private void Update()
        {
            if (flyCamera != null && joystick != null) flyCamera.JoystickInput = joystick.Value;
            if (noticeText != null && noticeText.enabled && Time.unscaledTime > noticeUntil) noticeText.enabled = false;
        }

        private void Build()
        {
            UiFactory.EnsureEventSystem(transform);

            Canvas canvas = UiFactory.CreateCanvas("GSplat Canvas", transform, 100);
            // Constant physical size: the joystick's dp sizes are handled in VirtualJoystick, the rest scales with dpi here.
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = VirtualJoystick.DpToPixels(1f);

            // Everything sits inside a panel that follows Screen.safeArea, so notches and rounded corners never clip it.
            RectTransform safeArea = UiFactory.CreateSafeArea(canvas.transform);

            statusText = UiFactory.CreateText(safeArea, "Status", new Vector2(0.5f, 1f), new Vector2(0f, -24f), TextSize, 18, TextAnchor.UpperCenter);
            noticeText = UiFactory.CreateText(safeArea, "Notice", new Vector2(0.5f, 1f), new Vector2(0f, -52f), TextSize, 14, TextAnchor.UpperCenter);
            noticeText.color = new Color(1f, 0.85f, 0.4f);
            noticeText.enabled = false;

            bool showTouchControls = !touchControlsOnlyOnMobile || Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer;
            if (!showTouchControls) return;

            joystick = CreateJoystick(safeArea);
            CreateResetButton(safeArea);
        }

        private static VirtualJoystick CreateJoystick(Transform parent)
        {
            float zone = VirtualJoystick.ZoneSizeDp;
            float knob = VirtualJoystick.KnobSizeDp;

            var zoneObject = new GameObject("Joystick", typeof(Image), typeof(VirtualJoystick));
            zoneObject.transform.SetParent(parent, false);
            var zoneRect = (RectTransform)zoneObject.transform;
            zoneRect.anchorMin = new Vector2(0f, 0f);
            zoneRect.anchorMax = new Vector2(0f, 0f);
            zoneRect.pivot = new Vector2(0.5f, 0.5f);
            zoneRect.anchoredPosition = new Vector2(zone * 0.5f + 24f, zone * 0.5f + 72f); // above the scene menu row
            zoneRect.sizeDelta = new Vector2(zone, zone);
            Image zoneImage = zoneObject.GetComponent<Image>();
            zoneImage.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            zoneImage.color = new Color(1f, 1f, 1f, 0.25f);

            var knobObject = new GameObject("Knob", typeof(Image));
            knobObject.transform.SetParent(zoneObject.transform, false);
            var knobRect = (RectTransform)knobObject.transform;
            knobRect.sizeDelta = new Vector2(knob, knob);
            Image knobImage = knobObject.GetComponent<Image>();
            knobImage.sprite = zoneImage.sprite;
            knobImage.color = new Color(1f, 1f, 1f, 0.7f);
            knobImage.raycastTarget = false;

            VirtualJoystick stick = zoneObject.GetComponent<VirtualJoystick>();
            stick.Initialize(knobRect);
            return stick;
        }

        private void CreateResetButton(Transform parent)
        {
            // Bottom-right corner, above the scene menu row.
            Button reset = UiFactory.CreateButton(parent, "Reset", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 72f), new Vector2(96f, 48f), 0.6f, "Reset", 16);
            reset.onClick.AddListener(() => flyCamera?.ResetToSpawn());
        }

        private void OnState(WorldLoadState state)
        {
            if (statusText == null) return;
            switch (state)
            {
                case WorldLoadState.Idle: statusText.text = "No world set. Put a descriptor .json or a .spz URL into World Url on the World object."; break;
                case WorldLoadState.LoadingDescriptor: statusText.text = "Loading world…"; break;
                case WorldLoadState.LoadingFirstLevel: statusText.text = "Loading preview…"; break;
                case WorldLoadState.ShowingFirstLevel: statusText.text = "Preview"; break;
                case WorldLoadState.LoadingFinalLevel: statusText.text = "Loading full quality…"; break;
                case WorldLoadState.Crossfading: statusText.text = "Switching to full quality"; break;
                case WorldLoadState.Ready: statusText.text = loader.CurrentLevel != null && loader.CurrentLevel.splatCount != int.MaxValue ? $"{loader.CurrentLevel.splatCount / 1000}k splats" : ""; break;
                case WorldLoadState.Failed: statusText.text = ErrorText(loader.LastError, loader.LastErrorMessage); break;
            }

            if (state == WorldLoadState.Ready && flyCamera != null && loader.CurrentRenderer != null)
            {
                flyCamera.SetLimitBounds(loader.CurrentRenderer.WorldBounds);
            }
        }

        private void OnSpawnKnown(Vector3 position, Quaternion rotation)
        {
            if (flyCamera == null) return;
            flyCamera.SetSpawn(position, rotation);
            flyCamera.ResetToSpawn();
        }

        private void OnStatus(SplatLoadStatus status)
        {
            if (statusText == null) return;
            if (status.Stage == SplatLoadStage.Downloading) statusText.text = $"Downloading… {status.Progress:P0}";
            else if (status.Stage == SplatLoadStage.Decoding) statusText.text = "Decoding…";
            else if (status.Stage == SplatLoadStage.Building) statusText.text = "Preparing…";
        }

        /// <summary>User-facing wording for each error (TZ E8-T4); the technical message goes to the log.</summary>
        public static string ErrorText(SplatLoadError error, string detail)
        {
            switch (error)
            {
                case SplatLoadError.NotFound: return "This world does not exist (404).";
                case SplatLoadError.Network: return "No connection. Check the network and try again.";
                case SplatLoadError.UnsupportedFormat: return "This file format is not supported: " + detail;
                case SplatLoadError.Corrupted: return "The world file is damaged.";
                case SplatLoadError.OutOfMemory: return "Not enough memory for this world on this device.";
                case SplatLoadError.Cancelled: return "Loading cancelled.";
                default: return "Could not load the world: " + detail;
            }
        }

        private void ShowNotice(string message)
        {
            if (noticeText == null) return;
            noticeText.text = message;
            noticeText.enabled = true;
            noticeUntil = Time.unscaledTime + 6f;
        }
    }
}
