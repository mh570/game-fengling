using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>Phase 1：从 P2Minimal 复制出 RpgLab，换成透视 RTS 相机。</summary>
    public static class RpgLabScene
    {
        private const string SourcePath = "Assets/Scenes/P2Minimal.unity";
        private const string ScenePath = "Assets/Scenes/RpgLab.unity";
        private const string PendingKey = "RpgLab.Pending";
        private const string StartKey = "RpgLab.Start";
        private const string StageKey = "RpgLab.Stage";
        private const string StageStartKey = "RpgLab.StageStart";

        [MenuItem("Fengling/Debug/Build RpgLab Camera Scene", false, 92)]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(SourcePath);
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[RpgLab] no Main Camera");
                return;
            }

            // 移除旧的第一人称/正交俯视组件，只保留新 RTS 相机。
            foreach (var mb in cam.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == "TopDownCamera" || n == "FirstPersonCamera" || n == "CameraModeSwitch")
                    Object.DestroyImmediate(mb);
            }

            var player = GameObject.Find("Player");
            var terrain = Object.FindFirstObjectByType<Environment.CoastTerrain>();
            if (player == null || terrain == null)
            {
                Debug.LogError("[RpgLab] missing Player/CoastTerrain");
                return;
            }

            var rts = cam.GetComponent<Player.RtsCameraController>();
            if (rts == null) rts = cam.gameObject.AddComponent<Player.RtsCameraController>();
            rts.focusTarget = player.transform;
            rts.startFocused = true;
            rts.terrain = terrain;
            rts.boundsHalf = new Vector2(terrain.size.x * 0.5f - 5f, terrain.size.y * 0.5f - 5f);
            rts.yaw = 45f;
            rts.pitch = 55f;
            rts.distance = 35f;
            rts.minDistance = 15f;
            rts.maxDistance = 120f;
            rts.edgeScroll = false;

            cam.orthographic = false;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;

            // Phase 2：时间核心 + 太阳/月亮/天空。
            var timeGo = GameObject.Find("TimeOfDay");
            if (timeGo == null) timeGo = new GameObject("TimeOfDay");
            var tod = timeGo.GetComponent<Fengling.Time.TimeOfDaySystem>();
            if (tod == null) tod = timeGo.AddComponent<Fengling.Time.TimeOfDaySystem>();
            tod.sun = RenderSettings.sun;
            tod.skyMaterial = RenderSettings.skybox;
            tod.timeOfDay = 8f;
            tod.gameMinutesPerSecond = 1f;
            tod.showHud = true;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
                list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[RpgLab] built " + ScenePath);
        }

        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetFloat(StageStartKey, 0f);
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
            float now = (float)EditorApplication.timeSinceStartup;
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, now);
                return;
            }
            if (now - start < 8f) return;

            var cam = Camera.main;
            if (cam == null) { EditorApplication.Exit(1); return; }
            var rts = cam.GetComponent<Player.RtsCameraController>();
            int stage = SessionState.GetInt(StageKey, 0);
            float stageStart = SessionState.GetFloat(StageStartKey, 0f);

            if (stage == 0)
            {
                Render(cam, "D:/item/game/p9_camera_default.png");
                if (rts != null)
                {
                    rts.distance = 18f;
                    rts.FocusOnPlayer();
                }
                SessionState.SetInt(StageKey, 1);
                SessionState.SetFloat(StageStartKey, now);
                return;
            }

            if (stage == 1)
            {
                if (now - stageStart < 1.5f) return;
                Render(cam, "D:/item/game/p9_camera_near.png");
                if (rts != null)
                {
                    rts.distance = 90f;
                    rts.FocusOnPlayer();
                }
                SessionState.SetInt(StageKey, 2);
                SessionState.SetFloat(StageStartKey, now);
                return;
            }

            if (stage == 2)
            {
                if (now - stageStart < 1.5f) return;
                Render(cam, "D:/item/game/p9_camera_far.png");
                if (rts != null)
                {
                    rts.yaw = 135f;
                    rts.distance = 35f;
                    rts.FocusOnPlayer();
                }
                SessionState.SetInt(StageKey, 3);
                SessionState.SetFloat(StageStartKey, now);
                return;
            }

            if (stage == 3)
            {
                if (now - stageStart < 1.5f) return;
                Render(cam, "D:/item/game/p9_camera_rotated.png");
                SessionState.SetBool(PendingKey, false);
                EditorApplication.update -= Tick;
                Debug.Log("[RpgLab] captured variants");
                EditorApplication.Exit(0);
            }
        }

        private static void Render(Camera cam, string path)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "D:/item/game");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
