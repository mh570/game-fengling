using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 渲染路径验证（Play 模式版）。
    ///
    /// 为什么必须进 Play：ParticleSystem 在编辑模式下不参与相机渲染，
    /// 只在 Scene 视图的预览里出现。编辑模式测粒子会得到假阴性。
    ///
    /// 场景三个对照：白立方（不透明）/ 绿透明 Quad / 红色粒子。
    /// 粒子用真实播放（playOnAwake + 持续发射），跑 2 秒后抓帧。
    /// 输出：项目根目录 _probe_play.png
    /// </summary>
    public static class RenderProbeVerifyPlay
    {
        private const string OutputPath = "_probe_play.png";
        private const int Width = 960;
        private const int Height = 540;
        private const float HalfWidth = 3f * Width / Height;
        private const float HalfHeight = 3f;

        private static double _startTime;
        private static bool _sceneReady;

        private const string PendingKey = "ProbePlay.Pending";
        private const string StartKey = "ProbePlay.Start";

        public static void Run()
        {
            Build();

            // 进入 Play 会触发 domain reload：静态字段和 update 订阅都会丢。
            // 所以状态必须放 SessionState，并在 Reload 后由 InitializeOnLoadMethod 重新挂上。
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
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (!EditorApplication.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, (float)now);
                return;
            }

            if (now - start < 2.0) return;

            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            Capture();
        }

        private static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Debug.Log("[ProbePlay] 渲染管线: " + (pipeline != null ? pipeline.GetType().Name : "Built-in (null)"));

            var camGo = new GameObject("ProbeCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = HalfHeight;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.10f, 0.14f, 1f);
            cam.allowHDR = true;

            var lightGo = new GameObject("ProbeSun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = light;
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.4f);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(-2.2f, 0f, 0f);
            cube.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = Vector3.zero;
            quad.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            var quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = new Color(0.1f, 1f, 0.2f, 0.75f),
            };
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 粒子：真实播放
            var psGo = new GameObject("ProbeParticles");
            psGo.transform.position = new Vector3(2.2f, 0f, 0f);
            var ps = psGo.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = 5f;
            main.startSpeed = 0.4f;
            main.startSize = 0.7f;
            main.startColor = new Color(1f, 0.15f, 0.15f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 512;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 80f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.sharedMaterial = new Material(Shader.Find("Particles/Standard Unlit"))
            {
                color = new Color(1f, 0.15f, 0.15f, 1f),
            };

            ps.Play();
        }

        private static void Capture()
        {
            // 不能用 Camera.main：它只认带 "MainCamera" 标签的相机
            var cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null) { Debug.LogError("[ProbePlay] 场景里没有相机"); EditorApplication.Exit(1); return; }

            var ps = Object.FindFirstObjectByType<ParticleSystem>();
            if (ps != null)
            {
                Debug.Log("[ProbePlay] 粒子数: " + ps.particleCount);
            }

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();

            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputPath));
            File.WriteAllBytes(full, tex.EncodeToPNG());

            Vector3Int cube = Sample(tex, -2.2f, 0f, "不透明立方体");
            Vector3Int quad = Sample(tex, 0f, 0f, "透明 Quad");
            Vector3Int particles = Sample(tex, 2.2f, 0f, "粒子");

            bool passed = CompareWithBaseline(cube, quad, particles);

            cam.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            Debug.Log("[ProbePlay] 已保存: " + full);
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(passed ? 0 : 2);
        }

        private const int Tolerance = 5;

        private static string BaselinePath
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_probe_baseline.txt")); }
        }

        /// <summary>
        /// 第一次运行写入基线，之后每次对比。超过容差退出码为 2，供批处理自动判定。
        /// </summary>
        private static bool CompareWithBaseline(Vector3Int cube, Vector3Int quad, Vector3Int particles)
        {
            if (!File.Exists(BaselinePath))
            {
                File.WriteAllText(BaselinePath,
                    cube.x + "," + cube.y + "," + cube.z + "\n" +
                    quad.x + "," + quad.y + "," + quad.z + "\n" +
                    particles.x + "," + particles.y + "," + particles.z + "\n");
                Debug.Log("[ProbePlay] 已写入基线: " + BaselinePath);
                return true;
            }

            string[] lines = File.ReadAllLines(BaselinePath);
            if (lines.Length < 3)
            {
                Debug.LogWarning("[ProbePlay] 基线格式不对，跳过对比");
                return true;
            }

            Vector3Int[] expected = new Vector3Int[3];
            for (int i = 0; i < 3; i++)
            {
                string[] parts = lines[i].Split(',');
                expected[i] = new Vector3Int(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            }

            bool ok = true;
            ok &= Diff("不透明立方体", expected[0], cube);
            ok &= Diff("透明 Quad", expected[1], quad);
            ok &= Diff("粒子", expected[2], particles);

            Debug.Log("[ProbePlay] 基线对比: " + (ok ? "通过" : "偏差超限"));
            return ok;
        }

        private static bool Diff(string label, Vector3Int expected, Vector3Int actual)
        {
            int dr = Mathf.Abs(expected.x - actual.x);
            int dg = Mathf.Abs(expected.y - actual.y);
            int db = Mathf.Abs(expected.z - actual.z);
            bool ok = dr <= Tolerance && dg <= Tolerance && db <= Tolerance;

            Debug.Log(string.Format(
                "[ProbePlay] 对比 {0}: 基线({1},{2},{3}) 实测({4},{5},{6}) 偏差({7},{8},{9}) {10}",
                label, expected.x, expected.y, expected.z,
                actual.x, actual.y, actual.z, dr, dg, db, ok ? "OK" : "FAIL"));

            return ok;
        }

        private static Vector3Int Sample(Texture2D tex, float worldX, float worldY, string label)
        {
            int px = Mathf.RoundToInt((worldX / HalfWidth * 0.5f + 0.5f) * Width);
            int py = Mathf.RoundToInt((worldY / HalfHeight * 0.5f + 0.5f) * Height);

            int sumR = 0, sumG = 0, sumB = 0, count = 0;
            for (int dy = -8; dy <= 8; dy++)
            {
                for (int dx = -8; dx <= 8; dx++)
                {
                    Color c = tex.GetPixel(
                        Mathf.Clamp(px + dx, 0, Width - 1),
                        Mathf.Clamp(py + dy, 0, Height - 1));
                    sumR += Mathf.RoundToInt(c.r * 255f);
                    sumG += Mathf.RoundToInt(c.g * 255f);
                    sumB += Mathf.RoundToInt(c.b * 255f);
                    count++;
                }
            }

            var result = new Vector3Int(sumR / count, sumG / count, sumB / count);
            Debug.Log(string.Format("[ProbePlay] {0}: 平均 RGB = ({1}, {2}, {3})",
                label, result.x, result.y, result.z));

            return result;
        }
    }
}
