using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 最小复现场景：正交俯视 + 水面 + Cube 地台。
    /// 目的不是好看，是确认“正交俯视下水面到底有没有被画出来”。
    /// </summary>
    public static class P2MinimalScene
    {
        private const string ScenePath = "Assets/Scenes/P2Minimal.unity";
        private const string PendingKey = "P2.Minimal.Pending";
        private const string StartKey = "P2.Minimal.Start";

        [MenuItem("Fengling/Debug/Build P2 Minimal Scene", false, 90)]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var sun = RenderSettings.sun;
            if (sun == null)
            {
                var sunGo = new GameObject("Directional Light");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.intensity = 9f;
            sun.color = new Color(1f, 0.93f, 0.80f);
            sun.transform.rotation = Quaternion.Euler(42f, -138f, 0f);
            RenderSettings.sun = sun;
            SkyboxSetup.Apply();

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            var top = cam.gameObject.AddComponent<Player.TopDownCamera>();
            top.pitch = 55f;
            top.yaw = 45f;
            top.orthoSize = 25f;
            top.focus = Vector3.zero;
            top.enabled = true;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -3f, 0f);
            ground.transform.localScale = new Vector3(80f, 4f, 80f);
            ground.GetComponent<Renderer>().sharedMaterial =
                LitMaterialProvider.Make("P2MinimalGround", new Color(0.66f, 0.60f, 0.46f), 0.05f);

            var island = GameObject.CreatePrimitive(PrimitiveType.Cube);
            island.name = "Island";
            island.transform.position = new Vector3(0f, -0.2f, 0f);
            island.transform.localScale = new Vector3(20f, 1.4f, 20f);
            island.GetComponent<Renderer>().sharedMaterial =
                LitMaterialProvider.Make("P2MinimalIsland", new Color(0.72f, 0.66f, 0.50f), 0.05f);

            var player = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 1.5f, 0f);
            player.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
            var cap = player.GetComponent<CapsuleCollider>();
            if (cap != null) Object.DestroyImmediate(cap);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.45f;
            player.AddComponent<Player.PlayerMotor>();
            player.GetComponent<Renderer>().sharedMaterial =
                LitMaterialProvider.Make("PlayerBody", new Color(0.72f, 0.32f, 0.24f), 0.2f);
            top.followTarget = player.transform;
            top.followOffset = Vector3.zero;

            var waterGo = new GameObject("Water");
            var wm = waterGo.AddComponent<Water.WaterModule>();
            wm.Settings.kind = Water.WaterKind.Flat;
            wm.Settings.size = new Vector2(200f, 200f);
            wm.Settings.baseLevel = 0f;

            var surfaceGo = new GameObject("WaterSurface");
            surfaceGo.AddComponent<MeshFilter>();
            surfaceGo.AddComponent<MeshRenderer>();
            var sm = surfaceGo.AddComponent<Water.WaterSurfaceRenderer>();
            sm.water = wm;
            sm.waterMaterialTemplate = WaterMaterialProvider.GetOrCreate();
            sm.buildMesh = true;
            sm.segments = 64;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Marker";
            marker.transform.position = new Vector3(0f, 3f, 0f);
            marker.transform.localScale = new Vector3(2f, 2f, 2f);
            marker.GetComponent<Renderer>().sharedMaterial =
                LitMaterialProvider.Make("P2MinimalMarker", new Color(1f, 0.05f, 0.02f), 0.2f);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[P2Minimal] built " + ScenePath);
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
            if ((float)EditorApplication.timeSinceStartup - start < 8f) return;

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;

            var cam = Camera.main;
            if (cam == null) { EditorApplication.Exit(1); return; }
            var top = cam.GetComponent<Player.TopDownCamera>();
            if (top != null)
            {
                top.enabled = true;
                top.pitch = 55f;
                top.yaw = 45f;
                top.orthoSize = 25f;
            }
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;

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
            File.WriteAllBytes("D:/item/game/p2c_minimal.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log("[P2Minimal] captured");
            EditorApplication.Exit(0);
        }
    }
}
