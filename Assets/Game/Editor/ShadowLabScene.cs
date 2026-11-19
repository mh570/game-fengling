using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 最小阴影测试场景：Cube + 地面 + 方向光。
    /// 不接水、不接反射，专门隔离 Shadow pass / render state。
    /// </summary>
    public static class ShadowLabScene
    {
        private const string ScenePath = "Assets/Scenes/ShadowLab.unity";
        private const string PendingKey = "ShadowLab.Pending";
        private const string StartKey = "ShadowLab.Start";

        [MenuItem("Fengling/Debug/Build Shadow Lab", false, 91)]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(7f, 5f, -7f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, 0f) - cam.transform.position);
            cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.Skybox;

            var sun = RenderSettings.sun;
            if (sun == null)
            {
                var sunGo = new GameObject("Directional Light");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.type = LightType.Directional;
            sun.intensity = 9f;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.sun = sun;
            SkyboxSetup.Apply();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            ground.GetComponent<Renderer>().sharedMaterial =
                LitMaterialProvider.Make("ShadowLabGround", new Color(0.66f, 0.60f, 0.46f), 0.05f);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ShadowCube";
            cube.transform.position = new Vector3(0f, 1.5f, 0f);
            cube.transform.localScale = new Vector3(2f, 2f, 2f);
            cube.GetComponent<Renderer>().sharedMaterial =
                LitMaterialProvider.Make("ShadowLabCube", new Color(0.85f, 0.15f, 0.10f), 0.1f);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[ShadowLab] built " + ScenePath);
        }

        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Reattach()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                return;
            }
            if ((float)EditorApplication.timeSinceStartup - start < 5f) return;

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;

            var cam = Camera.main;
            if (cam == null) { EditorApplication.Exit(1); return; }
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            Directory.CreateDirectory("D:/item/game");
            File.WriteAllBytes("D:/item/game/shadow_lab.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log("[ShadowLab] captured");
            EditorApplication.Exit(0);
        }
    }
}
