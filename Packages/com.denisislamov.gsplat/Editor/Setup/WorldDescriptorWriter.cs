using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GSplat.Editor
{
    /// <summary>
    /// Turns a folder of imported splat levels (e.g. a InnerTest export: name_100k.spz, name_500k.spz, name_full_res.spz
    /// and name_collider.glb) into a world descriptor JSON the WorldLoader can open with a file:// URL. Splat counts
    /// and SH degrees come from the imported assets, so the descriptor matches what the importer kept.
    /// </summary>
    public static class WorldDescriptorWriter
    {
        [Serializable]
        private sealed class InnerTestSemantics
        {
            public float metric_scale_factor = 1f;
            public float ground_plane_offset;
        }

        private const string InnerTestFolder = "Assets/Samples/InnerTest";

        [MenuItem("GSplat/Setup/Write World Descriptors for InnerTest Samples")]
        public static void WriteInnerTestDescriptors()
        {
            if (!Directory.Exists(InnerTestFolder))
            {
                Debug.LogWarning("GSplat: no " + InnerTestFolder + " folder.");
                return;
            }

            int written = 0;
            foreach (string worldFolder in Directory.GetDirectories(InnerTestFolder))
            {
                string path = WriteDescriptor(worldFolder);
                if (path != null)
                {
                    written++;
                    Debug.Log("GSplat: wrote " + path);
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"GSplat: {written} world descriptor(s) written.");
        }

        /// <summary>Writes &lt;folder&gt;/&lt;name&gt;.world.json; returns its path, or null when the folder has no imported splat assets.</summary>
        public static string WriteDescriptor(string worldFolder)
        {
            string name = Path.GetFileName(worldFolder);
            var levels = new List<(GaussianSplatAsset asset, string file)>();
            SplatCoordinateSystem coordinateSystem = SplatCoordinateSystem.Rub;
            string collider = null;

            foreach (string file in Directory.GetFiles(worldFolder))
            {
                string assetPath = file.Replace('\\', '/');
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".glb")
                {
                    collider = assetPath;
                    continue;
                }

                if (extension != ".spz" && extension != ".ply") continue;

                var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(assetPath);
                if (asset == null) continue;
                levels.Add((asset, assetPath));
                if (AssetImporter.GetAtPath(assetPath) is SplatImporterBase importer) coordinateSystem = importer.Options.SourceCoordinateSystem;
            }

            if (levels.Count == 0) return null;
            levels.Sort((a, b) => a.asset.SplatCount.CompareTo(b.asset.SplatCount));

            // InnerTest semantics_metadata (saved next to the export): scene units -> meters.
            float unitsToMeters = 1f;
            string semanticsPath = Path.Combine(worldFolder, name + ".semantics.json");
            if (File.Exists(semanticsPath))
            {
                var semantics = JsonUtility.FromJson<InnerTestSemantics>(File.ReadAllText(semanticsPath));
                if (semantics != null && semantics.metric_scale_factor > 0f) unitsToMeters = semantics.metric_scale_factor;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"name\": \"{name}\",\n");
            json.Append($"  \"coordinateSystem\": \"{coordinateSystem}\",\n");
            json.Append(string.Format(CultureInfo.InvariantCulture, "  \"unitsToMeters\": {0},\n", unitsToMeters));
            json.Append("  \"levels\": [\n");
            for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
            {
                (GaussianSplatAsset asset, string file) = levels[levelIndex];
                string url = new Uri(Path.Combine(projectRoot, file)).AbsoluteUri;
                long bytes = new FileInfo(file).Length;
                json.Append(string.Format(CultureInfo.InvariantCulture,
                    "    {{ \"url\": \"{0}\", \"splatCount\": {1}, \"bytes\": {2}, \"shDegree\": {3} }}{4}\n",
                    url, asset.SourceSplatCount, bytes, asset.ShDegree, levelIndex < levels.Count - 1 ? "," : ""));
            }

            json.Append("  ],\n");
            if (collider != null) json.Append($"  \"colliderUrl\": \"{new Uri(Path.Combine(projectRoot, collider)).AbsoluteUri}\",\n");
            // InnerTest generates the world around the origin, which is the natural viewpoint.
            json.Append("  \"spawn\": { \"position\": [0, 0, 0], \"rotationEuler\": [0, 0, 0] }\n");
            json.Append("}\n");

            string descriptorPath = Path.Combine(worldFolder, name + ".world.json");
            File.WriteAllText(descriptorPath, json.ToString());
            return descriptorPath;
        }
    }
}
