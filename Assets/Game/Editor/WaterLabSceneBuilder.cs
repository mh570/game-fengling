using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 一键生成水测试场景。
    ///
    /// 手搭要拖 8 个组件、设十几个参数，每次都容易漏。
    /// 用脚本生成，保证每次都是一致的起点，也方便回滚重来。
    ///
    /// 菜单：Fengling / Build Water Lab Scene
    /// </summary>
    public static partial class WaterLabSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/WaterLab.unity";
        private const string SandMatPath = "Assets/Game/Environment/Terrain/CoastSand.mat";
        private const string HeightMapPath = "Assets/Game/Environment/Terrain/CoastHeightMap.asset";
        private const string NormalMap1Path = "Assets/Game/Water/Textures/WaterNormal1.asset";
        private const string NormalMap2Path = "Assets/Game/Water/Textures/WaterNormal2.asset";

        [MenuItem("Fengling/Build Water Lab Scene")]
        public static void Build()
        {
            // 1. 新场景（自带主相机 + 平行光）
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 2. 海岸地形
            var terrainGo = new GameObject("CoastTerrain");
            terrainGo.AddComponent<MeshFilter>();
            var terrainRenderer = terrainGo.AddComponent<MeshRenderer>();
            var terrain = terrainGo.AddComponent<Environment.CoastTerrain>();

            terrainRenderer.sharedMaterial = GetOrCreateSandMaterial();
            terrain.Generate();

            // 3. 把地形高度烘焙成纹理（水面用它算水深和泡沫）
            var heightMap = BakeHeightMap(terrain);

            // 3.5 天空 + 曝光 + 色调映射 + 太阳
            //     没有这一步，水面反射是黑的、颜色是未映射的线性值 —— 画面必然又暗又灰
            SetupSkyAndSun();
            PropSpawner.SpawnAll(null, terrain);

            // 4. 水模块
            var waterGo = new GameObject("Water");
            var waterModule = waterGo.AddComponent<Water.WaterModule>();
            waterModule.Settings.size = new Vector2(1000f, 1000f); // 让海平线远离 200m 地形边缘
            waterModule.Settings.amplitudeScale = 1.5f;   // 参考图的浪比较明显
            waterGo.AddComponent<Water.WaterFieldGizmo>();
            waterGo.AddComponent<Water.WaterLabHUD>();

            // 5. 水面渲染（网格 + 材质 + 每帧喂参数）
            var surfaceGo = new GameObject("WaterSurface");
            surfaceGo.AddComponent<MeshFilter>();
            surfaceGo.AddComponent<MeshRenderer>();
            var surfaceRenderer = surfaceGo.AddComponent<Water.WaterSurfaceRenderer>();
            surfaceRenderer.water = waterModule;
            surfaceRenderer.waterMaterialTemplate = WaterMaterialProvider.GetOrCreate();
            surfaceRenderer.terrainHeightMap = heightMap;
            surfaceRenderer.heightMapBounds = new Vector4(
                terrain.transform.position.x,
                terrain.transform.position.z,
                terrain.size.x,
                terrain.size.y);
            surfaceRenderer.heightRange = terrain.HeightMapRange;
            surfaceRenderer.normalMap1 = GetOrCreateNormalMap(NormalMap1Path, 1337, 5);
            surfaceRenderer.normalMap2 = GetOrCreateNormalMap(NormalMap2Path, 4242, 9);

            // 6. 相机：正交俯视（地图视角，没有角色）
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.SetParent(null, false);
            var topDown = cam.gameObject.GetComponent<Player.TopDownCamera>();
            if (topDown == null) topDown = cam.gameObject.AddComponent<Player.TopDownCamera>();
            topDown.pitch = 45f;
            topDown.yaw = 0f;
            topDown.orthoSize = 42f;
            topDown.focus = new Vector3(0f, 0f, terrain.shoreZ + 14f);
            topDown.clampFocus = false;

            // player capsule for third-person mode
            var playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerGo.name = "Player";
            playerGo.transform.position = new Vector3(0f, 2f, terrain.shoreZ + 12f);
            var capsuleCollider = playerGo.GetComponent<CapsuleCollider>();
            if (capsuleCollider != null) Object.DestroyImmediate(capsuleCollider);
            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.5f;
            playerGo.AddComponent<Player.PlayerMotor>();
            playerGo.AddComponent<Player.PlayerBuoyancy>();
            playerGo.GetComponent<Renderer>().sharedMaterial =
                LitMaterialProvider.Make("PlayerBody", new Color(0.72f, 0.32f, 0.24f), 0.2f);

            var fps = cam.gameObject.AddComponent<Player.FirstPersonCamera>();
            fps.target = playerGo.transform;
            fps.eyeHeight = 1.62f;
            fps.enabled = false;

            var switcher = cam.gameObject.AddComponent<Player.CameraModeSwitch>();
            switcher.firstPerson = fps;
            switcher.topDown = topDown;

            // 8. 保存
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // 9. 加进 Build Settings
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath))
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[Fengling] 水测试场景已生成：{ScenePath}\n" +
                      "按 Play。快捷键：2=Gerstner  1=Flat  [ ]=振幅  P=定格  F1=隐藏面板");
        }

        // ---------------- 资源生成 ----------------

        /// <summary>
        /// 天空 + 曝光 + 色调映射 + 太阳。
        ///
        /// 为什么必须有：HDRP 是线性 HDR 管线，没有天空就没有环境反射，
        /// 水面采样到的是黑的；没有 Exposure/Tonemapping，颜色是未映射的原始值。
        /// 这两样缺任何一个，水都会"又暗又灰"—— 而它们跟水面 shader 一点关系都没有。
        /// </summary>
        private static void SetupSkyAndSun()
        {
            // ---- 太阳：低角度逆光，水面才能拉出长条碎光
            var sunGo = GameObject.Find("Directional Light");
            Light sun = null;
            if (sunGo != null) sun = sunGo.GetComponent<Light>();
            if (sun == null)
            {
                sunGo = new GameObject("Directional Light");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.color = new Color(1f, 0.93f, 0.80f);
            sun.intensity = 9f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(42f, -138f, 0f);
            RenderSettings.sun = sun;

            // 自建管线不读 HDRP Volume。直接用程序化天空盒 + Skybox 环境光，
            // 曝光/ACES/Bloom 由 FenglingPipeline 的后处理负责。
            SkyboxSetup.Apply();
        }

        private const string VolumePath = "Assets/Settings/WaterLabVolume.asset";

        private static VolumeProfile GetOrCreateVolumeProfile(Cubemap sky)
        {
            AssetDatabase.DeleteAsset(VolumePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Directory.CreateDirectory(Path.GetDirectoryName(VolumePath) ?? "Assets");
            AssetDatabase.CreateAsset(profile, VolumePath);

            bool Has<T>() where T : VolumeComponent =>
                profile.components.Exists(c => c != null && c is T);

            if (!Has<VisualEnvironment>())
            {
                var ve = profile.Add<VisualEnvironment>(true);
                ve.skyType.Override((int)UnityEngine.Rendering.HighDefinition.SkyType.HDRI);
            }

            if (!Has<HDRISky>())
            {
                var hs = profile.Add<HDRISky>(true);
                if (sky != null) hs.hdriSky.Override(sky);
            }

            if (!Has<Exposure>())
            {
                var ex = profile.Add<Exposure>(true);
                ex.mode.Override(ExposureMode.Fixed);
                ex.fixedExposure.Override(13.5f);
                ex.limitMax.Override(14f);
            }

            if (!Has<Bloom>())
            {
                var bl = profile.Add<Bloom>(true);
                bl.intensity.Override(0.35f);
                bl.threshold.Override(0.9f);
            }

            if (!Has<Tonemapping>())
            {
                var tm = profile.Add<Tonemapping>(true);
                tm.mode.Override(TonemappingMode.ACES);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static Material GetOrCreateSandMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SandMatPath);
            if (existing != null) return existing;

            var shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[Fengling] 找不到 HDRP/Lit，地形会用默认材质（可能是粉红色）");
                return null;
            }

            var mat = new Material(shader) { name = "CoastSand" };
            mat.SetColor("_BaseColor", new Color(0.76f, 0.68f, 0.50f, 1f));
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_Metallic", 0f);

            Directory.CreateDirectory(Path.GetDirectoryName(SandMatPath) ?? "Assets");
            AssetDatabase.CreateAsset(mat, SandMatPath);
            return mat;
        }

        private static Texture2D BakeHeightMap(Environment.CoastTerrain terrain)
        {
            AssetDatabase.DeleteAsset(HeightMapPath);

            var tex = terrain.BuildHeightMap(256);
            tex.name = "CoastHeightMap";
            Directory.CreateDirectory(Path.GetDirectoryName(HeightMapPath) ?? "Assets");
            AssetDatabase.CreateAsset(tex, HeightMapPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(HeightMapPath);
        }

        /// <summary>
        /// 生成可平铺的水面法线贴图。
        ///
        /// 用整数频率的正弦叠加，保证左右上下无缝（Perlin 做不到无缝，会有接缝线）。
        /// 高度场 h 求偏导得到法线，压到 0..1 存进纹理。
        /// </summary>
        private static Texture2D GetOrCreateNormalMap(string path, int seed, int bandCount)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int res = 256;
            var rng = new System.Random(seed);

            // 每层：频率(fx, fy)、振幅、相位
            var bands = new Vector4[bandCount];
            for (int i = 0; i < bandCount; i++)
            {
                int fx = 1 + rng.Next(0, 4);
                int fy = 1 + rng.Next(0, 4);
                float amp = 1f / (i + 1);
                float phase = (float)rng.NextDouble() * Mathf.PI * 2f;
                bands[i] = new Vector4(fx, fy, amp, phase);
            }

            var pixels = new Color32[res * res];
            const float heightScale = 0.35f;

            float H(float u, float v)
            {
                float h = 0f;
                for (int i = 0; i < bands.Length; i++)
                {
                    var b = bands[i];
                    h += b.z * Mathf.Sin(2f * Mathf.PI * (b.x * u + b.y * v) + b.w);
                }
                return h;
            }

            float eps = 1f / res;
            for (int y = 0; y < res; y++)
            {
                float v = (float)y / res;
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / res;

                    float hL = H(u - eps, v);
                    float hR = H(u + eps, v);
                    float hD = H(u, v - eps);
                    float hU = H(u, v + eps);

                    float dx = (hR - hL) * heightScale;
                    float dz = (hU - hD) * heightScale;

                    var n = new Vector3(-dx, -dz, 1f).normalized;
                    pixels[y * res + x] = new Color32(
                        (byte)((n.x * 0.5f + 0.5f) * 255f),
                        (byte)((n.y * 0.5f + 0.5f) * 255f),
                        (byte)((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true, true)
            {
                name = Path.GetFileNameWithoutExtension(path),
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, false);

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
            AssetDatabase.CreateAsset(tex, path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
