using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>把所有场景里的 RTS 相机改成"默认自由观察 + F 键切换"。</summary>
    public static class CameraModeDefaults
    {
        public static void Run()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var rts = Object.FindFirstObjectByType<Fengling.Player.RtsCameraController>();
                if (rts == null) continue;

                bool dirty = false;
                if (rts.startFocused) { rts.startFocused = false; dirty = true; }
                if (rts.focusKey != KeyCode.F) { rts.focusKey = KeyCode.F; dirty = true; }

                if (dirty)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changed++;
                    Debug.Log("[CamDefaults] 已改 " + path);
                }
            }
            Debug.Log("[CamDefaults] 共修改 " + changed + " 个场景");
            EditorApplication.Exit(0);
        }
    }
}