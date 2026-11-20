using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 方向光 4 级级联阴影：4 张独立 RFloat 线性深度图。
    /// 级联比例/裁剪球按 Catlike/URP 的 ComputeDirectionalShadowMatricesAndCullingPrimitives 路径。
    /// </summary>
    public sealed class FenglingShadows
    {
        public const int CascadeCount = 4;
        private static readonly Vector3 CascadeRatios = new Vector3(0.1f, 0.25f, 0.5f);

        private readonly RenderTexture[] _maps = new RenderTexture[CascadeCount];
        private readonly Matrix4x4[] _views = new Matrix4x4[CascadeCount];
        private readonly Matrix4x4[] _vps = new Matrix4x4[CascadeCount];
        private readonly Vector4[] _spheres = new Vector4[CascadeCount];
        private readonly float[] _splits = new float[CascadeCount];

        private RenderTexture _moonMap;
        private Matrix4x4 _moonView;
        private Matrix4x4 _moonProj;
        private Vector4 _moonSphere;
        private Light _moon;
        private Renderer _terrainCaster;

        private bool _hasRendered;
        private Vector3 _lastLightDir;
        private Vector3 _lastCameraPos;
        private Vector3 _lastCameraForward;
        private float _lastDistance;
        private int _lastResolution;

        public int Resolution = 1024;
        public bool enableMoonShadows = false;

        private void EnsureMap(int index, int size)
        {
            if (_maps[index] != null && _maps[index].width == size) return;
            if (_maps[index] != null)
            {
                _maps[index].Release();
                Object.DestroyImmediate(_maps[index]);
            }
            _maps[index] = new RenderTexture(size, size, 24, RenderTextureFormat.RFloat)
            {
                name = "FenglingShadowCascade" + index,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _maps[index].Create();
        }

        private void EnsureMoonMap(int size)
        {
            size = Mathf.Max(256, size / 2);
            if (_moonMap != null && _moonMap.width == size) return;
            if (_moonMap != null)
            {
                _moonMap.Release();
                Object.DestroyImmediate(_moonMap);
            }
            _moonMap = new RenderTexture(size, size, 24, RenderTextureFormat.RFloat)
            {
                name = "FenglingMoonShadow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _moonMap.Create();
        }

        public void Render(ScriptableRenderContext context, Camera camera, CullingResults culling,
                           Vector3 lightDirection, float distance, int resolution,
                           RenderTargetIdentifier restoreTarget, RenderTargetIdentifier restoreDepthTarget)
        {
            Resolution = Mathf.Clamp(resolution, 256, 2048);
            for (int i = 0; i < CascadeCount; i++) EnsureMap(i, Resolution);

            // 按需更新：太阳方向/相机位姿/阴影距离基本不变时复用缓存。
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

            for (int i = 0; i < CascadeCount; i++)
            {
                culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    lightIndex, i, CascadeCount, CascadeRatios, Resolution, 0.1f,
                    out Matrix4x4 view, out Matrix4x4 proj, out ShadowSplitData splitData);

                _views[i] = view;
                _vps[i] = proj * view;
                _spheres[i] = splitData.cullingSphere;
                _splits[i] = distance * (i + 1f) / CascadeCount;

                var setup = CommandBufferPool.Get("Fengling.ShadowCascade" + i);
                setup.SetRenderTarget(_maps[i]);
                setup.ClearRenderTarget(true, true, new Color(1000f, 0f, 0f, 1f));
                setup.SetViewProjectionMatrices(view, proj);
                context.ExecuteCommandBuffer(setup);
                CommandBufferPool.Release(setup);

                var shadowSettings = new ShadowDrawingSettings(culling, lightIndex)
                {
                    splitData = splitData,
                };
                var shadowList = context.CreateShadowRendererList(ref shadowSettings);
                var draw = CommandBufferPool.Get("Fengling.ShadowDraw" + i);
                draw.DrawRendererList(shadowList);
                context.ExecuteCommandBuffer(draw);
                CommandBufferPool.Release(draw);
            }

            // 月亮单独一张低分辨率阴影图（单级，不参与太阳级联）。
            if (_moon == null)
            {
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l != null && l.name == "Moon Light")
                    {
                        _moon = l;
                        break;
                    }
                }
            }
            if (_moon != null && enableMoonShadows && _moon.intensity > 0.01f)
            {
                int moonIndex = -1;
                var moonVisible = culling.visibleLights;
                for (int i = 0; i < moonVisible.Length; i++)
                {
                    if (moonVisible[i].lightType == LightType.Directional && moonVisible[i].light == _moon)
                    {
                        moonIndex = i;
                        break;
                    }
                }
                if (moonIndex < 0)
                {
                    Shader.SetGlobalFloat("_FenglingMoonShadowEnabled", 0f);
                    Shader.SetGlobalColor("_FenglingMoonColor", _moon.color * (_moon.intensity * 0.12f));
                    goto afterMoon;
                }

                EnsureMoonMap(Resolution);
                int moonSize = _moonMap.width;
                culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    moonIndex, 0, 1, Vector3.zero, moonSize, 0f,
                    out _moonView, out _moonProj, out ShadowSplitData moonSplit);
                _moonSphere = moonSplit.cullingSphere;

                var moonCmd = CommandBufferPool.Get("Fengling.MoonShadow");                moonCmd.SetRenderTarget(_moonMap);
                moonCmd.ClearRenderTarget(true, true, new Color(1000f, 0f, 0f, 1f));
                moonCmd.SetViewProjectionMatrices(_moonView, _moonProj);
                context.ExecuteCommandBuffer(moonCmd);
                CommandBufferPool.Release(moonCmd);

                var moonSettings = new ShadowDrawingSettings(culling, moonIndex)
                {
                    splitData = moonSplit,
                };
                if (_terrainCaster == null)
                {
                    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (r.name.Contains("CoastTerrain"))
                        {
                            _terrainCaster = r;
                            break;
                        }
                    }
                }
                var previousCasting = _terrainCaster != null
                    ? _terrainCaster.shadowCastingMode
                    : ShadowCastingMode.On;
                if (_terrainCaster != null) _terrainCaster.shadowCastingMode = ShadowCastingMode.Off;

                var moonList = context.CreateShadowRendererList(ref moonSettings);
                var moonDraw = CommandBufferPool.Get("Fengling.MoonShadowDraw");
                moonDraw.DrawRendererList(moonList);
                context.ExecuteCommandBuffer(moonDraw);
                CommandBufferPool.Release(moonDraw);

                if (_terrainCaster != null) _terrainCaster.shadowCastingMode = previousCasting;

                Shader.SetGlobalTexture("_FenglingMoonShadowMap", _moonMap);
                Shader.SetGlobalMatrix("_FenglingMoonShadowView", _moonView);
                Shader.SetGlobalMatrix("_FenglingMoonShadowProj", _moonProj);
                Shader.SetGlobalFloat("_FenglingMoonShadowTexel", 1f / moonSize);
                Shader.SetGlobalVector("_FenglingMoonShadowSphere", _moonSphere);
                Shader.SetGlobalFloat("_FenglingMoonShadowEnabled", 1f);
                Shader.SetGlobalVector("_FenglingMoonDir", -_moon.transform.forward);
                Shader.SetGlobalColor("_FenglingMoonColor", _moon.color * (_moon.intensity * 0.12f));
            }
            else
            {
                Shader.SetGlobalFloat("_FenglingMoonShadowEnabled", 0f);
                Shader.SetGlobalColor("_FenglingMoonColor", Color.black);
            }
            afterMoon:
            var restore = CommandBufferPool.Get("Fengling.ShadowRestore");
            restore.SetRenderTarget(restoreTarget, restoreDepthTarget);
            context.ExecuteCommandBuffer(restore);
            CommandBufferPool.Release(restore);
            context.SetupCameraProperties(camera);

            for (int i = 0; i < CascadeCount; i++)
            {
                Shader.SetGlobalTexture("_FenglingShadowMap" + i, _maps[i]);
                Shader.SetGlobalMatrix("_FenglingShadowView" + i, _views[i]);
                Shader.SetGlobalMatrix("_FenglingShadowVP" + i, _vps[i]);
            }
            Shader.SetGlobalVectorArray("_FenglingShadowSpheres", _spheres);
            Shader.SetGlobalVector("_FenglingShadowSplits",
                new Vector4(_splits[0], _splits[1], _splits[2], _splits[3]));
            Shader.SetGlobalFloat("_FenglingShadowTexel", 1f / Resolution);
            Shader.SetGlobalFloat("_FenglingShadowNormalBias",
                Mathf.Max(0.05f, distance / Resolution * 1.5f));
            Shader.SetGlobalFloat("_FenglingShadowSoftness", 1f);
            Shader.SetGlobalFloat("_FenglingShadowEnabled", 1f);

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
        }
    }
}
