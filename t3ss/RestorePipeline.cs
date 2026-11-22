using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace T3ssDemo
{
    public static class RestorePipeline
    {
        [MenuItem("T3SS/恢复原管线")]
        public static void Run()
        {
            // 恢复成我们工程原本的 PC_RPAsset
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Debug.Log("[RP] PC_RPAsset=" + (pc != null));
            if (pc != null)
            {
                GraphicsSettings.defaultRenderPipeline = pc;
                QualitySettings.renderPipeline = pc;
                Debug.Log("[RP] 已恢复 PC_RPAsset");
            }
            // 顺便关掉工程的 Volume 影响：删掉场景里的 Global Volume（新场景每次重建，无需处理）

            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var rp = so.FindProperty("m_CustomRenderPipeline");
            Debug.Log("[RP] 当前 m_CustomRenderPipeline = " + (rp != null && rp.objectReferenceValue != null ? rp.objectReferenceValue.name : "null"));
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
    }
}