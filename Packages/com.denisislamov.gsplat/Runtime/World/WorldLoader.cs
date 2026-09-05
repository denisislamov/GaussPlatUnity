using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace GSplat
{
    /// <summary>
    /// The progressive loading of TZ E8-T2: descriptor -> smallest level shown as soon as it is in -> the best level
    /// the profile allows -> crossfade -> the small one goes away. Owns two child <see cref="GaussianSplatRenderer"/>s
    /// (current and incoming). Keeps the first level's data in memory so a low-memory warning can fall back to it.
    /// </summary>
    [AddComponentMenu("GSplat/World Loader")]
    public sealed class WorldLoader : MonoBehaviour
    {
        [SerializeField, Tooltip("URL of a world descriptor (.json) or directly of a .spz/.ply/.gsplat file. Loaded on Start when 'Load On Start' is set.")]
        private string worldUrl;

        [SerializeField, Tooltip("Axis convention when the URL points straight at a splat file (descriptors carry their own).")]
        private SplatCoordinateSystem singleFileCoordinateSystem = SplatCoordinateSystem.Rub;

        [SerializeField] private bool loadOnStart = true;

        [SerializeField, Tooltip("Leave 'Use Device Profile' on to pick Mobile/Desktop automatically; turn it off to use the profile below.")]
        private bool useDeviceProfile = true;

        [SerializeField] private SplatQualityProfile profile = SplatQualityProfile.Desktop();

        [SerializeField, Tooltip("Network retries per file before giving up.")]
        [Range(0, 5)] private int retries = 2;

        private GaussianSplatRenderer current;
        private GaussianSplatRenderer incoming;
        private GsplatData firstLevelData;
        private CancellationTokenSource cancellation;
        private WorldLoadState state = WorldLoadState.Idle;

        public WorldLoadState State => state;
        public WorldDescriptor Descriptor { get; private set; }
        public SplatQualityProfile ActiveProfile { get; private set; }
        public SplatLoadStatus LastStatus { get; private set; }
        public SplatLoadError LastError { get; private set; }
        public string LastErrorMessage { get; private set; }
        public WorldLevel CurrentLevel { get; private set; }

        /// <summary>The renderer that currently shows the world (null before the first level arrives).</summary>
        public GaussianSplatRenderer CurrentRenderer => current;

        public event Action<WorldLoadState> StateChanged;
        public event Action<SplatLoadStatus> StatusChanged;

        /// <summary>Raised when the descriptor names a collider GLB; the optional glTFast module listens and builds MeshColliders under the given parent.</summary>
        public event Action<string, Transform> ColliderRequested;

        private void Start()
        {
            if (loadOnStart && !string.IsNullOrEmpty(worldUrl)) _ = LoadAsync(worldUrl);
        }

        private void OnEnable()
        {
            Application.lowMemory += OnLowMemory;
        }

        private void OnDisable()
        {
            Application.lowMemory -= OnLowMemory;
        }

        private void OnDestroy()
        {
            Cancel();
            firstLevelData?.Dispose();
            firstLevelData = null;
        }

        /// <summary>Starts loading a world (descriptor JSON or a splat file URL), cancelling any load in progress.</summary>
        public async Awaitable LoadAsync(string url)
        {
            Cancel();
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            ActiveProfile = useDeviceProfile ? SplatQualityProfile.ForThisDevice() : profile.Clone();
            if (ActiveProfile.TargetFrameRate > 0) Application.targetFrameRate = ActiveProfile.TargetFrameRate;

            try
            {
                SetState(WorldLoadState.LoadingDescriptor);
                Descriptor = await LoadDescriptorAsync(url, token);
                if (Descriptor.HasCollider) ColliderRequested?.Invoke(Descriptor.colliderUrl, transform);

                WorldLevel first = Descriptor.FirstLevel(ActiveProfile);
                WorldLevel final = Descriptor.FinalLevel(ActiveProfile);

                SetState(WorldLoadState.LoadingFirstLevel);
                GsplatData firstData = await LoadLevelWithRetriesAsync(first, token);
                ReplaceCurrent(firstData, first, keepDataForFallback: true);
                SetState(WorldLoadState.ShowingFirstLevel);

                if (final == first || !SplatMemoryBudget.CanAfford(final.splatCount, ActiveProfile.ShDegree))
                {
                    SetState(WorldLoadState.Ready);
                    return;
                }

                SetState(WorldLoadState.LoadingFinalLevel);
                GsplatData finalData = await LoadLevelWithRetriesAsync(final, token);

                SetState(WorldLoadState.Crossfading);
                await CrossfadeToAsync(finalData, final, token);
                SetState(WorldLoadState.Ready);
            }
            catch (OperationCanceledException)
            {
                // A newer LoadAsync took over, or the component is going away; it sets the next state itself.
            }
            catch (SplatLoadException e)
            {
                Fail(e.Code, e.Message);
            }
            catch (WorldDescriptorException e)
            {
                Fail(SplatLoadError.UnsupportedFormat, e.Message);
            }
        }

        /// <summary>Called from the web page (index.html sends ?world=... through SendMessage) and by deep links.</summary>
        public void LoadFromPage(string url)
        {
            worldUrl = url;
            _ = LoadAsync(url);
        }

        /// <summary>Web page hidden: stop rendering to save battery and avoid GPU context churn.</summary>
        public void PauseFromPage(string unused)
        {
            Application.targetFrameRate = 1;
        }

        public void ResumeFromPage(string unused)
        {
            Application.targetFrameRate = ActiveProfile != null && ActiveProfile.TargetFrameRate > 0 ? ActiveProfile.TargetFrameRate : -1;
        }

        public void Cancel()
        {
            if (cancellation == null) return;
            cancellation.Cancel();
            cancellation.Dispose();
            cancellation = null;
            if (incoming != null)
            {
                Destroy(incoming.gameObject);
                incoming = null;
            }
        }

        private async Awaitable<WorldDescriptor> LoadDescriptorAsync(string url, CancellationToken token)
        {
            if (!url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return WorldDescriptor.ForSingleFile(url, singleFileCoordinateSystem);
            }

            using (var request = UnityWebRequest.Get(url))
            {
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Awaitable.NextFrameAsync(token);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    SplatLoadError code = request.responseCode == 404 ? SplatLoadError.NotFound : SplatLoadError.Network;
                    throw new SplatLoadException(code, $"Could not download the world descriptor {url}: {request.error}");
                }

                return WorldDescriptor.Parse(request.downloadHandler.text);
            }
        }

        private async Awaitable<GsplatData> LoadLevelWithRetriesAsync(WorldLevel level, CancellationToken token)
        {
            var options = new SplatImportOptions
            {
                SourceCoordinateSystem = Descriptor.CoordinateSystem,
                TargetShDegree = ActiveProfile.ShDegree,
                MaxSplatCount = ActiveProfile.MaxSplatCount
            };
            var progress = new Progress<SplatLoadStatus>(OnStatus);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await SplatLoader.LoadAsync(level.url, options, progress, token);
                }
                catch (SplatLoadException e) when (e.Code == SplatLoadError.Network && attempt < retries)
                {
                    // Transient network trouble: back off 1 s, 2 s, ... and try again (TZ E8-T2).
                    await Awaitable.WaitForSecondsAsync(1f * (attempt + 1), token);
                }
            }
        }

        private void ReplaceCurrent(GsplatData data, WorldLevel level, bool keepDataForFallback)
        {
            if (current != null) Destroy(current.gameObject);
            current = CreateRenderer("Splats " + level.splatCount, data, ownsData: !keepDataForFallback);
            current.Opacity = 1f;
            CurrentLevel = level;
            if (keepDataForFallback)
            {
                firstLevelData?.Dispose();
                firstLevelData = data;
            }
        }

        private async Awaitable CrossfadeToAsync(GsplatData data, WorldLevel level, CancellationToken token)
        {
            incoming = CreateRenderer("Splats " + level.splatCount, data, ownsData: true);
            incoming.Opacity = 0f;

            // Wait for the upload so the fade does not start on a half-uploaded scene.
            while (incoming != null && incoming.Gpu != null && !incoming.Gpu.IsFullyUploaded)
            {
                await Awaitable.NextFrameAsync(token);
            }

            // Both scenes are drawn during the fade: twice the overdraw for a few seconds. Acceptable for MVP (TZ E8-T2);
            // TODO: a dithered swap would avoid the double cost on weak phones.
            float duration = Mathf.Max(0.01f, ActiveProfile.CrossfadeSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (incoming != null) incoming.Opacity = t;
                if (current != null) current.Opacity = 1f - t;
                await Awaitable.NextFrameAsync(token);
            }

            if (current != null) Destroy(current.gameObject);
            current = incoming;
            incoming = null;
            current.Opacity = 1f;
            CurrentLevel = level;
        }

        private GaussianSplatRenderer CreateRenderer(string objectName, GsplatData data, bool ownsData)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var renderer = child.AddComponent<GaussianSplatRenderer>();
            renderer.MaxStdDev = ActiveProfile.MaxStdDev;
            renderer.ShDegree = ActiveProfile.ShDegree;
            renderer.SetData(data, ownsData);
            return renderer;
        }

        /// <summary>Low memory: go back to the small level if we still have it; a smaller world beats a killed app (TZ E8-T3).</summary>
        private void OnLowMemory()
        {
            if (firstLevelData == null || current == null || current.Data == firstLevelData) return;

            Debug.LogWarning("GSplat: low memory warning, falling back to the first level.");
            Cancel();
            ReplaceCurrent(firstLevelData, Descriptor.FirstLevel(ActiveProfile), keepDataForFallback: true);
            OnStatus(new SplatLoadStatus(SplatLoadStage.Ready, 1f, "Quality reduced: low memory"));
            SetState(WorldLoadState.Ready);
        }

        private void OnStatus(SplatLoadStatus status)
        {
            LastStatus = status;
            StatusChanged?.Invoke(status);
        }

        private void SetState(WorldLoadState newState)
        {
            state = newState;
            StateChanged?.Invoke(newState);
        }

        private void Fail(SplatLoadError code, string message)
        {
            LastError = code;
            LastErrorMessage = message;
            Debug.LogError("GSplat: " + message, this);
            SetState(WorldLoadState.Failed);
        }
    }
}
