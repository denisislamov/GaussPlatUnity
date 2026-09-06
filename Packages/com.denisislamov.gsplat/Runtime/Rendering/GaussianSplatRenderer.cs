using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Places a splat scene in the world. Holds the GPU data, the sorter and the per-camera state; the URP feature
    /// (<see cref="GaussianSplatRendererFeature"/>) asks every enabled renderer for a <see cref="SplatDrawItem"/>
    /// each frame. Data comes from an asset, or from code via <see cref="SetData"/> (runtime loading).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("GSplat/Gaussian Splat Renderer")]
    [DisallowMultipleComponent]
    public sealed class GaussianSplatRenderer : MonoBehaviour
    {
        /// <summary>sqrt(8): the classic 3DGS cutoff. Spark recommends sqrt(5) for VR / weak GPUs.</summary>
        public const float DefaultMaxStdDev = 2.8284271f;

        private static readonly List<GaussianSplatRenderer> ActiveRenderers = new List<GaussianSplatRenderer>();
        private static readonly int SplatsId = Shader.PropertyToID("_Splats");
        private static readonly int OrderId = Shader.PropertyToID("_Order");
        private static readonly int ShId = Shader.PropertyToID("_Sh");
        private static readonly int ChunkRangesId = Shader.PropertyToID("_ChunkRanges");
        private static readonly int MaxStdDevId = Shader.PropertyToID("_MaxStdDev");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int ShDegreeId = Shader.PropertyToID("_ShDegree");
        private static readonly int ShTexelsPerSplatId = Shader.PropertyToID("_ShTexelsPerSplat");
        private static readonly int AntialiasedId = Shader.PropertyToID("_Antialiased");
        private static readonly int SrgbInputId = Shader.PropertyToID("_SrgbInput");
        private static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");
        private static readonly int MinPixelRadiusId = Shader.PropertyToID("_MinPixelRadius");
        private static readonly int DilationId = Shader.PropertyToID("_Dilation");
        private static readonly int MaxPixelRadiusId = Shader.PropertyToID("_MaxPixelRadius");

        [SerializeField, Tooltip("Imported .spz/.ply. Leave empty when the data is set from code (SetData).")]
        private GaussianSplatAsset asset;

        [Header("Quality")]
        [SerializeField, Range(1f, 4f), Tooltip("Quad reach in standard deviations. sqrt(8) = 2.83 is the 3DGS default; sqrt(5) = 2.24 is visually the same and cheaper on phones.")]
        private float maxStdDev = DefaultMaxStdDev;

        [SerializeField, Range(0, ShMath.MaxDegree), Tooltip("View-dependent color detail. Capped by what the data contains; 0 on mobile.")]
        private int shDegree = ShMath.MaxDegree;

        [SerializeField, Range(0f, 3f), Tooltip("Splats whose own radius (largest axis, before the 0.3 px anti-aliasing dilation) projects below this many pixels are skipped. 0 draws everything. Hundreds of thousands of sub-pixel quads are what makes tile-based GPUs slow, so 0.5 is a good default; raise it on weak phones.")]
        private float minPixelRadius = 0.5f;

        [SerializeField, Min(0f), Tooltip("Splats that would reach further than this many pixels are shrunk to it. Phone captures have huge background splats that otherwise cover the whole screen when the camera is inside the scene. 512 is Spark's default; 0 = no limit.")]
        private float maxPixelRadius = 512f;

        [SerializeField, Range(0f, 1f), Tooltip("Low-pass filter added to every splat's screen covariance (pixels squared). 0 matches the Spark web viewer and is the default; 0.3 is what the original 3DGS rasterizer uses and softens thin splats at the cost of a slight haze. Mip-splatting scenes compensate the opacity automatically.")]
        private float dilation = 0f;

        [Header("Look")]
        [SerializeField, Range(0f, 2f)] private float brightness = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("Whole-scene opacity; the loader animates it for crossfades.")]
        private float opacity = 1f;
        [SerializeField, Tooltip("Splat colors are sRGB (trained on photos). In a linear project they must be converted before blending. Turn off only for the A/B comparison of TZ E3-T5.")]
        private bool convertSrgbToLinear = true;

        [Header("Engine")]
        [SerializeField] private SplatSorterKind sorterKind = SplatSorterKind.Auto;
        [SerializeField, Tooltip("Sort by distance to the camera (what Spark does) instead of by view depth (classic 3DGS). Matches Spark's output; see ADR-003.")]
        private bool sortRadial = true;
        [SerializeField, Min(1), Tooltip("Chunks (65k splats, 1 MB) uploaded per frame. Higher = faster appearance, longer frame.")]
        private int uploadChunksPerFrame = 2;
        [SerializeField] private SplatDebugMode debugMode = SplatDebugMode.None;

        private GsplatData data;
        private bool ownsData;
        private SplatGpuData gpu;
        private ISplatSorter sorter;
        private readonly Dictionary<Camera, SplatCameraState> cameraStates = new Dictionary<Camera, SplatCameraState>();
        private int lastVisibleChunkCount;
        private MaterialPropertyBlock properties;
        private readonly List<int> visibleScratch = new List<int>();

        /// <summary>Renderers that are enabled and have data; the render feature draws these.</summary>
        public static IReadOnlyList<GaussianSplatRenderer> Active => ActiveRenderers;

        public GaussianSplatAsset Asset => asset;
        public GsplatData Data => data;
        public SplatGpuData Gpu => gpu;
        public ISplatSorter Sorter => sorter;
        public bool HasData => data != null && gpu != null;
        public int SplatCount => data != null ? data.SplatCount : 0;

        /// <summary>
        /// Splats drawn for the last prepared camera. Exact with the CPU sorter; with the GPU sorter it is the number of
        /// candidates (visible chunks), the exact count stays on the GPU in the indirect arguments.
        /// </summary>
        public int LastDrawnSplatCount { get; private set; }
        public int LastVisibleChunkCount => lastVisibleChunkCount;

        public float MaxStdDev { get => maxStdDev; set => maxStdDev = Mathf.Clamp(value, 1f, 4f); }
        public int ShDegree { get => shDegree; set => shDegree = Mathf.Clamp(value, 0, ShMath.MaxDegree); }
        public float MinPixelRadius { get => minPixelRadius; set => minPixelRadius = Mathf.Max(0f, value); }
        public float Dilation { get => dilation; set => dilation = Mathf.Clamp(value, 0f, 1f); }
        public float MaxPixelRadius { get => maxPixelRadius; set => maxPixelRadius = Mathf.Max(0f, value); }
        public float Brightness { get => brightness; set => brightness = Mathf.Max(0f, value); }
        public float Opacity { get => opacity; set => opacity = Mathf.Clamp01(value); }
        public bool ConvertSrgbToLinear { get => convertSrgbToLinear; set => convertSrgbToLinear = value; }
        public SplatSorterKind SorterKind => sorterKind;
        public bool SortRadial { get => sortRadial; set => sortRadial = value; }

        /// <summary>Switches the sorter at runtime (quality fallback, tests). Rebuilds it if the data is already on the GPU.</summary>
        public void SetSorterKind(SplatSorterKind kind)
        {
            sorterKind = kind;
            if (gpu == null) return;

            sorter?.Dispose();
            sorter = CreateSorter();
            foreach (SplatCameraState state in cameraStates.Values) state.Policy.Reset();
        }
        public SplatDebugMode DebugMode { get => debugMode; set => debugMode = value; }
        public int UploadChunksPerFrame { get => uploadChunksPerFrame; set => uploadChunksPerFrame = Mathf.Max(1, value); }

        /// <summary>World-space bounds of the scene (object bounds transformed), for culling and camera framing.</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (gpu == null) return new Bounds(transform.position, Vector3.zero);
                var local = new SplatChunkInfo(0, gpu.LocalBounds.min, gpu.LocalBounds.max);
                return ChunkCulling.TransformBounds(transform.localToWorldMatrix, local);
            }
        }

        /// <summary>
        /// Gives the renderer data built at runtime (from <see cref="SplatLoader"/>). With <paramref name="takeOwnership"/>
        /// the renderer disposes it when the data is replaced or the component is destroyed. Replaces any asset data.
        /// </summary>
        public void SetData(GsplatData newData, bool takeOwnership)
        {
            ReleaseResources();
            data = newData;
            ownsData = takeOwnership;
            if (isActiveAndEnabled && data != null) CreateResources();
        }

        /// <summary>Drops the current data (and disposes it when owned).</summary>
        public void ClearData()
        {
            ReleaseResources();
            data = null;
        }

        private void OnEnable()
        {
            if (data == null && asset != null)
            {
                data = asset.LoadData();
                ownsData = true;
            }

            if (data != null) CreateResources();
        }

        private void OnDisable()
        {
            ReleaseResources();
            if (asset != null && ownsData && data != null)
            {
                // Asset data is reloaded on enable; keeping it while disabled would only hold memory.
                data.Dispose();
                data = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseResources();
            if (ownsData && data != null) data.Dispose();
            data = null;
        }

        private void OnValidate()
        {
            maxStdDev = Mathf.Clamp(maxStdDev, 1f, 4f);
            shDegree = Mathf.Clamp(shDegree, 0, ShMath.MaxDegree);
            uploadChunksPerFrame = Mathf.Max(1, uploadChunksPerFrame);
            if (!Application.isPlaying && isActiveAndEnabled && gpu != null && asset != null && asset.SplatCount != data?.SplatCount)
            {
                // The asset was re-imported under us: reload.
                OnDisable();
                OnEnable();
            }
        }

        private void Update()
        {
            if (gpu == null || gpu.IsFullyUploaded) return;
            // TODO: once the upload is done and the sorter is the GPU one, the CPU copy of the packed data is dead weight
            // (8 MB per 500k). Free it when nothing else (debug tools, a CPU-sort fallback) needs it.
            for (int upload = 0; upload < uploadChunksPerFrame && !gpu.IsFullyUploaded; upload++)
            {
                gpu.UploadNextChunk();
            }
        }

        private void CreateResources()
        {
            gpu = new SplatGpuData(data);
            sorter = CreateSorter();
            properties = new MaterialPropertyBlock();
            if (!ActiveRenderers.Contains(this)) ActiveRenderers.Add(this);
        }

        private ISplatSorter CreateSorter()
        {
            bool wantGpu = sorterKind == SplatSorterKind.Gpu || (sorterKind == SplatSorterKind.Auto && GpuCountingSorter.IsSupported);
            if (wantGpu)
            {
                ComputeShader shader = GpuCountingSorter.LoadShader();
                if (shader != null) return new GpuCountingSorter(shader, data.SplatCount);
                if (sorterKind == SplatSorterKind.Gpu) Debug.LogWarning("GSplat: compute shaders are unavailable here; sorting on the CPU instead.", this);
            }

            return new CpuCountingSorter(data.SplatCount);
        }

        private void ReleaseResources()
        {
            ActiveRenderers.Remove(this);
            sorter?.Dispose();
            sorter = null;
            gpu?.Dispose();
            gpu = null;
            foreach (SplatCameraState state in cameraStates.Values) state.Dispose();
            cameraStates.Clear();
            lastVisibleChunkCount = 0;
        }

        private SplatCameraState StateFor(Camera camera)
        {
            if (cameraStates.TryGetValue(camera, out SplatCameraState state)) return state;

            // Cameras come and go (Scene View, previews); drop states of destroyed ones so the dictionary stays small.
            if (cameraStates.Count > 8)
            {
                var dead = new List<Camera>();
                foreach (Camera key in cameraStates.Keys)
                {
                    if (key == null) dead.Add(key);
                }

                foreach (Camera key in dead)
                {
                    cameraStates[key].Dispose();
                    cameraStates.Remove(key);
                }
            }

            state = new SplatCameraState(data.ChunkCount);
            cameraStates[camera] = state;
            return state;
        }

        /// <summary>
        /// Main-thread work for one camera: cull chunks, decide whether to re-sort, run the sorter's main-thread step,
        /// fill the material properties. Returns false when nothing of this renderer is visible.
        /// </summary>
        public bool TryPrepare(Camera camera, out SplatDrawItem item)
        {
            item = default;
            if (camera == null || gpu == null || gpu.UploadedChunkCount == 0) return false;

            SplatCameraState state = StateFor(camera);
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            int visibleSplats = CollectVisibleChunks(camera, localToWorld, state, out int visibleHash);
            if (state.VisibleChunkCount == 0)
            {
                LastDrawnSplatCount = 0;
                return false;
            }

            SplatSortInput input = BuildSortInput(camera, localToWorld, state, visibleSplats);

            // The GPU sorter re-sorts for every camera that draws (its order texture is shared), so with two cameras
            // it works every frame; the policy only saves work while a single camera stands still. Turning can be
            // ignored only when the order is radial AND the key pass does not cull by view (culling is view-dependent).
            double now = Time.realtimeSinceStartupAsDouble;
            bool ignoreRotation = input.View.Radial && !input.View.CullInKeys;
            bool sharedOrderTexture = sorter.DrawArgs != null && cameraStates.Count > 1;
            bool resort = sharedOrderTexture || state.Policy.ShouldResort(input.View.PositionLocal, input.View.ForwardLocal, visibleHash, now, ignoreRotation);
            sorter.Sort(input, resort);
            if (resort) state.Policy.MarkSorted(input.View.PositionLocal, input.View.ForwardLocal, visibleHash, now);

            if (sorter.OrderedSplatCount == 0) return false; // CPU sorter: first result not in yet

            FillProperties();
            LastDrawnSplatCount = sorter.OrderedSplatCount;
            item = new SplatDrawItem
            {
                Sorter = sorter,
                LocalToWorld = localToWorld,
                Properties = properties,
                InstanceCount = sorter.OrderedSplatCount,
                DistanceToCamera = Vector3.Distance(camera.transform.position, WorldBounds.center)
            };
            return true;
        }

        /// <summary>
        /// Frustum-culls the chunks for this camera into <paramref name="state"/> and uploads the list when it changed.
        /// Returns the number of splats in the visible chunks; <paramref name="visibleHash"/> identifies the set.
        /// </summary>
        private int CollectVisibleChunks(Camera camera, Matrix4x4 localToWorld, SplatCameraState state, out int visibleHash)
        {
            visibleScratch.Clear();
            ChunkCulling.CollectVisible(camera, localToWorld, data.Chunks, visibleScratch);

            // Chunks still on their way to the GPU are not drawn yet: that is the progressive appearance of E2-T2.
            state.VisibleChunkCount = 0;
            int visibleSplats = 0;
            int hash = gpu.UploadedChunkCount;
            for (int listIndex = 0; listIndex < visibleScratch.Count; listIndex++)
            {
                int chunkIndex = visibleScratch[listIndex];
                if (chunkIndex >= gpu.UploadedChunkCount) continue;
                state.VisibleChunks[state.VisibleChunkCount++] = chunkIndex;
                visibleSplats += data.Chunks[chunkIndex].SplatCount;
                hash = hash * 31 + chunkIndex;
            }

            lastVisibleChunkCount = state.VisibleChunkCount;
            if (state.VisibleChunkCount > 0 && hash != state.UploadedVisibleHash)
            {
                state.VisibleChunkBuffer.SetData(state.VisibleChunks, 0, 0, state.VisibleChunkCount);
                state.UploadedVisibleHash = hash;
            }

            visibleHash = hash;
            return visibleSplats;
        }

        /// <summary>The camera in the object's local space plus everything the key pass needs, see <see cref="SplatCameraView"/>.</summary>
        private SplatSortInput BuildSortInput(Camera camera, Matrix4x4 localToWorld, SplatCameraState state, int visibleSplats)
        {
            NativeArray<int> visible = state.VisibleChunks.GetSubArray(0, state.VisibleChunkCount);

            // The forward vector is transformed with the transpose (not the inverse) so that dot(localPosition, forward)
            // stays proportional to the world-space view depth under non-uniform scale:
            // depth = dot(M p + t - c, f) = dot(p, M^T f) + const.
            float3 cameraPositionLocal = transform.InverseTransformPoint(camera.transform.position);
            float3 cameraForwardLocal = math.normalizesafe((float3)localToWorld.transpose.MultiplyVector(camera.transform.forward), new float3(0f, 0f, 1f));
            SplatSortKeys.DepthRange(data.Chunks, visible, cameraPositionLocal, cameraForwardLocal, sortRadial, out float minDepth, out float maxDepth);

            // Per-splat culling in the key pass needs the projection; it assumes a perspective camera (w = depth).
            Matrix4x4 projection = camera.projectionMatrix;
            var view = new SplatCameraView
            {
                PositionLocal = cameraPositionLocal,
                ForwardLocal = cameraForwardLocal,
                Radial = sortRadial,
                MinDepth = minDepth,
                MaxDepth = maxDepth,
                CullInKeys = !camera.orthographic,
                LocalToClip = projection * camera.worldToCameraMatrix * localToWorld,
                FocalPixelsY = Mathf.Abs(projection.m11) * camera.pixelHeight * 0.5f,
                ScreenSize = new float2(camera.pixelWidth, camera.pixelHeight),
                MaxStdDev = maxStdDev,
                MinPixelRadius = minPixelRadius
            };

            return new SplatSortInput
            {
                Data = data,
                Gpu = gpu,
                VisibleChunks = visible,
                VisibleChunkBuffer = state.VisibleChunkBuffer,
                VisibleSplatCount = visibleSplats,
                View = view
            };
        }

        private void FillProperties()
        {
            properties.SetTexture(SplatsId, gpu.SplatTexture);
            properties.SetTexture(OrderId, sorter.OrderTexture);
            properties.SetTexture(ChunkRangesId, gpu.ChunkRangeTexture);
            int effectiveShDegree = gpu.ShTexture != null ? math.min(shDegree, gpu.ShDegree) : 0;
            properties.SetTexture(ShId, gpu.ShTexture != null ? gpu.ShTexture : Texture2D.blackTexture);
            properties.SetInt(ShDegreeId, effectiveShDegree);
            properties.SetInt(ShTexelsPerSplatId, gpu.ShTexelsPerSplat);
            properties.SetFloat(MaxStdDevId, maxStdDev);
            properties.SetFloat(OpacityId, opacity);
            properties.SetFloat(BrightnessId, brightness);
            properties.SetInt(AntialiasedId, gpu.Antialiased ? 1 : 0);
            properties.SetInt(SrgbInputId, convertSrgbToLinear && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            properties.SetInt(DebugModeId, (int)debugMode);
            properties.SetFloat(MinPixelRadiusId, minPixelRadius);
            properties.SetFloat(DilationId, dilation);
            properties.SetFloat(MaxPixelRadiusId, maxPixelRadius);
        }

        private void OnDrawGizmosSelected()
        {
            if (gpu == null) return;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(gpu.LocalBounds.center, gpu.LocalBounds.size);
        }
    }
}
