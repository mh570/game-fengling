using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 运行时排查：把场景里每个渲染器用的材质和 shader 打进 Console。
    /// 洋红问题只有两种可能——材质为空，或 shader 为空/出错，这个菜单能直接区分。
    /// 请在 Play 状态下使用。
    /// </summary>
    public static class RenderDebugLogger
    {
        [MenuItem("Fengling/Debug/Log Renderer Materials", false, 80)]
        public static void LogAll()
        {
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var sb = new StringBuilder();
            sb.AppendLine($"[RenderDebug] 共 {renderers.Length} 个渲染器");

            int nullMat = 0, nullShader = 0;
            foreach (var r in renderers)
            {
                var mat = r.sharedMaterial;
                string matName = mat != null ? mat.name : "<null>";
                string shaderName = mat != null && mat.shader != null ? mat.shader.name : "<null shader>";
                if (mat == null) nullMat++;
                else if (mat.shader == null) nullShader++;
                sb.AppendLine($"  {r.name} | {matName} | {shaderName}");
            }

            sb.AppendLine($"[RenderDebug] 材质为空: {nullMat}，shader 为空: {nullShader}");
            Debug.Log(sb.ToString());
        }
    }
}
