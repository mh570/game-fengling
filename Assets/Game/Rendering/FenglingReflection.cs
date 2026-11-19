using System;
using UnityEngine;

namespace Fengling.Rendering
{
    /// <summary>
    /// 平面反射 RT/矩阵的当前帧绑定。和场景深度一样，走事件而不是 Update 读全局，
    /// 保证水面材质拿到的是本帧反射，而不是上一帧已释放的 RT。
    /// </summary>
    public static class FenglingReflection
    {
        public static event Action<Texture, Matrix4x4> Ready;

        public static void Publish(Texture reflection, Matrix4x4 viewProjection)
        {
            Ready?.Invoke(reflection, viewProjection);
        }
    }
}
