using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 创建 Unity Terrain 并导入 Skye 的 RAW 高度图。
    ///
    /// 数据来自 TerrariumToRaw：
    ///   RAW: 1025x1025 16 位小端，0..65535 线性映射 minH..maxH
    ///   高程: -50 ~ 478.6 m
    ///
    /// 布局：
    ///   terrain.transform.position.y = MinHeight（Unity Terrain 的高度永远从 0 开始算）
    ///   terrainData.size.y = MaxHeight - MinHeight
    ///
    /// 注意：Terrain 的材质在自建 SRP 下会缺 shader（会显示洋红），
    ///       下一步要写 Fengling/Terrain shader。
    /// </summary>
    public static class IslandTerrainBuilder
    {
        private const int Res = 1025;
        private const float MinH = -50f;
        private const float MaxH = 478.6f;
        private const float SpanMeters = 2600f;   // 2.6 km

        private const string RawPath = "Assets/Resources/Terrain/skye_2km6.raw";
        private const string AssetPath = "Assets/Resources/Terrain/SkyeTerrain.asset";
        private const string ScenePath = "Assets/Scenes/Island.unity";

        [MenuItem("Fengling/Terrain/创建岛地形")]
        public static void Run()
        {
            if (!File.Exists(RawPath))
            {
                Debug.LogError("[Terrain] 找不到 RAW: " + RawPath);
                return;
            }

            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(AssetPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, AssetPath);
            }

            // 尺寸与分辨率
            data.heightmapResolution = Res;
            data.size = new Vector3(SpanMeters, MaxH - MinH, SpanMeters);

            // 导入 RAW
            byte[] raw = File.ReadAllBytes(RawPath);
            int expected = Res * Res * 2;
            if (raw.Length != expected)
            {
                Debug.LogError("[Terrain] RAW 大小不对: " + raw.Length + " 期望 " + expected);
                return;
            }

            var heights = new float[Res, Res];
            float minT = float.MaxValue, maxT = float.MinValue;
            for (int y = 0; y < Res; y++)
            {
                for (int x = 0; x < Res; x++)
                {
                    int i = (y * Res + x) * 2;
                    ushort v = (ushort)(raw[i] | (raw[i + 1] << 8));
                    float t = v / 65535f;
                    heights[y, x] = t;
                    if (t < minT) minT = t;
                    if (t > maxT) maxT = t;
                }
            }
            data.SetHeights(0, 0, heights);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            // 建场景
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Terrain_Island");
            var terrain = go.AddComponent<Terrain>();
            var collider = go.AddComponent<TerrainCollider>();
            terrain.terrainData = data;
            collider.terrainData = data;
            go.transform.position = new Vector3(-SpanMeters * 0.5f, MinH, -SpanMeters * 0.5f);

            // 相机：俯视
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.85f);
            cam.transform.position = new Vector3(0f, 700f, -900f);
            cam.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
            cam.farClipPlane = 5000f;
            // 暂时不加 RtsCameraController：它没有地形参照会把相机压到地面以下
            // 等地形验证通过、并给 Terrain 写 sampler 适配器后再加回

            // 太阳
            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = sun;

            // 时间系统
            var tod = new GameObject("TimeOfDay").AddComponent<Fengling.Time.TimeOfDaySystem>();
            tod.sun = sun;
            tod.timeOfDay = 10f;

            // 引导器
            new GameObject("EnvironmentBootstrap").AddComponent<Fengling.Time.EnvironmentBootstrap>();
            new GameObject("TerrainSceneBootstrap").AddComponent<Fengling.Environment.TerrainSceneBootstrap>();
            new GameObject("PlayerSceneBootstrap").AddComponent<Fengling.Player.PlayerSceneBootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log(string.Format(
                "[Terrain] 已创建 {0}\n  尺寸 {1}m x {2}m，高 {3:F1}m，位置 y={4:F1}\n  高度范围(t) {5:F3} ~ {6:F3}",
                ScenePath, SpanMeters, SpanMeters, MaxH - MinH, MinH, minT, maxT));
        }
    }
}
