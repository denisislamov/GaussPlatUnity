using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace GSplat.Editor
{
    /// <summary>
    /// Shared body of the SPZ and PLY importers: read the file, decode it with the format-specific reader,
    /// build the GPU layout, store it in a <see cref="GaussianSplatAsset"/>. Subclasses only pick the decoder
    /// and the default coordinate system (SPZ files assume RUB, 3DGS PLY files are RDF).
    /// </summary>
    public abstract class SplatImporterBase : ScriptedImporter
    {
        [SerializeField] private SplatImportOptions options;

        public SplatImportOptions Options => options;

        protected abstract SplatCoordinateSystem DefaultCoordinateSystem { get; }

        protected abstract SplatCloud Decode(byte[] bytes);

        public override void OnImportAsset(AssetImportContext context)
        {
            if (options == null)
            {
                options = new SplatImportOptions { SourceCoordinateSystem = DefaultCoordinateSystem };
            }

            byte[] bytes = File.ReadAllBytes(context.assetPath);
            SplatCloud cloud;
            try
            {
                cloud = Decode(bytes);
            }
            catch (Exception e) when (e is SpzException || e is PlyException)
            {
                context.LogImportError($"Could not import {Path.GetFileName(context.assetPath)}: {e.Message}");
                return;
            }

            GaussianSplatAsset asset;
            try
            {
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                {
                    asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
                    asset.Initialize(data, Path.GetFileName(context.assetPath), cloud.Count);
                }
            }
            finally
            {
                cloud.Dispose();
            }

            asset.name = Path.GetFileNameWithoutExtension(context.assetPath);
            context.AddObjectToAsset("main", asset);
            context.SetMainObject(asset);
        }
    }
}
