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
        _WaveCount ("Wave Count", Int) = 0
        _WaveDirs ("Wave Directions", Vector) = (1, 0, 0, 0)
        _WaveParams ("Wave Params (amp, k, omega, phase)", Vector) = (0.5, 0.2, 1, 0)
        _AmplitudeScale ("Amplitude Scale", Float) = 1
        _WaveNoise ("Wave Irregularity", Range(0, 1)) = 0.35

        [Header(Time)]
        _WaterTime ("Water Time", Float) = 0

        [Header(Toggles)]
        [ToggleUI] _UseReflectionCube ("Use Reflection Cube", Float) = 1
        [ToggleUI] _UseFoam ("Use Foam", Float) = 1
        _CameraUnderwater ("Camera Underwater", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "HDRenderPipeline"
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

            float4 _FoamColor;
            float _FoamDistance;
            float _FoamStrength;
            float _FoamWarp;

            sampler2D _HeightMap;
            float4 _HeightMapBounds;
            float4 _HeightRange;

            int _WaveCount;
            float4 _WaveDirs[MAX_WAVES];
            float4 _WaveParams[MAX_WAVES];
            float _AmplitudeScale;
            float _WaveNoise;

            float _WaterTime;
            float _UseReflectionCube;
            float _UseFoam;
            float _CameraUnderwater;

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
            float TerrainDepth(float3 worldPos)
            {
                float2 center = _HeightMapBounds.xy;
                float2 size = max(_HeightMapBounds.zw, 1e-4);
                float2 uv = (worldPos.xz - (center - size * 0.5)) / size;
                float h = tex2Dlod(_HeightMap, float4(saturate(uv), 0, 0)).r;
                float terrainY = lerp(_HeightRange.x, _HeightRange.y, h);
                return max(0.0, worldPos.y - terrainY);
            }

            // ---------- Gerstner（与 CPU 侧同一公式） ----------
            void EvaluateWaves(float2 xz, out float height, out float2 slope)
            {
                height = 0;
                slope = 0;

                // 低频噪声调制振幅：破掉"所有波同时到峰"的规则感
                float irregular = 1.0 + _WaveNoise * (valueNoise(xz * 0.03) - 0.5) * 2.0;

                int count = min(_WaveCount, MAX_WAVES);
                for (int i = 0; i < count; i++)
                {
                    float4 dir = _WaveDirs[i];
                    float4 p = _WaveParams[i];
                    float amp = p.x * _AmplitudeScale * irregular;
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
                EvaluateWaves(worldPos.xz, height, slope);
                worldPos.y += height;

                o.worldPos = worldPos;
                o.uv = v.uv;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
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
                float2 uv1 = worldXZ * (_NormalTiling1 / 10.0) + _NormalSpeed1.xy * _WaterTime;
                float2 uv2 = worldXZ * (_NormalTiling2 / 10.0) + _NormalSpeed2.xy * _WaterTime;
                float3 n1 = UnpackNormal(tex2D(_NormalMap1, uv1));
                float3 n2 = UnpackNormal(tex2D(_NormalMap2, uv2));
                // whiteout blend：直接相加会让 Z 超 1，归一化后变平
                return normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
            }

            float4 frag(v2f i) : SV_Target
            {
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

                // ---- 深度（决定颜色和透明度）
                float depth = TerrainDepth(i.worldPos);
                float transmittance = exp(-depth / max(0.01, _AbsorptionDistance));
                float3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, transmittance);

                // ---- 菲涅尔 + 环境反射
                float NoV = saturate(dot(N, V));
                float fresnel = _F0 + (1.0 - _F0) * pow(1.0 - NoV, 5.0);
                float3 reflectDir = reflect(-V, N);

                float3 envColor;
                if (_UseReflectionCube > 0.5)
                    envColor = DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflectDir), unity_SpecCube0_HDR);
                else
                    envColor = ShadeSH9(float4(reflectDir, 1.0));

                float3 color = lerp(waterColor, envColor, fresnel);

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
                color += _SunColor.rgb * spec * _SunIntensity;

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
                float foam = (depthMask * (0.35 + 0.65 * foamNoise) + coreLine * 0.55 + crest * 0.15 * foamNoise)
                             * _FoamStrength * _UseFoam;

                color = lerp(color, _FoamColor.rgb, saturate(foam));

                // ---- 透明度：浅处几乎全透（看得见沙底），深处不透明
                float alpha = saturate(depth / max(0.01, _FadeDistance));
                alpha = max(alpha, saturate(foam));        // 泡沫不透明
                alpha = max(alpha, fresnel * 0.9);         // 掠射角反光不透明
                alpha = saturate(alpha + 0.06);            // 保底一点存在感

                // ---- 水下整体偏青
                color = lerp(color, color * float3(0.35, 0.70, 0.90), _CameraUnderwater * 0.5);

                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
