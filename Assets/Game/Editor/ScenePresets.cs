using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 多场景预设：小河 / 瀑布 / 湖泊 / 池塘 / 沼泽 / 入海口。
    /// 每个菜单项生成一个独立的 .unity 场景，共用同一套水 shader，只换参数。
    /// </summary>
    public static class ScenePresets
    {
        private const string HeightMapDir = "Assets/Game/Environment/Terrain";
        private const string NormalMap1Path = "Assets/Game/Water/Textures/WaterNormal1.asset";
        private const string NormalMap2Path = "Assets/Game/Water/Textures/WaterNormal2.asset";

        private struct Preset
        {
            public string Name;
            public Vector2 Size;
            public float ShoreZ;
            public float LandHeight;
            public float SeaDepth;
            public float SlopeLength;
            public float WaterLevel;
            public Color Shallow;
            public Color Deep;
            public float Absorption;
            public float OpaqueAtDepth;
            public float Amplitude;
            public float FoamDistance;
            public float FoamStrength;
            public bool VerticalWater;
        }

        private static Preset River() => new Preset
        {
            Name = "River",
            Size = new Vector2(120f, 160f),
            ShoreZ = -10f,
            LandHeight = 6f,
            SeaDepth = 6f,
            SlopeLength = 40f,
            WaterLevel = 0f,
            Shallow = new Color(0.42f, 0.72f, 0.66f),
            Deep = new Color(0.10f, 0.30f, 0.30f),
            Absorption = 3f,
            OpaqueAtDepth = 2.5f,
            Amplitude = 0.35f,
            FoamDistance = 0.6f,
            FoamStrength = 0.8f,
            VerticalWater = false,
        };

        private static Preset Waterfall() => new Preset
        {
            Name = "Waterfall",
            Size = new Vector2(120f, 160f),
            ShoreZ = 0f,
            LandHeight = 22f,
            SeaDepth = 10f,
            SlopeLength = 8f,
            WaterLevel = 0f,
            Shallow = new Color(0.55f, 0.80f, 0.85f),
            Deep = new Color(0.12f, 0.34f, 0.42f),
            Absorption = 2f,
            OpaqueAtDepth = 2f,
            Amplitude = 0.2f,
            FoamDistance = 2.2f,
            FoamStrength = 2.2f,
            VerticalWater = true,
        };

        private static Preset Lake() => new Preset
        {
            Name = "Lake",
            Size = new Vector2(240f, 240f),
            ShoreZ = -90f,
            LandHeight = 5f,
            SeaDepth = 12f,
            SlopeLength = 90f,
            WaterLevel = 0f,
            Shallow = new Color(0.38f, 0.70f, 0.76f),
            Deep = new Color(0.03f, 0.16f, 0.28f),
            Absorption = 7f,
            OpaqueAtDepth = 10f,
            Amplitude = 0.18f,
            FoamDistance = 0.5f,
            FoamStrength = 0.6f,
            VerticalWater = false,
        };

        private static Preset Pond() => new Preset
        {
            Name = "Pond",
            Size = new Vector2(60f, 60f),
            ShoreZ = -20f,
            LandHeight = 2.5f,
            SeaDepth = 2.5f,
            SlopeLength = 18f,
            WaterLevel = 0f,
            Shallow = new Color(0.46f, 0.74f, 0.66f),
            Deep = new Color(0.12f, 0.32f, 0.28f),
            Absorption = 2f,
            OpaqueAtDepth = 2f,
            Amplitude = 0.05f,
            FoamDistance = 0.4f,
            FoamStrength = 0.4f,
            VerticalWater = false,
        };

        private static Preset Swamp() => new Preset
        {
            Name = "Swamp",
            Size = new Vector2(120f, 120f),
            ShoreZ = -30f,
            LandHeight = 1.2f,
            SeaDepth = 1.6f,
            SlopeLength = 40f,
            WaterLevel = 0f,
            Shallow = new Color(0.30f, 0.38f, 0.24f),
            Deep = new Color(0.10f, 0.16f, 0.10f),
            Absorption = 1.5f,
            OpaqueAtDepth = 1.4f,
            Amplitude = 0.03f,
            FoamDistance = 0.3f,
            FoamStrength = 0.25f,
            VerticalWater = false,
        };

        private static Preset Estuary() => new Preset
        {
            Name = "Estuary",
            Size = new Vector2(200f, 200f),
            ShoreZ = 0f,
            LandHeight = 3f,
            SeaDepth = 8f,
            SlopeLength = 60f,
            WaterLevel = 0f,
            Shallow = new Color(0.42f, 0.76f, 0.80f),
            Deep = new Color(0.04f, 0.22f, 0.36f),
            Absorption = 6f,
            OpaqueAtDepth = 9f,
            Amplitude = 0.5f,
            FoamDistance = 1.2f,
            FoamStrength = 1.2f,
            VerticalWater = false,
        };

        /// <summary>Batch entry point: build all six scenes.</summary>
        public static void BuildAll()
        {
            BuildRiver();
            BuildWaterfall();
            BuildLake();
            BuildPond();
            BuildSwamp();
            BuildEstuary();
            Debug.Log("[Preset] all six scenes built");
        }

        [MenuItem("Fengling/Scenes/1 River", false, 20)]
        private static void BuildRiver() => Build(River());

        [MenuItem("Fengling/Scenes/2 Waterfall", false, 21)]
        private static void BuildWaterfall() => Build(Waterfall());

        [MenuItem("Fengling/Scenes/3 Lake", false, 22)]
        private static void BuildLake() => Build(Lake());

        [MenuItem("Fengling/Scenes/4 Pond", false, 23)]
        private static void BuildPond() => Build(Pond());

        [MenuItem("Fengling/Scenes/5 Swamp", false, 24)]
        private static void BuildSwamp() => Build(Swamp());

        [MenuItem("Fengling/Scenes/6 Estuary", false, 25)]
        private static void BuildEstuary() => Build(Estuary());

        private static void Build(Preset p)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // ---- terrain
            var terrainGo = new GameObject("Terrain_" + p.Name);
            terrainGo.AddComponent<MeshFilter>();
            var mr = terrainGo.AddComponent<MeshRenderer>();
            var terrain = terrainGo.AddComponent<Environment.CoastTerrain>();
            terrain.size = p.Size;
            terrain.shoreZ = p.ShoreZ;
            terrain.landHeight = p.LandHeight;
            terrain.seaDepth = p.SeaDepth;
            terrain.slopeLength = p.SlopeLength;
            mr.sharedMaterial = MakeMaterial("Mat_" + p.Name, new Color(0.66f, 0.60f, 0.46f), 0.05f);
            terrain.Generate();

            var heightMap = BakeHeightMap(terrain, p.Name);

            PropSpawner.SpawnAll(null, terrain, p.Size.x);

            // ---- water
            var waterGo = new GameObject("Water");
            var wm = waterGo.AddComponent<Water.WaterModule>();
            // 封闭的小水体（池塘/沼泽）严格贴合地形；开放水体向外延伸，
            // 避免 200m 水面边缘在低机位下变成一条黑色海平线。
            bool enclosed = p.Name == "Pond" || p.Name == "Swamp";
            wm.Settings.size = enclosed ? p.Size : p.Size * 4f;
            wm.Settings.baseLevel = p.WaterLevel;
            wm.Settings.amplitudeScale = p.Amplitude;

            var surface = new GameObject("WaterSurface");
            var sm = surface.AddComponent<Water.WaterSurfaceRenderer>();
            sm.water = wm;
            sm.waterMaterialTemplate = WaterMaterialProvider.GetOrCreate();
            sm.terrainHeightMap = heightMap;
            sm.heightMapBounds = new Vector4(0f, 0f, p.Size.x, p.Size.y);
            sm.heightRange = terrain.HeightMapRange;
            sm.normalMap1 = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMap1Path);
            sm.normalMap2 = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMap2Path);
            sm.shallowColor = p.Shallow;
            sm.deepColor = p.Deep;
            sm.absorptionDistance = p.Absorption;
            sm.opaqueAtDepth = p.OpaqueAtDepth;
            sm.foamDistance = p.FoamDistance;
            sm.foamStrength = p.FoamStrength;
            sm.stylized = 0f;
            sm.flowDirection = new Vector2(0f, 1f);
            sm.flowSpeed = p.Name == "River" ? 0.8f : 0.05f;
            sm.verticalFlow = 0f;

            // P4 topdown baseline
            sm.autoTier = false;
            sm.useFoam = true;
            sm.useReflectionCube = false;
            sm.normalTiling1 = 2f;
            sm.normalTiling2 = 5f;
            sm.normalStrength = 0.45f;
            sm.roughness = 0.5f;
            sm.sunIntensity = 0.2f;
            sm.f0 = 0f;

            if (p.VerticalWater) BuildWaterfallSheet(surface.transform, p);

            // ---- camera (topdown RPG default; C 切第一人称)
            var player = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            player.name = "Player";
            player.transform.position = new Vector3(0f, p.WaterLevel + 2f, p.ShoreZ + 10f);
            player.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
            var playerMat = MakeMaterial("PlayerBody", new Color(0.72f, 0.32f, 0.24f), 0.2f);
            player.GetComponent<Renderer>().sharedMaterial = playerMat;
            var cap = player.GetComponent<CapsuleCollider>();
            if (cap != null) Object.DestroyImmediate(cap);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f; cc.radius = 0.45f;
            player.AddComponent<Player.PlayerMotor>();

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            var fps = cam.gameObject.AddComponent<Player.FirstPersonCamera>();
            fps.target = player.transform;
            fps.enabled = false;
            var top = cam.gameObject.AddComponent<Player.TopDownCamera>();
            top.pitch = 55f;
            top.yaw = 45f;
            top.orthoSize = Mathf.Clamp(p.Size.x * 0.12f, 18f, 28f);
            top.focus = new Vector3(0f, p.WaterLevel, p.ShoreZ + 20f);
            top.followTarget = player.transform;
            top.followOffset = new Vector3(0f, 0f, 12f);
            top.enabled = true;
            var sw = cam.gameObject.AddComponent<Player.CameraModeSwitch>();
            sw.firstPerson = fps;
            sw.topDown = top;
            sw.startInFirstPerson = false;

            // ---- sun + volume
            var sunGo = GameObject.Find("Directional Light");
            Light sun = sunGo != null ? sunGo.GetComponent<Light>() : null;
            if (sun == null)
            {
                sunGo = new GameObject("Directional Light");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.intensity = 9f;
            sun.color = new Color(1f, 0.93f, 0.80f);
            sunGo.transform.rotation = Quaternion.Euler(42f, -138f, 0f);
            RenderSettings.sun = sun;

            // 自建管线不读 HDRP Volume；天空/环境光/曝光都由 Fengling 自己负责。
            SkyboxSetup.Apply();

            // ---- save
            Directory.CreateDirectory("Assets/Scenes");
            var path = "Assets/Scenes/" + p.Name + ".unity";
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == path))
                list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[Preset] 已生成 " + path);
        }

        private static void BuildWaterfallSheet(Transform parent, Preset p)
        {
            // 程序化地形是缓坡，不是崖壁；给 Waterfall 单独加一面竖直岩壁，
            // 否则低机位会看到一面“土墙”或者直接看穿坡面。
            var cliff = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cliff.name = "WaterfallCliff";
            cliff.transform.SetParent(parent, false);
            cliff.transform.localScale = new Vector3(p.Size.x, p.LandHeight, 1f);
            cliff.transform.position = new Vector3(0f, p.LandHeight * 0.5f,
                p.ShoreZ + p.SlopeLength + 0.3f);
            cliff.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var cliffCol = cliff.GetComponent<MeshCollider>();
            if (cliffCol != null) Object.DestroyImmediate(cliffCol);
            cliff.GetComponent<Renderer>().sharedMaterial =
                MakeMaterial("Mat_WaterfallCliff", new Color(0.34f, 0.32f, 0.29f), 0.08f);

            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "WaterfallSheet";
            plane.transform.SetParent(parent, false);
            plane.transform.localScale = new Vector3(24f, p.LandHeight, 1f);
            // 放在崖壁海侧，避免整块 sheet 埋进崖壁/地形里。
            plane.transform.position = new Vector3(0f, p.LandHeight * 0.5f,
                p.ShoreZ + p.SlopeLength + 0.5f);
            plane.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var col = plane.GetComponent<MeshCollider>();
            if (col != null) Object.DestroyImmediate(col);

            var sm = plane.AddComponent<Water.WaterSurfaceRenderer>();
            sm.water = Object.FindFirstObjectByType<Water.WaterModule>();
            sm.waterMaterialTemplate = WaterMaterialProvider.GetOrCreate();
            sm.buildMesh = false;              // 保留 Quad 的竖直网格
            sm.verticalFlow = 1f;
            sm.flowSpeed = 1.6f;
            sm.shallowColor = p.Shallow;
            sm.deepColor = p.Deep;
            sm.normalStrength = 1.6f;
            sm.foamStrength = 2.4f;
        }

        private static Texture2D BakeHeightMap(Environment.CoastTerrain terrain, string sceneName)
        {
            var path = HeightMapDir + "/CoastHeightMap_" + sceneName + ".asset";
            AssetDatabase.DeleteAsset(path);

            var tex = terrain.BuildHeightMap(256);
            tex.name = "CoastHeightMap_" + sceneName;

            Directory.CreateDirectory(HeightMapDir);
            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void AddVolume()
        {
            const string path = "Assets/Settings/PresetVolume.asset";
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(profile, path);

            var ve = profile.Add<VisualEnvironment>(true);
            ve.skyType.Override((int)SkyType.HDRI);

            var hs = profile.Add<HDRISky>(true);
            var sky = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Settings/WaterLabSky.asset");
            if (sky != null) hs.hdriSky.Override(sky);

            var ex = profile.Add<Exposure>(true);
            ex.mode.Override(ExposureMode.Fixed);
            ex.fixedExposure.Override(13.5f);

            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);

            var go = new GameObject("Global Volume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.profile = profile;
        }

        private static Material MakeMaterial(string name, Color color, float smoothness)
        {
            return LitMaterialProvider.Make(name, color, smoothness);
        }
    }
}
