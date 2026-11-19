// 自包含水面 shader（HDRP 下走 SRPDefaultUnlit 通道）
//
// 关键设计（对照 GitHub 上的成熟实现）：
//   1. 世界空间 UV       —— 多块水面拼接无接缝（Uber-Stylized-Water 的做法）
//   2. 深度差算泡沫       —— 不是画白边，而是浅的地方自然出现（Roystan/ToonWater）
//   3. 噪声扭曲泡沫坐标   —— 边缘才会碎，而不是刀切一样平滑
//   4. 按深度透明         —— 浅处能看见沙底，深处才不透明
//
// 不依赖 HDRP 的 Lit/LightLoop，光照自己算，改起来可控。

Shader "Fengling/Water Surface"
{
    Properties
    {
        [Header(Normals)]
        _NormalMap1 ("Normal Map 1", 2D) = "bump" {}
        _NormalMap2 ("Normal Map 2", 2D) = "bump" {}
        _NormalTiling1 ("Tiling 1 (per 10m)", Float) = 8
        _NormalTiling2 ("Tiling 2 (per 10m)", Float) = 24
        _NormalSpeed1 ("Scroll 1 (xy)", Vector) = (0.02, 0.011, 0, 0)
        _NormalSpeed2 ("Scroll 2 (xy)", Vector) = (-0.013, 0.021, 0, 0)
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _NormalFadeStart ("Normal Fade Start (m)", Float) = 25
        _NormalFadeRange ("Normal Fade Range (m)", Float) = 120

        [Header(Sun)]
        _SunDirection ("Sun Direction (world)", Vector) = (0.3, 0.8, 0.5, 0)
        _SunColor ("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SunIntensity ("Sun Intensity", Float) = 3
        _Roughness ("Roughness Near", Range(0.01, 0.5)) = 0.05
        _RoughnessFar ("Roughness Far", Range(0.01, 0.6)) = 0.22
        _F0 ("Fresnel F0", Range(0, 0.2)) = 0.02

        [Header(Water Color)]
        _ShallowColor ("Shallow Color", Color) = (0.55, 0.85, 0.85, 1)
        _DeepColor ("Deep Color", Color) = (0.02, 0.18, 0.30, 1)
        _AbsorptionDistance ("Absorption (m)", Float) = 4
        _FadeDistance ("Opaque At Depth (m)", Float) = 6
        _WaterLevel ("Water Level Y", Float) = 0
        _HorizonColor ("Horizon Color", Color) = (0.65, 0.78, 0.90, 1)
        _HorizonFadeStart ("Horizon Fade Start (m)", Float) = 30
        _HorizonFadeRange ("Horizon Fade Range (m)", Float) = 120
        _ShoreColor ("Shore / Wet Sand Color", Color) = (0.55, 0.50, 0.38, 1)

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Depth (m)", Float) = 1.4
        _FoamStrength ("Foam Strength", Range(0, 2)) = 1.6
        _FoamWarp ("Foam Warp (m)", Float) = 2.5

        [Header(Terrain Height Map)]
        _HeightMap ("Terrain Height Map", 2D) = "black" {}
        _HeightMapBounds ("Bounds (cx, cz, sizeX, sizeZ)", Vector) = (0, 0, 200, 200)
        _HeightRange ("Height Range (min, max)", Vector) = (-9, 4, 0, 0)

        [Header(Waves)]
        _AmplitudeScale ("Amplitude Scale", Float) = 1

        [Header(Time)]
        _WaterTime ("Water Time", Float) = 0
        _FlowDir ("Flow Direction (xz)", Vector) = (1, 0, 0, 0)
        _FlowSpeed ("Flow Speed", Float) = 0
        _Vertical ("Vertical Flow (waterfall)", Range(0,1)) = 0

        [Header(Toggles)]
        [ToggleUI] _UseReflectionCube ("Use Reflection Cube", Float) = 1
        [ToggleUI] _UseFoam ("Use Foam", Float) = 1
        _CameraUnderwater ("Camera Underwater", Float) = 0
        _Stylized ("Stylized (0=realistic 1=stylized)", Range(0,1)) = 0
        _OpacityScale ("Stylized Opacity Scale", Range(0,1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define MAX_WAVES 16
            #define PI 3.14159265359

            sampler2D _NormalMap1;
            sampler2D _NormalMap2;
            float _NormalTiling1;
            float _NormalTiling2;
            float4 _NormalSpeed1;
            float4 _NormalSpeed2;
            float _NormalStrength;
            float _NormalFadeStart;
            float _NormalFadeRange;

            float4 _SunDirection;
            float4 _SunColor;
            float _SunIntensity;
            float _Roughness;
            float _RoughnessFar;
            float _F0;

            float4 _ShallowColor;
            float4 _DeepColor;
            float _AbsorptionDistance;
            float _FadeDistance;
            float _WaterLevel;
            float4 _HorizonColor;
            float _HorizonFadeStart;
            float _HorizonFadeRange;
            float4 _ShoreColor;

            float4 _FoamColor;
            float _FoamDistance;
            float _FoamStrength;
            float _FoamWarp;

            sampler2D _HeightMap;
            float4 _HeightMapBounds;
            float4 _HeightRange;

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float _FenglingCameraDepthAvailable;

            int _FenglingWaveCount;
            float4 _FenglingWaveDirs[MAX_WAVES];
            float4 _FenglingWaveParams[MAX_WAVES];
            float _AmplitudeScale;

            float _WaterTime;
            float4 _FlowDir;
            float _FlowSpeed;
            float _Vertical;
            float _UseReflectionCube;
            float _UseFoam;
            float _CameraUnderwater;
            float _Stylized;
            float _OpacityScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float2 uv       : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            // ---------- 噪声 ----------
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // ---------- 地形深度 ----------
            float TerrainHeight(float3 worldPos)
            {
                float2 center = _HeightMapBounds.xy;
                float2 size = max(_HeightMapBounds.zw, 1e-4);
                float2 uv = (worldPos.xz - (center - size * 0.5)) / size;
                float h = tex2Dlod(_HeightMap, float4(saturate(uv), 0, 0)).r;
                return lerp(_HeightRange.x, _HeightRange.y, h);
            }

            float TerrainDepth(float3 worldPos)
            {
                return max(0.0, worldPos.y - TerrainHeight(worldPos));
            }

            // ---------- Gerstner（与 CPU 侧同一公式） ----------
            void EvaluateWaves(float2 xz, out float height, out float2 slope)
            {
                height = 0;
                slope = 0;

                int count = min(_FenglingWaveCount, MAX_WAVES);
                for (int i = 0; i < count; i++)
                {
                    float4 dir = _FenglingWaveDirs[i];
                    float4 p = _FenglingWaveParams[i];
                    float amp = p.x * _AmplitudeScale;
                    float k = p.y;
                    float omega = p.z;
                    float phase = p.w;

                    float theta = k * dot(dir.xy, xz) - omega * _WaterTime + phase;
                    height += amp * sin(theta);
                    slope += amp * k * dir.xy * cos(theta);
                }
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                float height;
                float2 slope;
                if (_Vertical < 0.5)
                {
                    EvaluateWaves(worldPos.xz, height, slope);
                    height *= (1.0 - _Vertical);
                    slope *= (1.0 - _Vertical);
                    worldPos.y += height;
                }

                o.worldPos = worldPos;
                o.uv = v.uv;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float DistributionGGX(float NoH, float a)
            {
                float a2 = a * a;
                float d = NoH * NoH * (a2 - 1.0) + 1.0;
                return a2 / max(PI * d * d, 1e-6);
            }

            float3 FresnelSchlick(float VoH, float3 f0)
            {
                return f0 + (1.0 - f0) * pow(saturate(1.0 - VoH), 5.0);
            }

            float3 SampleDetailNormal(float2 worldXZ)
            {
                worldXZ += _FlowDir.xy * _FlowSpeed * _WaterTime;
                float2 uv1 = worldXZ * (_NormalTiling1 / 10.0) + _NormalSpeed1.xy * _WaterTime;
                float2 uv2 = worldXZ * (_NormalTiling2 / 10.0) + _NormalSpeed2.xy * _WaterTime;
                float3 n1 = UnpackNormal(tex2D(_NormalMap1, uv1));
                float3 n2 = UnpackNormal(tex2D(_NormalMap2, uv2));
                // whiteout blend：直接相加会让 Z 超 1，归一化后变平
                return normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
            }

            // 垂直瀑布：不采样地形深度，用 UV 纵向流动 + 噪声泡沫。
            // 这是 P0 的轻量实现，真正的垂直流体/碰撞飞沫以后再补。
            float4 VerticalWater(v2f i) : SV_Target
            {
                float speed = max(0.2, _FlowSpeed);
                float2 uv1 = float2(i.uv.x * 3.0, i.uv.y * 1.2 + _WaterTime * speed * 1.4);
                float2 uv2 = float2(i.uv.x * 7.0 + 0.13, i.uv.y * 2.0 + _WaterTime * speed * 2.1);

                float3 n1 = UnpackNormal(tex2D(_NormalMap1, uv1));
                float3 n2 = UnpackNormal(tex2D(_NormalMap2, uv2));
                float3 n = normalize(float3(n1.x + n2.x, n1.z * n2.z, n1.y + n2.y));

                float streak = valueNoise(float2(i.uv.x * 18.0, i.uv.y * 3.0 - _WaterTime * speed * 3.0));
                float streak2 = valueNoise(float2(i.uv.x * 46.0 + 13.0, i.uv.y * 7.0 - _WaterTime * speed * 5.0));
                float foam = saturate((streak * 0.65 + streak2 * 0.35 - 0.34) * 2.0);

                float edge = smoothstep(0.0, 0.10, i.uv.x) * smoothstep(1.0, 0.90, i.uv.x);
                float topFade = smoothstep(1.0, 0.72, i.uv.y);
                float3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(i.uv.y * 1.1));
                float3 col = lerp(baseCol, _FoamColor.rgb, foam * 0.9);
                col += n.x * 0.04;

                float alpha = saturate(0.55 + foam * 0.35) * edge * topFade;
                return float4(col, alpha);
            }

            float4 frag(v2f i) : SV_Target
            {
                if (_Vertical > 0.5)
                    return VerticalWater(i);

                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float viewDist = distance(i.worldPos, _WorldSpaceCameraPos);

                // ---- 法线：几何法线（由波推导）+ 细节法线（按距离衰减）
                float height;
                float2 slope;
                EvaluateWaves(i.worldPos.xz, height, slope);
                float3 Ngeo = normalize(float3(-slope.x, 1.0, -slope.y));

                float fade = saturate(1.0 - (viewDist - _NormalFadeStart) / max(1.0, _NormalFadeRange));
                float nStrength = lerp(0.22, 1.0, fade) * _NormalStrength;
                float rough = lerp(_RoughnessFar, _Roughness, fade);

                float3 tN = SampleDetailNormal(i.worldPos.xz);
                float3 N = normalize(Ngeo + float3(tN.x * nStrength, 0.0, tN.y * nStrength));
                if (_Vertical > 0.5)
                    N = normalize(float3(tN.x, 1.0, tN.y));

                // ---- 深度（决定颜色和透明度）
                // 优先用不透明 pass 的真实深度；没有深度纹理时才退回烘焙高度图。
                float terrainY = TerrainHeight(i.worldPos);
                float depth;
                if (_FenglingCameraDepthAvailable > 0.5)
                {
                    float2 screenUV = i.screenPos.xy / i.screenPos.w;
                    float sceneEye = LinearEyeDepth(tex2D(_CameraDepthTexture, screenUV).r);
                    depth = max(0.0, sceneEye - i.screenPos.w);
                }
                else
                {
                    if (terrainY > i.worldPos.y + 0.02)
                        discard;
                    depth = TerrainDepth(i.worldPos);
                }

                // ---- 水线裁切已移除：海平线淡出已改成水平距离，
                // 近岸暗带的根因不再是相机高度；保留完整浅水带。

                // 写实：Beer-Lambert 连续吸收
                float transmittance = exp(-depth / max(0.01, _AbsorptionDistance));
                float3 realColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, transmittance);

                // 风格化：4 档色阶（t3ssel8r 的思路，牺牲连续渐变换可读性）
                float depthT = saturate(depth / max(0.01, _FadeDistance));
                float bands = floor(depthT * 4.0) / 4.0;
                float3 stylColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, bands);

                float3 waterColor = lerp(realColor, stylColor, _Stylized);

                // ---- 菲涅尔 + 环境反射
                float NoV = saturate(dot(N, V));
                float fresnel = _F0 + (1.0 - _F0) * pow(1.0 - NoV, 5.0);
                float3 reflectDir = reflect(-V, N);

                // 自建 SRP 还没有绑定反射探针；unity_SpecCube0 在打包/无探针时不可靠。
                // P0 先用天空 SH 兜底，等平面反射做好后再替换。
                float3 envColor = ShadeSH9(float4(reflectDir, 1.0));

                // 浅水处不要满菲涅尔反射：否则掠射角会把薄薄一层水染成深色带。
                float shoreFactor = saturate(depth / 0.75);
                float3 color = lerp(waterColor, envColor, fresnel * shoreFactor);

                // ---- 太阳高光（GGX）
                float3 L = normalize(_SunDirection.xyz);
                float3 H = normalize(L + V);
                float NoL = saturate(dot(N, L));
                float NoH = saturate(dot(N, H));
                float VoH = saturate(dot(V, H));
                float a = max(0.002, rough * rough);
                float D = DistributionGGX(NoH, a);
                float3 F = FresnelSchlick(VoH, float3(_F0, _F0, _F0));
                float spec = D * F.x * NoL / max(4.0 * NoV * NoL, 1e-4);
                color += _SunColor.rgb * spec * _SunIntensity * (1.0 - _Stylized);

                // ---- 岸边泡沫：深度差 + 噪声扭曲坐标（让边缘碎掉）
                float2 warp = float2(
                    valueNoise(i.worldPos.xz * 0.35 + _WaterTime * 0.05),
                    valueNoise(i.worldPos.xz * 0.35 + 31.7 - _WaterTime * 0.04)) - 0.5;
                float3 warpedPos = i.worldPos + float3(warp.x, 0.0, warp.y) * _FoamWarp;
                float warpedDepth = TerrainDepth(warpedPos);

                float foamWidth = max(0.01, _FoamDistance);
                float depthMask = saturate(1.0 - warpedDepth / foamWidth);
                depthMask *= depthMask;
                float coreLine = saturate(1.0 - depth / (foamWidth * 0.2));

                float foamNoise = valueNoise(i.worldPos.xz * 1.7 + _WaterTime * 0.12);
                foamNoise = saturate((foamNoise - 0.35) * 2.2);

                float crest = saturate((i.worldPos.y - _WaterLevel) / 1.2);
                float realFoam = (depthMask * (0.35 + 0.65 * foamNoise) + coreLine * 0.55 + crest * 0.15 * foamNoise);

                // 风格化：硬阈值切出点状虚线（参考图的岸线就是这种点线）
                float dotNoise = valueNoise(i.worldPos.xz * 6.0 + _WaterTime * 0.08);
                float dotMask = step(0.52, dotNoise);
                float stylFoam = coreLine * dotMask * 1.4 + depthMask * dotMask * 0.5;

                float foam = lerp(realFoam, stylFoam, _Stylized) * _FoamStrength * _UseFoam;

                // 岸线湿沙带：浅水直接混到沙色，而不是让深水色在岸边拉出一条暗带。
                float shoreMask = saturate(1.0 - depth / 1.0);
                color = lerp(color, _ShoreColor.rgb, shoreMask * 0.65);

                color = lerp(color, _FoamColor.rgb, saturate(foam));

                // ---- 透明度：浅处几乎全透（看得见沙底），深处不透明
                float alpha = saturate(depth / max(0.01, _FadeDistance));
                alpha = max(alpha, fresnel * shoreFactor * 0.9);
                alpha = saturate(alpha + 0.04);

                // 岸线：水深趋近 0 时淡出，避免水/沙滩共面处出现黑边。
                alpha *= saturate(depth / 0.5);
                alpha = max(alpha, 0.04 * saturate(depth / 0.5));
                alpha = max(alpha, shoreMask * 0.45);

                // 远处：用水平距离，而不是相机到水面的 3D 距离。
                // 俯视相机离水面很高，用 3D 距离会把整片水都判成“远景”而淡成透明。
                float2 horizonDelta = i.worldPos.xz - _WorldSpaceCameraPos.xz;
                float horizonDist = length(horizonDelta);
                float horizonFade = saturate((horizonDist - _HorizonFadeStart) / max(1.0, _HorizonFadeRange));
                color = lerp(color, _HorizonColor.rgb, horizonFade);
                alpha *= (1.0 - horizonFade);

                // 泡沫不受岸线淡出影响，但受海平线淡出影响。
                alpha = max(alpha, saturate(foam) * (1.0 - horizonFade));

                // 风格化：整体压低不透明度，让水下地形和鱼透出来
                alpha = lerp(alpha, saturate(depth / max(0.01, _FadeDistance)) * _OpacityScale, _Stylized);

                // ---- 水下整体偏青
                color = lerp(color, color * float3(0.35, 0.70, 0.90), _CameraUnderwater * 0.5);

                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
