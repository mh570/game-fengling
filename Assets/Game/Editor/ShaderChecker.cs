using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 打印 shader 的编译消息（错误/警告），用于定位洋红材质。
    /// </summary>
    public static class ShaderChecker
    {
        [MenuItem("Fengling/Setup/Check Shaders", false, 60)]
        public static void CheckAll()
        {
            var guids = AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Game" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) continue;

                var messages = ShaderUtil.GetShaderMessages(shader);
                if (messages == null || messages.Length == 0) continue;

                var sb = new StringBuilder();
                sb.AppendLine("[ShaderCheck] " + path);
                foreach (var m in messages)
                {
                    sb.AppendLine($"  {m.severity} line {m.line}: {m.message}");
                }
                Debug.LogError(sb.ToString());
            }
            Debug.Log("[ShaderCheck] done");
        }
    }
}
