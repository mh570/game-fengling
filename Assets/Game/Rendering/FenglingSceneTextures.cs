using System;
using UnityEngine;

namespace Fengling.Rendering
{
    /// <summary>
    /// 管线把当前帧的深度/场景颜色交给水面材质。
    /// 放在 Rendering assembly 里，避免 Rendering 反向依赖 Water 的预编译程序集。
    /// </summary>
    public static class FenglingSceneTextures
    {
        public static event Action<Texture, Texture> Ready;

        public static void Publish(Texture depth, Texture color)
        {
            Ready?.Invoke(depth, color);
        }
    }
}
