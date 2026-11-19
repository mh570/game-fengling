using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 给新管线准备程序化天空盒材质，并写进 RenderSettings。
    /// 独立小文件，不碰 ScenePresets 那种大文件。
    /// </summary>
    public static class SkyboxSetup
    {
        private const string MatPath = "Assets/Game/Rendering/Materials/FenglingSky.mat";

        [MenuItem("Fengling/Setup/Apply Procedural Skybox", false, 40)]
        public static void Apply()
        {
            var shader = Shader.Find("Fengling/Skybox");
            if (shader == null)
            {
                Debug.LogError("[Skybox] 找不到 shader 'Fengling/Skybox'");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MatPath) ?? "Assets");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "FenglingSky" };
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            else
            {
                mat.shader = shader;
            }

            // 太阳方向与场景光源一致，避免天空太阳和实际光照方向对不上
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                var d = -sun.transform.forward;
                mat.SetVector("_SunDirection", new Vector4(d.x, d.y, d.z, 0f));
            }

            RenderSettings.skybox = mat;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.2f;

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            Debug.Log("[Skybox] 已应用程序化天空盒: " + MatPath);
        }
    }
}
