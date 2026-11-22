using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace T3ssDemo
{
    /// <summary>
    /// 把工程的法线描边 Pass 装到 URP Renderer 上（Unity 6 兼容版）。
    /// </summary>
    public static class InstallNormalLinePass
    {
        const string RendererPath = "Assets/Settings/ProjectOriginal/New Universal Render Pipeline Asset_Renderer.asset";
        const string URPAssetPath = "Assets/Settings/ProjectOriginal/New Universal Render Pipeline Asset.asset";

        [MenuItem("T3SS/安装法线描边")]
        public static void Run()
        {
            // ① 材质（用 HLSL 重写版）
            var normTexShader = Shader.Find("Custom/NormalTex");
            var normLineShader = Shader.Find("Custom/NormalLine");
            Debug.Log("[NL] NormalTex=" + (normTexShader != null) + " NormalLine=" + (normLineShader != null));
            if (normTexShader == null || normLineShader == null) { EditorApplication.Exit(1); return; }

            Directory.CreateDirectory("Assets/Materials");
            var normalTexMat = new Material(normTexShader) { name = "NormalTexMat" };
            AssetDatabase.DeleteAsset("Assets/Materials/NormalTexMat.mat");
            AssetDatabase.CreateAsset(normalTexMat, "Assets/Materials/NormalTexMat.mat");

            var normalLineMat = new Material(normLineShader) { name = "NormalLineMat" };
            normalLineMat.SetFloat("_Edge", 0.3f);
            normalLineMat.SetColor("_LineColor", new Color(0.05f, 0.08f, 0.05f, 1f));
            normalLineMat.SetFloat("_Width", 1f);
            AssetDatabase.DeleteAsset("Assets/Materials/NormalLineMat.mat");
            AssetDatabase.CreateAsset(normalLineMat, "Assets/Materials/NormalLineMat.mat");

            // ② 挂到 Renderer
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            Debug.Log("[NL] rendererData=" + (rendererData != null));
            if (rendererData == null) { EditorApplication.Exit(1); return; }

            // 删除已有同名 feature
            foreach (var f in rendererData.rendererFeatures)
                if (f != null && f.name == "NormalLine") AssetDatabase.RemoveObjectFromAsset(f);

            var feature = ScriptableObject.CreateInstance<NoramlLineRenderPassFeature>();
            feature.name = "NormalLine";
            feature.setting.normalTexMat = normalTexMat;
            feature.setting.normalLineMat = normalLineMat;
            feature.setting.layer = ~0;
            feature.setting.Edge = 0.3f;
            feature.setting.passEvent = RenderPassEvent.AfterRenderingPrePasses;

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            EditorUtility.SetDirty(rendererData);

            // SerializedObject 写入 m_RendererFeatures
            var so = new SerializedObject(rendererData);
            var arr = so.FindProperty("m_RendererFeatures");
            if (arr == null) { Debug.LogError("[NL] 找不到 m_RendererFeatures"); EditorApplication.Exit(1); return; }
            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = feature;
            var map = so.FindProperty("m_RendererFeatureMap");
            if (map != null) { map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = 0; }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            // ③ 确认 URP Asset 用这个 renderer
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(URPAssetPath);
            Debug.Log("[NL] URP Asset=" + (urp != null));
            if (urp != null)
            {
                Debug.Log("[NL] 当前 renderer 列表:");
                // 读取 m_RendererDataList 检查
                var so2 = new SerializedObject(urp);
                var list = so2.FindProperty("m_RendererDataList");
                if (list != null)
                {
                    Debug.Log("[NL] m_RendererDataList size=" + list.arraySize);
                    for (int i = 0; i < list.arraySize; i++)
                        Debug.Log("[NL]   [" + i + "] = " + (list.GetArrayElementAtIndex(i).objectReferenceValue != null
                            ? list.GetArrayElementAtIndex(i).objectReferenceValue.name : "null"));
                }
                // 设为项目当前管线
                GraphicsSettings.defaultRenderPipeline = urp;
                QualitySettings.renderPipeline = urp;
                Debug.Log("[NL] 已设为当前渲染管线");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[NL] 安装完成");
            EditorApplication.Exit(0);
        }
    }
}
