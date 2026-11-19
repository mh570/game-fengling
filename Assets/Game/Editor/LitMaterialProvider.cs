using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 提供一份可靠的 Fengling/Lit 基础材质资产。
    ///
    /// 为什么不用 Shader.Find：
    ///   批处理模式（-batchmode）下 Shader.Find 对没被任何资产引用的 shader 可能返回 null，
    ///   拿到空 shader 的材质就是粉色。改成先落一个材质资产，再克隆它，永远有效。
    /// </summary>
    public static class LitMaterialProvider
    {
        private const string BaseMaterialPath = "Assets/Game/Rendering/Materials/FenglingLit.mat";

        public static Material GetBase()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            if (mat != null) return mat;

            var shader = Shader.Find("Fengling/Lit");
            if (shader == null)
            {
                Debug.LogError("[Lit] 找不到 Fengling/Lit shader");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BaseMaterialPath) ?? "Assets");
            mat = new Material(shader) { name = "FenglingLit" };
            AssetDatabase.CreateAsset(mat, BaseMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// 生成（或复用）一个材质资产。
        ///
        /// 必须落成资产：运行时的内存材质写进场景后引用的是一份不可靠的临时对象，
        /// 实测会导致道具全粉。落成 .mat 后场景引用的是稳定 GUID。
        /// </summary>
        public static Material Make(string name, Color color, float smoothness)
        {
            var baseMat = GetBase();
            if (baseMat == null) return null;

            var dir = Path.GetDirectoryName(BaseMaterialPath) ?? "Assets";
            var path = dir + "/" + name + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = baseMat.shader;
                existing.SetColor("_BaseColor", color);
                existing.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var mat = new Material(baseMat) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
