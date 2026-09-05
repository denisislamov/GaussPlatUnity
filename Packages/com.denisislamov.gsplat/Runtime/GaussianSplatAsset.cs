using System;
using Unity.Collections;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// An imported splat scene: the .gsplat bytes plus the numbers the inspector shows. Produced by the SPZ/PLY
    /// importers, consumed by the renderer through <see cref="LoadData"/>.
    /// </summary>
    public sealed class GaussianSplatAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private byte[] fileBytes;
        [SerializeField, HideInInspector] private int splatCount;
        [SerializeField, HideInInspector] private int chunkCount;
        [SerializeField, HideInInspector] private int shDegree;
        [SerializeField, HideInInspector] private bool antialiased;
        [SerializeField, HideInInspector] private Bounds bounds;
        [SerializeField, HideInInspector] private string sourceFileName;
        [SerializeField, HideInInspector] private int sourceSplatCount;

        public int SplatCount => splatCount;
        public int ChunkCount => chunkCount;
        public int ShDegree => shDegree;
        public bool Antialiased => antialiased;
        public Bounds Bounds => bounds;
        public string SourceFileName => sourceFileName;

        /// <summary>Splats in the source file before pruning and the budget cut.</summary>
        public int SourceSplatCount => sourceSplatCount;

        public int FileSizeBytes => fileBytes != null ? fileBytes.Length : 0;

        /// <summary>Editor/importer use: fills the asset from freshly built data.</summary>
        public void Initialize(GsplatData data, string sourceName, int sourceCount)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            fileBytes = GsplatFile.Serialize(data);
            splatCount = data.SplatCount;
            chunkCount = data.ChunkCount;
            shDegree = data.ShDegree;
            antialiased = data.Antialiased;
            bounds = new Bounds();
            bounds.SetMinMax(data.BoundsMin, data.BoundsMax);
            sourceFileName = sourceName;
            sourceSplatCount = sourceCount;
        }

        /// <summary>Deserializes into native memory. The caller owns (disposes) the result.</summary>
        public GsplatData LoadData(Allocator allocator = Allocator.Persistent)
        {
            if (fileBytes == null || fileBytes.Length == 0)
            {
                throw new InvalidOperationException($"GaussianSplatAsset '{name}' holds no data; re-import the source file.");
            }

            return GsplatFile.Deserialize(fileBytes, allocator);
        }
    }
}
