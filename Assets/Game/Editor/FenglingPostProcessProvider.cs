using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 创建后处理材质资产，并把它挂到 FenglingPipeline.asset 上。
    /// 材质资产引用 shader，才能保证 Player 构建时 shader 不被剥离。
    /// </summary>
    public static class FenglingPostProcessProvider
    {
        private const string MaterialDir = "Assets/Game/Rendering/Materials";
        private const string MaterialPath = MaterialDir + "/PostProcess.mat";
        private const string PipelinePath = "Assets/Game/Rendering/FenglingPipeline.asset";

        public static Material GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
            {
                if (existing.shader == null)
                    existing.shader = Shader.Find("Fengling/PostProcess");
                return existing;
            }

            var shader = Shader.Find("Fengling/PostProcess");
            if (shader == null)
            {
                Debug.LogError("[PostProcess] 找不到 'Fengling/PostProcess'");
                return null;
            }

            Directory.CreateDirectory(MaterialDir);
            var mat = new Material(shader) { name = "PostProcess" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        [MenuItem("Fengling/Setup/Apply Post Process", false, 43)]
        public static void Apply()
        {
            var mat = GetOrCreate();
            var asset = AssetDatabase.LoadAssetAtPath<Fengling.Rendering.FenglingPipelineAsset>(PipelinePath);
            if (asset == null)
            {
                Debug.LogError("[PostProcess] 找不到管线资产: " + PipelinePath);
                return;
            }
            if (mat == null) return;

            var so = new SerializedObject(asset);
            var prop = so.FindProperty("postProcessMaterial");
            if (prop == null)
            {
                Debug.LogError("[PostProcess] 管线资产缺少 postProcessMaterial 字段");
                return;
            }

            prop.objectReferenceValue = mat;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[PostProcess] 已挂载材质: " + MaterialPath);
        }
    }
}
