using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// HDRP 风格统一方向光阴影 atlas：太阳 4 级级联共用一个 4096 RFloat。
    /// 每级是 atlas 的一个 tile，view/proj 与 tile offset 分开传给 shader。
    /// </summary>
    public sealed class FenglingShadows
    {
        public const int CascadeCount = 4;
        private const int AtlasSize = 4096;
        private const int MoonSize = 2048;
        private static readonly Vector3 CascadeRatios = new Vector3(0.1f, 0.25f, 0.5f);

        private readonly int[] _sizes = { 2048, 1024, 1024, 512 };
        private readonly Vector2Int[] _offsets =
        {
            new Vector2Int(0, 0),
            new Vector2Int(2048, 0),
            new Vector2Int(2048, 1024),
            new Vector2Int(0, 2048),
        };

        private readonly Matrix4x4[] _views = new Matrix4x4[CascadeCount];
        private readonly Matrix4x4[] _vps = new Matrix4x4[CascadeCount];
        private readonly Vector4[] _spheres = new Vector4[CascadeCount];
        private readonly Vector4[] _tiles = new Vector4[CascadeCount];
        private readonly float[] _splits = new float[CascadeCount];

        private RenderTexture _atlas;
        private RenderTexture _moonMap;
        public int Resolution = 2048;

        private bool _hasRendered;
        private Vector3 _lastLightDir;
        private Vector3 _lastCameraPos;
        private Vector3 _lastCameraForward;
        private float _lastDistance;
        private int _lastResolution;

        private void EnsureAtlas()
        {
            if (_atlas != null && _atlas.width == AtlasSize) return;
            if (_atlas != null)
            {
                _atlas.Release();
                Object.DestroyImmediate(_atlas);
            }
            _atlas = new RenderTexture(AtlasSize, AtlasSize, 24, RenderTextureFormat.RFloat)
            {
                name = "FenglingDirectionalShadowAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _atlas.Create();
        }

        public void Render(ScriptableRenderContext context, Camera camera, CullingResults culling,
                           Vector3 lightDirection, float distance, int resolution,
                           RenderTargetIdentifier restoreTarget, RenderTargetIdentifier restoreDepthTarget)
        {
            Resolution = Mathf.Clamp(resolution, 256, 2048);
            EnsureAtlas();

            if (_hasRendered &&
                Mathf.Abs(_lastDistance - distance) < 0.01f &&
                _lastResolution == Resolution &&
                Vector3.Angle(_lastLightDir, lightDirection) < 0.15f &&
                (_lastCameraPos - camera.transform.position).sqrMagnitude < 0.25f &&
                Vector3.Angle(_lastCameraForward, camera.transform.forward) < 0.5f)
            {
                return;
            }

            int lightIndex = -1;
            var visible = culling.visibleLights;
            var sun = RenderSettings.sun;
            for (int i = 0; i < visible.Length; i++)
            {
                if (visible[i].lightType == LightType.Directional &&
                    (sun == null || visible[i].light == sun))
                {
                    lightIndex = i;
                    break;
                }
            }
            if (lightIndex < 0)
            {
                Disable();
                return;
            }

            var clear = CommandBufferPool.Get("Fengling.ShadowAtlasClear");
            clear.SetRenderTarget(_atlas);
            clear.ClearRenderTarget(true, true, new Color(1000f, 0f, 0f, 1f));
            context.ExecuteCommandBuffer(clear);
            CommandBufferPool.Release(clear);

            for (int i = 0; i < CascadeCount; i++)
            {
                int size = _sizes[i];
                var offset = _offsets[i];
                culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    lightIndex, i, CascadeCount, CascadeRatios, size, 0.1f,
                    out Matrix4x4 view, out Matrix4x4 proj, out ShadowSplitData split);

                _views[i] = view;
                _vps[i] = proj * view;
                _spheres[i] = split.cullingSphere;
                _splits[i] = distance * (i + 1f) / CascadeCount;
                _tiles[i] = new Vector4(
                    size / (float)AtlasSize,
                    size / (float)AtlasSize,
                    offset.x / (float)AtlasSize,
                    offset.y / (float)AtlasSize);

                var setup = CommandBufferPool.Get("Fengling.ShadowSlice" + i);
                setup.SetRenderTarget(_atlas);
                setup.SetViewport(new Rect(offset.x, offset.y, size, size));
                setup.SetViewProjectionMatrices(view, proj);
                context.ExecuteCommandBuffer(setup);
                CommandBufferPool.Release(setup);

                var settings = new ShadowDrawingSettings(culling, lightIndex)
                {
                    splitData = split,
                };
                var list = context.CreateShadowRendererList(ref settings);
                var draw = CommandBufferPool.Get("Fengling.ShadowSliceDraw" + i);
                draw.DrawRendererList(list);
                context.ExecuteCommandBuffer(draw);
                CommandBufferPool.Release(draw);
            }

            var restore = CommandBufferPool.Get("Fengling.ShadowRestore");
            restore.SetRenderTarget(restoreTarget, restoreDepthTarget);
            context.ExecuteCommandBuffer(restore);
            CommandBufferPool.Release(restore);
            context.SetupCameraProperties(camera);

            Shader.SetGlobalTexture("_FenglingShadowAtlas", _atlas);
            for (int i = 0; i < CascadeCount; i++)
            {
                Shader.SetGlobalMatrix("_FenglingShadowView" + i, _views[i]);
                Shader.SetGlobalMatrix("_FenglingShadowVP" + i, _vps[i]);
                Shader.SetGlobalVector("_FenglingShadowTile" + i, _tiles[i]);
            }
            Shader.SetGlobalVectorArray("_FenglingShadowSpheres", _spheres);
            Shader.SetGlobalVector("_FenglingShadowSplits",
                new Vector4(_splits[0], _splits[1], _splits[2], _splits[3]));
            Shader.SetGlobalFloat("_FenglingShadowTexel", 1f / AtlasSize);
            Shader.SetGlobalFloat("_FenglingShadowNormalBias",
                Mathf.Max(0.05f, distance / Resolution * 1.5f));
            Shader.SetGlobalFloat("_FenglingShadowSoftness", 1f);
            Shader.SetGlobalFloat("_FenglingShadowEnabled", 1f);
            Shader.SetGlobalFloat("_FenglingMoonShadowEnabled", 0f);

            _hasRendered = true;
            _lastLightDir = lightDirection;
            _lastCameraPos = camera.transform.position;
            _lastCameraForward = camera.transform.forward;
            _lastDistance = distance;
            _lastResolution = Resolution;
        }

        public void Disable()
        {
            _hasRendered = false;
            Shader.SetGlobalFloat("_FenglingShadowEnabled", 0f);
            Shader.SetGlobalFloat("_FenglingMoonShadowEnabled", 0f);
        }

        private void EnsureMoonMap()
        {
            if (_moonMap != null && _moonMap.width == MoonSize) return;
            if (_moonMap != null)
            {
                _moonMap.Release();
                Object.DestroyImmediate(_moonMap);
            }
            _moonMap = new RenderTexture(MoonSize, MoonSize, 24, RenderTextureFormat.RFloat)
            {
                name = "FenglingMoonShadowMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _moonMap.Create();
        }

        public void RenderMoonShadow(ScriptableRenderContext context, Camera camera, CullingResults culling,
                                     RenderTargetIdentifier restoreTarget, RenderTargetIdentifier restoreDepthTarget)
        {
            EnsureMoonMap();
            var sun = RenderSettings.sun;
            int lightIndex = -1;
            for (int i = 0; i < culling.visibleLights.Length; i++)
            {
                var light = culling.visibleLights[i];
                if (light.lightType != LightType.Directional) continue;
                if (sun != null && light.light == sun) continue;
                if (light.light == null || light.light.shadows == LightShadows.None) continue;
                if (light.light.intensity <= 0.02f) continue;
                lightIndex = i;
                break;
            }

            if (lightIndex < 0)
            {
                Shader.SetGlobalFloat("_FenglingMoonShadowEnabled", 0f);
                return;
            }

            culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                lightIndex, 0, 1, CascadeRatios, MoonSize, 0.1f,
                out Matrix4x4 view, out Matrix4x4 proj, out ShadowSplitData split);

            var clear = CommandBufferPool.Get("Fengling.MoonShadowClear");
            clear.SetRenderTarget(_moonMap);
            clear.ClearRenderTarget(true, true, new Color(1000f, 0f, 0f, 1f));
            context.ExecuteCommandBuffer(clear);
            CommandBufferPool.Release(clear);

            var setup = CommandBufferPool.Get("Fengling.MoonShadowSetup");
            setup.SetRenderTarget(_moonMap);
            setup.SetViewport(new Rect(0f, 0f, MoonSize, MoonSize));
            setup.SetViewProjectionMatrices(view, proj);
            context.ExecuteCommandBuffer(setup);
            CommandBufferPool.Release(setup);

            var settings = new ShadowDrawingSettings(culling, lightIndex)
            {
                splitData = split,
            };
            var list = context.CreateShadowRendererList(ref settings);
            var draw = CommandBufferPool.Get("Fengling.MoonShadowDraw");
            draw.DrawRendererList(list);
            context.ExecuteCommandBuffer(draw);
            CommandBufferPool.Release(draw);

            var restore = CommandBufferPool.Get("Fengling.MoonShadowRestore");
            restore.SetRenderTarget(restoreTarget, restoreDepthTarget);
            context.ExecuteCommandBuffer(restore);
            CommandBufferPool.Release(restore);
            context.SetupCameraProperties(camera);

            Shader.SetGlobalTexture("_FenglingMoonShadowMap", _moonMap);
            Shader.SetGlobalMatrix("_FenglingMoonShadowView", view);
            Shader.SetGlobalMatrix("_FenglingMoonShadowProj", proj);
            Shader.SetGlobalFloat("_FenglingMoonShadowTexel", 1f / MoonSize);
            Shader.SetGlobalVector("_FenglingMoonShadowSphere", split.cullingSphere);
            Shader.SetGlobalFloat("_FenglingMoonShadowEnabled", 1f);
        }
    }
}
