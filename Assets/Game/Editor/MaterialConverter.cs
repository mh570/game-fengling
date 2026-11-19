using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 把工程里的 HDRP/Lit 材质换成自建管线的 Fengling/Lit。
    /// 切管线前必须跑一次，否则所有物体在新管线下是粉的。
    /// </summary>
    public static class MaterialConverter
    {
        private static readonly string[] SourceShaders =
        {
            "HDRP/Lit",
            "HDRP/Unlit",
            "Universal Render Pipeline/Lit",
            "Standard",
        };

        [MenuItem("Fengling/Setup/Convert Materials to Fengling Lit", false, 41)]
        public static void ConvertAll()
        {
            var target = Shader.Find("Fengling/Lit");
            if (target == null)
            {
                Debug.LogError("[Convert] 找不到 Fengling/Lit");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            int converted = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                bool needs = false;
                foreach (var name in SourceShaders)
                    if (mat.shader != null && mat.shader.name == name) { needs = true; break; }

                if (!needs) continue;

                Color baseColor = Color.white;
                if (mat.HasProperty("_BaseColor")) baseColor = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) baseColor = mat.GetColor("_Color");

                Texture baseMap = null;
                if (mat.HasProperty("_BaseColorMap")) baseMap = mat.GetTexture("_BaseColorMap");
                else if (mat.HasProperty("_MainTex")) baseMap = mat.GetTexture("_MainTex");

                float smoothness = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.15f;

                mat.shader = target;
                mat.SetColor("_BaseColor", baseColor);
                if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
                mat.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));

                EditorUtility.SetDirty(mat);
                converted++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Convert] 转换了 {converted} 个材质 -> Fengling/Lit");
        }
    }
}
