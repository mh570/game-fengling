using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 渲染路径验证：一个场景里同时放三样东西，看自建管线到底画出了哪些。
    ///
    ///   1. 不透明立方体（白）   —— 对照组，必须出现
    ///   2. 透明 Quad（绿 75%）  —— 测透明队列
    ///   3. 粒子（红）           —— 测 ParticleSystemRenderer
    ///
    /// 输出：项目根目录 _probe.png，同时打印三块区域的采样色。
    /// 新建空场景是为了排除 RpgLab 里雾/云/后处理的干扰。
    /// </summary>
    public static class RenderProbeVerify
    {
        private const string OutputPath = "_probe.png";
        private const int Width = 960;
        private const int Height = 540;

        // 世界坐标 -> 屏幕像素（正交，size=3，宽高比 960/540）
        private const float HalfWidth = 3f * Width / Height;   // 5.333
        private const float HalfHeight = 3f;

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Debug.Log("[Probe] 渲染管线: " + (pipeline != null ? pipeline.GetType().Name : "Built-in (null)"));

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

            // 1. 不透明对照组
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ProbeOpaqueCube";
            cube.transform.position = new Vector3(-2.2f, 0f, 0f);
            cube.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);

            // 2. 透明 Quad
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ProbeTransparentQuad";
            quad.transform.position = Vector3.zero;
            quad.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            var quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = new Color(0.1f, 1f, 0.2f, 0.75f),
            };
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3. 粒子
            var psGo = new GameObject("ProbeParticles");
            psGo.transform.position = new Vector3(2.2f, 0f, 0f);
            var ps = psGo.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 999f;
            main.startSpeed = 0f;
            main.startSize = 0.5f;
            main.startColor = new Color(1f, 0.15f, 0.15f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            ps.Emit(120);
            ps.Simulate(0.05f, true, true);

            var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.sharedMaterial = new Material(Shader.Find("Particles/Standard Unlit"))
            {
                color = new Color(1f, 0.15f, 0.15f, 1f),
            };

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();

            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputPath));
            File.WriteAllBytes(full, tex.EncodeToPNG());

            // 三块区域采样（世界中心点 -> 像素坐标）
            Report(cam, tex, -2.2f, 0f, "不透明立方体");
            Report(cam, tex, 0f, 0f, "透明 Quad");
            Report(cam, tex, 2.2f, 0f, "粒子");

            cam.targetTexture = previous;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            Debug.Log("[Probe] 已保存: " + full);
            EditorApplication.Exit(0);
        }

        private static void Report(Camera cam, Texture2D tex, float worldX, float worldY, string label)
        {
            // 像素坐标（左下原点）
            int px = Mathf.RoundToInt((worldX / HalfWidth * 0.5f + 0.5f) * Width);
            int py = Mathf.RoundToInt((worldY / HalfHeight * 0.5f + 0.5f) * Height);

            int sumR = 0, sumG = 0, sumB = 0, count = 0;
            for (int dy = -6; dy <= 6; dy++)
            {
                for (int dx = -6; dx <= 6; dx++)
                {
                    int x = Mathf.Clamp(px + dx, 0, Width - 1);
                    int y = Mathf.Clamp(py + dy, 0, Height - 1);
                    Color c = tex.GetPixel(x, y);
                    sumR += Mathf.RoundToInt(c.r * 255f);
                    sumG += Mathf.RoundToInt(c.g * 255f);
                    sumB += Mathf.RoundToInt(c.b * 255f);
                    count++;
                }
            }

            Debug.Log(string.Format("[Probe] {0}: 平均 RGB = ({1}, {2}, {3})   像素({4},{5})",
                label, sumR / count, sumG / count, sumB / count, px, py));
        }
    }
}
