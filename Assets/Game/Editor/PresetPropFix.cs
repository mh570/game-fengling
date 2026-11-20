using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>把六套 Preset 里已生成的球体 Rock 替换成低多边形岩石 mesh。</summary>
    public static class PresetPropFix
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

        public static void Run()
        {
            var mesh = RockMeshFactory.GetOrCreate();
            foreach (var path in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path);
                int count = 0;
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    if (!t.name.StartsWith("Rock_")) continue;
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    mf.sharedMesh = mesh;
                    Vector3 s = t.localScale;
                    s.y = Mathf.Max(0.35f, s.x * 0.55f);
                    t.localScale = s;
                    count++;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[PresetPropFix] {path} rocks={count}");
            }
        }
    }
}
