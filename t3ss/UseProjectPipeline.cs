using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace T3ssDemo
{
    /// <summary>把渲染管线设为工程自带的 URP Asset（整合工程配置，不换回）。</summary>
    public static class UseProjectPipeline
    {
        [MenuItem("T3SS/用工程管线")]
        public static void Run()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                "Assets/Settings/ProjectOriginal/New Universal Render Pipeline Asset.asset");
            Debug.Log("[UP] 工程 URP Asset=" + (urp != null));
            if (urp == null) { EditorApplication.Exit(1); return; }

            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
            AssetDatabase.SaveAssets();

            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var rp = so.FindProperty("m_CustomRenderPipeline");
            Debug.Log("[UP] 当前管线 = " + (rp != null && rp.objectReferenceValue != null ? rp.objectReferenceValue.name : "null"));
            Debug.Log("[UP] 完成");
            EditorApplication.Exit(0);
        }
    }
}