using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 工程原始 NoramlLineRenderPassFeature（法线描边）的 Unity 6 兼容适配版。
///
/// 原版（Unity 2021）用 ConfigureTarget(int) + cmd.GetTemporaryRT(int)，
/// Unity 6 改为 RTHandle 体系。
///
/// 适配点：
///   ① int RT id → RTHandle（RenderingUtils.ReAllocateIfNeeded）
///   ② ConfigureTarget(int) → ConfigureTarget(RTHandle)
///   ③ Execute 里的 cmd.SetRenderTarget(RTHandle) 显式绑定
///   ④ blit 用 RTHandle 版本
/// </summary>
public class NoramlLineRenderPassFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Setting
    {
        public LayerMask layer = ~0;
        public Material normalTexMat;
        public Material normalLineMat;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPrePasses;
        [Range(0, 1)]
        public float Edge = 0;
    }

    public Setting setting = new Setting();

    // ===================== Pass 1: 把法线渲染到 _NormalTex =====================
    public class DrawNormalTexPass : ScriptableRenderPass
    {
        private Setting setting;
        private RTHandle normalTexHandle;
        ShaderTagId shaderTag = new ShaderTagId("DepthOnly");
        FilteringSettings filter;

        public DrawNormalTexPass(Setting setting, NoramlLineRenderPassFeature feature)
        {
            this.setting = setting;

            RenderQueueRange queue = new RenderQueueRange();
            queue.lowerBound = 1000;
            queue.upperBound = 3500;
            filter = new FilteringSettings(queue, setting.layer);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // ★ 适配：int RT → RTHandle
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref normalTexHandle, desc,
                FilterMode.Point, TextureWrapMode.Clamp, name: "_NormalTex");

            ConfigureTarget(normalTexHandle);           // ★ 适配：接受 RTHandle
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("绘制NormalTex");
            // ★ 适配：显式绑定 RTHandle
            cmd.SetRenderTarget(normalTexHandle);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var draw = CreateDrawingSettings(shaderTag, ref renderingData,
                renderingData.cameraData.defaultOpaqueSortFlags);
            draw.overrideMaterial = setting.normalTexMat;
            draw.overrideMaterialPassIndex = 0;
            context.DrawRenderers(renderingData.cullResults, ref draw, ref filter);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            normalTexHandle?.Release();
            normalTexHandle = null;
        }
    }

    // ===================== Pass 2: 对法线图做边缘检测 =====================
    public class DrawNoramlLinePass : ScriptableRenderPass
    {
        private Setting setting;
        private RTHandle normalLineHandle;

        public DrawNoramlLinePass(Setting setting, NoramlLineRenderPassFeature feature)
        {
            this.setting = setting;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref normalLineHandle, desc,
                FilterMode.Point, TextureWrapMode.Clamp, name: "_NormalLineTex");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (setting.normalLineMat == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("绘制法线描边");
            setting.normalLineMat.SetFloat("_Edge", setting.Edge);

            // ★ 适配：RTHandle blit（把 _NormalTex 处理到 _NormalLineTex）
            Blitter.BlitCameraTexture(cmd, normalLineHandle, normalLineHandle,
                setting.normalLineMat, 0);

            // 叠加回相机目标
            var camTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Blitter.BlitCameraTexture(cmd, normalLineHandle, camTarget,
                setting.normalLineMat, 0);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            normalLineHandle?.Release();
            normalLineHandle = null;
        }
    }

    private DrawNormalTexPass _DrawNormalTexPass;
    private DrawNoramlLinePass _DrawNoramlLinePass;

    public override void Create()
    {
        _DrawNormalTexPass = new DrawNormalTexPass(setting, this);
        _DrawNormalTexPass.renderPassEvent = setting.passEvent;

        _DrawNoramlLinePass = new DrawNoramlLinePass(setting, this);
        _DrawNoramlLinePass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (setting.normalTexMat == null || setting.normalLineMat == null) return;
        renderer.EnqueuePass(_DrawNormalTexPass);
        renderer.EnqueuePass(_DrawNoramlLinePass);
    }

    protected override void Dispose(bool disposing)
    {
        _DrawNormalTexPass = null;
        _DrawNoramlLinePass = null;
    }
}
