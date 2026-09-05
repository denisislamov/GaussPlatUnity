using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GSplat.Editor
{
    /// <summary>
    /// Adds <see cref="GaussianSplatRendererFeature"/> to every renderer of the active URP asset, the way the
    /// "Add Renderer Feature" button does (the feature is a sub-asset of the renderer data).
    /// </summary>
    public static class UrpSetup
    {
        [MenuItem("GSplat/Setup/Add Renderer Feature to URP Renderers")]
        public static void AddRendererFeatureToActivePipeline()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
            {
                EditorUtility.DisplayDialog("GSplat", "The project's default render pipeline is not URP. Set a Universal Render Pipeline asset in Graphics settings first.", "OK");
                return;
            }

            int added = 0;
            foreach (ScriptableRendererData rendererData in RendererDataOf(pipeline))
            {
                if (AddFeature(rendererData)) added++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"GSplat: renderer feature added to {added} renderer(s) of '{pipeline.name}'.");
        }

        /// <summary>Renderer data assets of a pipeline asset; the list is internal to URP, so it is read through serialization.</summary>
        public static List<ScriptableRendererData> RendererDataOf(UniversalRenderPipelineAsset pipeline)
        {
            var result = new List<ScriptableRendererData>();
            var serialized = new SerializedObject(pipeline);
            SerializedProperty list = serialized.FindProperty("m_RendererDataList");
            for (int index = 0; index < list.arraySize; index++)
            {
                var rendererData = list.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
                if (rendererData != null) result.Add(rendererData);
            }

            return result;
        }

        /// <summary>Adds the feature unless the renderer already has one. Returns true when something changed.</summary>
        public static bool AddFeature(ScriptableRendererData rendererData)
        {
            foreach (ScriptableRendererFeature existing in rendererData.rendererFeatures)
            {
                if (existing is GaussianSplatRendererFeature) return false;
            }

            var feature = ScriptableObject.CreateInstance<GaussianSplatRendererFeature>();
            feature.name = "Gaussian Splats";
            AssetDatabase.AddObjectToAsset(feature, rendererData);

            // URP keeps a parallel list of local file ids so it can survive missing scripts; both must be updated.
            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serialized.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            return true;
        }
    }
}
