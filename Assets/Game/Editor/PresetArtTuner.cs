using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>场景级 Preset 调色：沙地 + 浅/深水 + 水面反射强度。</summary>
    public static class PresetArtTuner
    {
        private struct Palette
        {
            public Color Ground;
            public Color Shallow;
            public Color Deep;
            public float Reflection;
            public float Sun;
        }

        private static readonly string[] Scenes =
        {
            "River", "Waterfall", "Lake", "Pond", "Swamp", "Estuary"
        };

        private static Palette Get(string name)
        {
            switch (name)
            {
                case "River": return new Palette
                {
                    Ground = new Color(0.42f, 0.35f, 0.24f),
                    Shallow = new Color(0.20f, 0.48f, 0.42f),
                    Deep = new Color(0.035f, 0.14f, 0.14f),
                    Reflection = 0.14f, Sun = 1.5f,
                };
                case "Waterfall": return new Palette
                {
                    Ground = new Color(0.36f, 0.33f, 0.28f),
                    Shallow = new Color(0.42f, 0.70f, 0.78f),
                    Deep = new Color(0.07f, 0.22f, 0.30f),
                    Reflection = 0.14f, Sun = 1.3f,
                };
                case "Lake": return new Palette
                {
                    Ground = new Color(0.50f, 0.44f, 0.32f),
                    Shallow = new Color(0.20f, 0.50f, 0.60f),
                    Deep = new Color(0.020f, 0.10f, 0.20f),
                    Reflection = 0.12f, Sun = 1.4f,
                };
                case "Pond": return new Palette
                {
                    Ground = new Color(0.38f, 0.35f, 0.24f),
                    Shallow = new Color(0.24f, 0.45f, 0.30f),
                    Deep = new Color(0.055f, 0.15f, 0.10f),
                    Reflection = 0.12f, Sun = 1.4f,
                };
                case "Swamp": return new Palette
                {
                    Ground = new Color(0.28f, 0.26f, 0.17f),
                    Shallow = new Color(0.18f, 0.25f, 0.14f),
                    Deep = new Color(0.045f, 0.08f, 0.045f),
                    Reflection = 0.10f, Sun = 1.2f,
                };
                default: return new Palette
                {
                    Ground = new Color(0.46f, 0.40f, 0.29f),
                    Shallow = new Color(0.24f, 0.54f, 0.62f),
                    Deep = new Color(0.025f, 0.14f, 0.24f),
                    Reflection = 0.12f, Sun = 1.4f,
                };
            }
        }

        public static void Run()
        {
            foreach (var name in Scenes)
            {
                var palette = Get(name);
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");

                var ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Rendering/Materials/Mat_" + name + ".mat");
                if (ground != null)
                {
                    if (ground.HasProperty("_BaseColor")) ground.SetColor("_BaseColor", palette.Ground);
                    else if (ground.HasProperty("_Color")) ground.SetColor("_Color", palette.Ground);
                    EditorUtility.SetDirty(ground);
                }

                int waters = 0;
                foreach (var water in Object.FindObjectsByType<Water.WaterSurfaceRenderer>(FindObjectsSortMode.None))
                {
                    water.shallowColor = palette.Shallow;
                    water.deepColor = palette.Deep;
                    water.reflectionStrength = palette.Reflection;
                    water.sunIntensity = palette.Sun;
                    water.stylized = 0f;
                    waters++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[PresetArt] {name} waters={waters} ground={(ground != null)}");
            }
            AssetDatabase.SaveAssets();
        }
    }
}
