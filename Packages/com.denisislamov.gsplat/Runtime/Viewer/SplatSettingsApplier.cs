using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GSplat
{
    /// <summary>
    /// Makes <see cref="SplatDebugSettings.Current"/> the source of truth for the running viewer: every frame the
    /// values are pushed onto every active renderer (also the ones the WorldLoader creates later), the URP render
    /// scale and the quality controller. On start the settings come from the saved PlayerPrefs, or from the device
    /// profile when nothing was saved. Scenes without this component keep their inspector values.
    /// </summary>
    [AddComponentMenu("GSplat/Settings Applier")]
    [DefaultExecutionOrder(-50)]
    public sealed class SplatSettingsApplier : MonoBehaviour
    {
        [SerializeField, Tooltip("Use the device profile as the starting point in the editor too (off: the desktop profile, so the editor shows full quality).")]
        private bool deviceProfileInEditor;

        private SplatQualityController qualityController;

        /// <summary>The profile the settings were reset to; the menu's Reset goes back to it.</summary>
        public SplatQualityProfile Profile { get; private set; }

        private void Awake()
        {
            Profile = Application.isEditor && !deviceProfileInEditor ? SplatQualityProfile.Desktop() : SplatQualityProfile.ForThisDevice();
            var fallback = new SplatDebugSettings();
            fallback.InitializeFrom(Profile);
            bool restored = SplatDebugSettings.LoadOrUse(fallback);
            if (Profile.TargetFrameRate > 0) Application.targetFrameRate = Profile.TargetFrameRate;
            Debug.Log($"GSplat: settings {(restored ? "restored from the last run" : "from the device profile")}: {SplatDebugSettings.Current.Describe()}");
        }

        private void Start()
        {
            qualityController = FindFirstObjectByType<SplatQualityController>();
        }

        private void LateUpdate()
        {
            Apply(SplatDebugSettings.Current, qualityController);
        }

        /// <summary>Pushes the settings everywhere they matter. Cheap: a few property sets per renderer.</summary>
        public static void Apply(SplatDebugSettings settings, SplatQualityController controller)
        {
            foreach (GaussianSplatRenderer renderer in GaussianSplatRenderer.Active)
            {
                renderer.MinPixelRadius = settings.MinPixelRadius;
                renderer.MaxStdDev = settings.MaxStdDev;
                renderer.ShDegree = settings.ShDegree;
                renderer.VerticesPerSplat = settings.VerticesPerSplat;
                renderer.ChunkBudgetSplatsPerPixel = settings.ChunkBudgetSplatsPerPixel;
                renderer.ChunkBudgetFloor = settings.ChunkBudgetFloor;
                renderer.CheapGaussian = settings.CheapGaussian;
                renderer.ClipLowAlpha = settings.ClipLowAlpha;
                renderer.SortKeyBits = settings.SortKeyBits;
                renderer.TimeSlicedCpuSort = settings.TimeSlicedCpuSort;
                renderer.CpuSortSlotsPerFrame = settings.CpuSortSlotsPerFrame;
                renderer.SortMoveThreshold = settings.SortMoveThreshold;
                renderer.SortAngleThreshold = settings.SortAngleThreshold;
                if (renderer.SorterKind != settings.SorterKind) renderer.SetSorterKind(settings.SorterKind);
            }

            UniversalRenderPipelineAsset pipeline = UniversalRenderPipeline.asset;
            if (pipeline != null && !Mathf.Approximately(pipeline.renderScale, settings.RenderScale)) pipeline.renderScale = settings.RenderScale;

            if (controller != null)
            {
                controller.enabled = settings.QualityControllerEnabled;
                controller.PrimitivesFirst = settings.PrimitivesFirstLadder;
                controller.StepUpWhenFast = settings.StepUpWhenFast;
            }
        }
    }
}
