using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Fengling.Rendering;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 管线切换：在自建 Fengling 管线和原来的 HDRP 之间来回切。
    /// 切之前务必确认材质已经换成 Fengling/Lit，否则 HDRP/Lit 的物体会消失。
    /// </summary>
    public static class PipelineSwitcher
    {
        private const string FenglingAssetPath = "Assets/Game/Rendering/FenglingPipeline.asset";

        [MenuItem("Fengling/Setup/Use Fengling Pipeline", false, 50)]
        public static void UseFengling()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FenglingPipelineAsset>(FenglingAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<FenglingPipelineAsset>();
                AssetDatabase.CreateAsset(asset, FenglingAssetPath);
                AssetDatabase.SaveAssets();
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = null;
            AssetDatabase.SaveAssets();

            Debug.Log("[Pipeline] 已切换到自建 Fengling 管线");
        }

        [MenuItem("Fengling/Setup/Use HDRP Pipeline", false, 51)]
        public static void UseHdrp()
        {
            var guids = AssetDatabase.FindAssets("t:RenderPipelineAsset", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
                if (asset == null) continue;
                if (asset.GetType().Name.Contains("HDRenderPipeline"))
                {
                    GraphicsSettings.defaultRenderPipeline = asset;
                    QualitySettings.renderPipeline = null;
                    AssetDatabase.SaveAssets();
                    Debug.Log("[Pipeline] 已切回 HDRP: " + path);
                    return;
                }
            }

            // 工程里没有 HDRP 资产时，指向包内默认资产
            Debug.LogWarning("[Pipeline] 没有找到 HDRP 管线资产，请在 Project Settings → Graphics 手动指定");
        }
    }
}
