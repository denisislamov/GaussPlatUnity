using UnityEditor;
using UnityEngine;

namespace GSplat.Editor
{
    /// <summary>Read-only summary of an imported splat asset. Import settings live on the importer (select the source file).</summary>
    [CustomEditor(typeof(GaussianSplatAsset))]
    public sealed class GaussianSplatAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (GaussianSplatAsset)target;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Source", asset.SourceFileName);
                EditorGUILayout.LabelField("Splats", $"{asset.SplatCount:N0} (source {asset.SourceSplatCount:N0})");
                EditorGUILayout.LabelField("Chunks", asset.ChunkCount.ToString());
                EditorGUILayout.LabelField("SH degree", asset.ShDegree.ToString());
                EditorGUILayout.LabelField("Antialiased", asset.Antialiased ? "yes" : "no");
                EditorGUILayout.LabelField("Bounds", $"{asset.Bounds.size.x:F1} x {asset.Bounds.size.y:F1} x {asset.Bounds.size.z:F1} m");
                EditorGUILayout.LabelField("Data size", EditorUtility.FormatBytes(asset.FileSizeBytes));
            }

            EditorGUILayout.HelpBox("Import settings (axes, SH degree, pruning, splat budget) are on the source .spz/.ply file.", MessageType.None);
        }
    }
}
