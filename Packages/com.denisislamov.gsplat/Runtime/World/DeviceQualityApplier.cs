using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// For scenes assembled by hand (the sample scenes): on Start, pushes the device's quality profile onto every
    /// active <see cref="GaussianSplatRenderer"/>, so a phone gets the phone settings without a WorldLoader.
    /// </summary>
    [AddComponentMenu("GSplat/Device Quality Applier")]
    public sealed class DeviceQualityApplier : MonoBehaviour
    {
        [SerializeField, Tooltip("Apply the device profile in the editor too (off: only in players, so the editor shows full quality).")]
        private bool applyInEditor;

        public SplatQualityProfile Applied { get; private set; }

        private void Start()
        {
            if (Application.isEditor && !applyInEditor) return;

            Applied = SplatQualityProfile.ForThisDevice();
            if (Applied.TargetFrameRate > 0) Application.targetFrameRate = Applied.TargetFrameRate;
            foreach (GaussianSplatRenderer renderer in GaussianSplatRenderer.Active)
            {
                Applied.ApplyTo(renderer);
            }

            Debug.Log($"GSplat: device profile applied - maxStdDev {Applied.MaxStdDev:F2}, SH {Applied.ShDegree}, minPixelRadius {Applied.MinPixelRadius}, cap {Applied.MaxSplatCount}");
        }
    }
}
