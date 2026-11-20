using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>修 Preset 里明显过曝的水帘参数。</summary>
    public static class PresetVisualFix
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Waterfall.unity");
            var sheet = GameObject.Find("WaterfallSheet");
            if (sheet == null)
            {
                Debug.LogError("[PresetVisual] WaterfallSheet missing");
                EditorApplication.Exit(1);
                return;
            }
            var water = sheet.GetComponent<Water.WaterSurfaceRenderer>();
            if (water == null)
            {
                Debug.LogError("[PresetVisual] WaterSurfaceRenderer missing");
                EditorApplication.Exit(1);
                return;
            }
            water.foamStrength = 0.65f;
            water.normalStrength = 1.0f;
            water.flowSpeed = 1.1f;
            water.sunIntensity = 1.2f;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PresetVisual] waterfall foam={water.foamStrength} normal={water.normalStrength} sun={water.sunIntensity}");
            EditorApplication.Exit(0);
        }
    }
}
