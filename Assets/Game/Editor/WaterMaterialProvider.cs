using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 提供一份引用 Fengling/Water Surface 的材质资产。
    ///
    /// 为什么需要它：
    ///   WaterSurfaceRenderer 原来在运行时 Shader.Find + new Material，
    ///   没有任何资产引用这个 shader，Player 构建时它可能被剥离，水面直接消失。
    ///   落成 .mat 后，shader 会被资产引用，构建时保留。
    /// </summary>
    public static class WaterMaterialProvider
    {
        private const string Dir = "Assets/Game/Water/Materials";
        private const string Path = Dir + "/WaterSurface.mat";

        public static Material GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(Path);
            if (existing != null)
            {
                if (existing.shader == null)
                    existing.shader = Shader.Find("Fengling/Water Surface");
                return existing;
            }

            var shader = Shader.Find("Fengling/Water Surface");
            if (shader == null)
            {
                Debug.LogError("[WaterMaterial] 找不到 'Fengling/Water Surface'");
                return null;
            }

            Directory.CreateDirectory(Dir);
            var mat = new Material(shader) { name = "WaterSurface" };
            AssetDatabase.CreateAsset(mat, Path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
