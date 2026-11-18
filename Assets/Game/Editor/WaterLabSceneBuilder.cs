using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
    public static class WaterLabSceneBuilder
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

            // 4. 水模块
            var waterGo = new GameObject("Water");
            var waterModule = waterGo.AddComponent<Water.WaterModule>();
            waterGo.AddComponent<Water.WaterFieldGizmo>();
            waterGo.AddComponent<Water.WaterLabHUD>();

            // 5. 水面渲染（网格 + 材质 + 每帧喂参数）
            var surfaceGo = new GameObject("WaterSurface");
            surfaceGo.AddComponent<MeshFilter>();
            surfaceGo.AddComponent<MeshRenderer>();
            var surfaceRenderer = surfaceGo.AddComponent<Water.WaterSurfaceRenderer>();
            surfaceRenderer.water = waterModule;
            surfaceRenderer.terrainHeightMap = heightMap;
            surfaceRenderer.heightMapBounds = new Vector4(
                terrain.transform.position.x,
                terrain.transform.position.z,
                terrain.size.x,
                terrain.size.y);
            surfaceRenderer.heightRange = terrain.HeightMapRange;
            surfaceRenderer.normalMap1 = GetOrCreateNormalMap(NormalMap1Path, 1337, 5);
            surfaceRenderer.normalMap2 = GetOrCreateNormalMap(NormalMap2Path, 4242, 9);

            // 6. 玩家（站在陆地上）
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, terrain.landHeight + 1.5f, terrain.shoreZ - 20f);

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            player.AddComponent<Player.PlayerMotor>();
            player.AddComponent<Player.PlayerBuoyancy>();

            // 7. 相机挂到玩家头上
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.SetParent(player.transform, false);
            cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            cam.transform.localRotation = Quaternion.identity;

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
