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
            List<(GaussianSplatAsset asset, string file)> levels = CollectLevels(worldFolder, out SplatCoordinateSystem coordinateSystem, out string collider);
            if (levels.Count == 0) return null;

            float unitsToMeters = ReadUnitsToMeters(worldFolder, name);
            string json = BuildDescriptorJson(name, coordinateSystem, unitsToMeters, levels, collider);

            string descriptorPath = Path.Combine(worldFolder, name + ".world.json");
            File.WriteAllText(descriptorPath, json);
            return descriptorPath;
        }

        /// <summary>Imported splat assets in the folder, smallest first, plus the collider GLB and the axis convention the importer used.</summary>
        private static List<(GaussianSplatAsset asset, string file)> CollectLevels(string worldFolder, out SplatCoordinateSystem coordinateSystem, out string collider)
        {
            var levels = new List<(GaussianSplatAsset asset, string file)>();
            coordinateSystem = SplatCoordinateSystem.Rub;
            collider = null;

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

            levels.Sort((a, b) => a.asset.SplatCount.CompareTo(b.asset.SplatCount));
            return levels;
        }

        /// <summary>InnerTest semantics_metadata (saved next to the export) carries the scene units to meters factor; 1 when absent.</summary>
        private static float ReadUnitsToMeters(string worldFolder, string name)
        {
            string semanticsPath = Path.Combine(worldFolder, name + ".semantics.json");
            if (!File.Exists(semanticsPath)) return 1f;

            var semantics = JsonUtility.FromJson<InnerTestSemantics>(File.ReadAllText(semanticsPath));
            return semantics != null && semantics.metric_scale_factor > 0f ? semantics.metric_scale_factor : 1f;
        }

        /// <summary>Hand-written JSON so the file stays readable and the key order stable (JsonUtility cannot write nested arrays of objects).</summary>
        private static string BuildDescriptorJson(string name, SplatCoordinateSystem coordinateSystem, float unitsToMeters, List<(GaussianSplatAsset asset, string file)> levels, string collider)
        {
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
            return json.ToString();
        }
    }
}
