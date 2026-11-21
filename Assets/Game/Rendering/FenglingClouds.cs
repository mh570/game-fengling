using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 体积云 pass。
    ///
    /// 为什么放在不透明之后、透明之前：
    ///   云是半透明的远景，应该在陆地之后、水面之前合成，
    ///   这样水面能看到被云遮挡后的天空颜色。
    ///
    /// 第一版是全分辨率 40 步，先验证"能看到云的体积"，
    /// 之后再改半分辨率 + 上采样。
    /// </summary>
    public sealed class FenglingClouds
    {
        private Material _material;
        private bool _shaderMissing;

        public bool Enabled = true;

        // 与天气/时间同步的参数（由管线每帧推入）
        public float Coverage = 0.45f;
        public float Density = 0.6f;
        public float Altitude = 600f;
        public float Thickness = 600f;
        public float Scale = 0.0008f;
        public int RaySteps = 40;

        public void Render(ScriptableRenderContext context, Camera camera,
                           RenderTargetIdentifier colorTarget, RenderTargetIdentifier depthTarget)
        {
            if (!Enabled)
            {
                Shader.SetGlobalFloat("_FenglingVolumetricClouds", 0f);
                return;
            }

            // 通知天空盒：体积云开着时压掉它的中/近层，避免双层云叠在一起
            Shader.SetGlobalFloat("_FenglingVolumetricClouds", 1f);

            if (_material == null && !_shaderMissing)
            {
                var shader = Shader.Find("Fengling/Volumetric Clouds");
                if (shader == null)
                {
                    _shaderMissing = true;
                    Debug.LogWarning("[Clouds] 找不到 shader 'Fengling/Volumetric Clouds'，体积云关闭");
                    return;
                }
                _material = new Material(shader) { name = "FenglingClouds (instance)" };
            }
            if (_material == null) return;

            var cmd = CommandBufferPool.Get("Fengling.Clouds");

            cmd.SetRenderTarget(colorTarget, depthTarget);

            // 相机逆视投影矩阵：fragment 里用来从 UV 重建世界方向
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 viewProj = proj * camera.worldToCameraMatrix;
            cmd.SetGlobalMatrix("_CameraInvViewProj", viewProj.inverse);
            cmd.SetGlobalVector("_CameraPos", camera.transform.position);

            Vector3 sunDir = RenderSettings.sun != null
                ? -RenderSettings.sun.transform.forward
                : Vector3.up;
            cmd.SetGlobalVector("_SunDirection", sunDir.normalized);
            cmd.SetGlobalColor("_SunColor",
                RenderSettings.sun != null
                    ? RenderSettings.sun.color * Mathf.Max(0.3f, RenderSettings.sun.intensity * 0.12f)
                    : Color.white);

            _material.SetFloat("_Coverage", Coverage);
            _material.SetFloat("_Density", Density);
            _material.SetFloat("_Altitude", Altitude);
            _material.SetFloat("_Thickness", Thickness);
            _material.SetFloat("_Scale", Scale);
            _material.SetFloat("_RaySteps", RaySteps);

            // 全屏三角形绘制（材质自带 Blend One OneMinusSrcAlpha）
            cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
