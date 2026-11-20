using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>把 WeatherSystem 接到六套 Preset 的 TimeOfDay。</summary>
    public static class PresetWeatherInstaller
    {
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/River.unity",
            "Assets/Scenes/Waterfall.unity",
            "Assets/Scenes/Lake.unity",
            "Assets/Scenes/Pond.unity",
            "Assets/Scenes/Swamp.unity",
            "Assets/Scenes/Estuary.unity",
        };

        [MenuItem("Fengling/Scenes/Install Weather In Presets")]
        public static void InstallAll()
        {
            foreach (var path in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path);
                var tod = Object.FindFirstObjectByType<Time.TimeOfDaySystem>();
                if (tod == null)
                {
                    Debug.LogWarning("[PresetWeather] no TimeOfDay in " + path);
                    continue;
                }
                var weather = tod.GetComponent<Time.WeatherSystem>();
                if (weather == null) weather = tod.gameObject.AddComponent<Time.WeatherSystem>();
                weather.cloudCover = 0.35f;
                weather.haze = 0.15f;
                weather.fog = 0.10f;
                weather.rain = 0f;
                weather.snow = 0f;
                tod.weather = weather;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[PresetWeather] installed " + path);
            }
        }
    }
}
