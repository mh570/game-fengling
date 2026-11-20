using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>最小透明探针：相机子 quad + 世界 quad，用已知 Blob 材质。</summary>
    public static class TransparentProbeCapture
    {
        private const string PendingKey = "TransparentProbe.Pending";
        private const string StartKey = "TransparentProbe.Start";

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RpgLab.unity");
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
            float now = (float)EditorApplication.timeSinceStartup;
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, now);
                return;
            }
            if (now - start < 6f) return;

            var cam = Camera.main;
            if (cam == null) { EditorApplication.Exit(1); return; }

            var shader = Shader.Find("Fengling/Blob Shadow");
            if (shader == null) { Debug.LogError("[Probe] blob shader missing"); EditorApplication.Exit(1); return; }
            var mat = new Material(shader) { name = "ProbeBlob", color = new Color(0.1f, 0.9f, 0.2f, 0.8f) };

            // 相机子物体 quad
            var child = GameObject.CreatePrimitive(PrimitiveType.Quad);
            child.name = "ProbeCameraQuad";
            Object.DestroyImmediate(child.GetComponent<Collider>());
            child.transform.SetParent(cam.transform, false);
            child.transform.localPosition = new Vector3(0f, 0f, 1.2f);
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            var cr = child.GetComponent<MeshRenderer>();
            cr.sharedMaterial = mat;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cr.receiveShadows = false;

            // 世界空间 quad
            var world = GameObject.CreatePrimitive(PrimitiveType.Quad);
            world.name = "ProbeWorldQuad";
            Object.DestroyImmediate(world.GetComponent<Collider>());
            world.transform.position = cam.transform.position + cam.transform.forward * 3f + cam.transform.right * 1.5f;
            world.transform.rotation = Quaternion.LookRotation(world.transform.position - cam.transform.position, Vector3.up);
            world.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            var wr = world.GetComponent<MeshRenderer>();
            wr.sharedMaterial = mat;
            wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wr.receiveShadows = false;

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
            File.WriteAllBytes("D:/item/game/p9_transparent_probe.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Debug.Log("[Probe] captured camera+world quads");
            EditorApplication.Exit(0);
        }
    }
}
