// 自包含水面 shader（不依赖 HDRP 光照管线，用 SRPDefaultUnlit 通道渲染）
//
// 为什么不用 HDRP 的 Lit/WaterSystem：
//   1. HDRP 的 lit pass 需要一整套 SurfaceData/LightLoop 接口，改起来牵扯太多
//   2. 我们要和 CPU 的 Gerstner 完全对齐，自己算最可控
//   3. HDRP 官方明确保留了 SRPDefaultUnlit 作为 legacy unlit 的回退通道
//
// 光照模型：太阳 GGX 高光 + 菲涅尔 + 球谐环境色（近似反射）
// 深度信息：不读深度缓冲，改采样地形高度图（见 CoastTerrain.BuildHeightMap）

Shader "Fengling/Water Surface"
{
    Properties
    {
        [Header(Normals)]
        _NormalMap1 ("Normal Map 1", 2D) = "bump" {}
        _NormalMap2 ("Normal Map 2", 2D) = "bump" {}
        _NormalTiling1 ("Tiling 1 (per 100m)", Float) = 8
        _NormalTiling2 ("Tiling 2 (per 100m)", Float) = 24
        _NormalSpeed1 ("Scroll 1 (xy)", Vector) = (0.02, 0.011, 0, 0)
        _NormalSpeed2 ("Scroll 2 (xy)", Vector) = (-0.013, 0.021, 0, 0)
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1

        [Header(Sun)]
        _SunDirection ("Sun Direction (world)", Vector) = (0.3, 0.8, 0.5, 0)
        _SunColor ("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SunIntensity ("Sun Intensity", Float) = 2
        _Roughness ("Roughness", Range(0.01, 0.5)) = 0.06
        _F0 ("Fresnel F0", Range(0, 0.2)) = 0.02

        [Header(Water Color)]
        _ShallowColor ("Shallow Color", Color) = (0.05, 0.42, 0.45, 1)
        _DeepColor ("Deep Color", Color) = (0.01, 0.06, 0.12, 1)
        _AbsorptionDistance ("Absorption Distance (m)", Float) = 5
        _WaterLevel ("Water Level Y", Float) = 0

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Depth (m)", Float) = 0.6
        _FoamStrength ("Foam Strength", Range(0, 2)) = 1

        [Header(Terrain Height Map)]
        _HeightMap ("Terrain Height Map", 2D) = "black" {}
        _HeightMapBounds ("Bounds (cx, cz, sizeX, sizeZ)", Vector) = (0, 0, 200, 200)
        _HeightRange ("Height Range (min, max)", Vector) = (-9, 4, 0, 0)

        [Header(Waves)]
        _WaveCount ("Wave Count", Int) = 4
        _WaveDirs ("Wave Directions", Vector) = (1, 0, 0, 0)
        _WaveParams ("Wave Params (amp, k, omega, phase)", Vector) = (0.5, 0.2, 1, 0)
        _AmplitudeScale ("Amplitude Scale", Float) = 1

        [Header(Time)]
        _WaterTime ("Water Time", Float) = 0

        [Header(Toggles)]
        [ToggleUI] _UseReflectionCube ("Use Reflection Cube", Float) = 1
        [ToggleUI] _UseFoam ("Use Foam", Float) = 1
        _CameraUnderwater ("Camera Underwater", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // 双面渲染：从水下往上看也要能看到水面
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define MAX_WAVES 8
            #define PI 3.14159265359

            sampler2D _NormalMap1;
            sampler2D _NormalMap2;
            float _NormalTiling1;
            float _NormalTiling2;
            float4 _NormalSpeed1;
            float4 _NormalSpeed2;
            float _NormalStrength;

            float4 _SunDirection;
            float4 _SunColor;
            float _SunIntensity;
            float _Roughness;
            float _F0;

            float4 _ShallowColor;
            float4 _DeepColor;
            float _AbsorptionDistance;
            float _WaterLevel;

            float4 _FoamColor;
            float _FoamDistance;
            float _FoamStrength;

            sampler2D _HeightMap;
            float4 _HeightMapBounds;
            float4 _HeightRange;

            int _WaveCount;
            float4 _WaveDirs[MAX_WAVES];
            float4 _WaveParams[MAX_WAVES];
            float _AmplitudeScale;

            float _WaterTime;

            float _UseReflectionCube;
            float _UseFoam;
            float _CameraUnderwater;
            float _MaxWaveHeight;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            // Gerstner 高度 + 解析导数（与 CPU 的 GerstnerWaterSimulation 保持同一公式）
            void EvaluateWaves(float2 xz, out float height, out float2 slope)
            {
                height = 0;
                slope = 0;
                int count = min(_WaveCount, MAX_WAVES);
                for (int i = 0; i < count; i++)
                {
                    float4 dir = _WaveDirs[i];
                    float4 p = _WaveParams[i];          // amp, k, omega, phase
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
                EvaluateWaves(worldPos.xz, height, slope);

                worldPos.y += height;

                o.worldPos = worldPos;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                // 高度场 y = h(x,z) 的法线 = normalize(-∂h/∂x, 1, -∂h/∂z)
                o.normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
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

            // 用两张法线做 whiteout blend（直接相加会让 Z 超 1，归一化后变平）
            float3 SampleWaterNormal(float2 worldXZ)
            {
                float2 uv1 = worldXZ * (_NormalTiling1 / 100.0) + _NormalSpeed1.xy * _WaterTime;
                float2 uv2 = worldXZ * (_NormalTiling2 / 100.0) + _NormalSpeed2.xy * _WaterTime;

                float3 n1 = UnpackNormal(tex2D(_NormalMap1, uv1));
                float3 n2 = UnpackNormal(tex2D(_NormalMap2, uv2));

                float3 blended = normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
                blended.xy *= _NormalStrength;
                return normalize(blended);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 Ngeo = normalize(i.normalWS);

                // ---- 法线贴图：切线空间 → 世界空间（水面基本水平，用世界轴近似即可）
                float3 tN = SampleWaterNormal(i.worldPos.xz);
                float3 N = normalize(Ngeo + float3(tN.x, 0.0, tN.y));

                // ---- 地形深度（采样烘焙高度图，不依赖深度缓冲）
                float2 boundsCenter = _HeightMapBounds.xy;
                float2 boundsSize = _HeightMapBounds.zw;
                float2 huv = (i.worldPos.xz - (boundsCenter - boundsSize * 0.5)) / max(boundsSize, 1e-4);
                float hNormalized = tex2D(_HeightMap, saturate(huv)).r;
                float terrainY = lerp(_HeightRange.x, _HeightRange.y, hNormalized);
                float depth = max(0.0, i.worldPos.y - terrainY);

                // ---- 水体颜色：Beer-Lambert 吸收
                float transmittance = exp(-depth / max(0.01, _AbsorptionDistance));
                float3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, transmittance);

                // ---- 菲涅尔 + 环境反射
                float NoV = saturate(dot(N, V));
                float fresnel = _F0 + (1.0 - _F0) * pow(1.0 - NoV, 5.0);
                float3 reflectDir = reflect(-V, N);

                // 优先用反射探针的立方体贴图（真实天空），退化时用球谐
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

                float a = max(0.002, _Roughness * _Roughness);
                float D = DistributionGGX(NoH, a);
                float3 F = FresnelSchlick(VoH, float3(_F0, _F0, _F0));
                float spec = D * F.x * NoL / max(4.0 * NoV * NoL, 1e-4);
                color += _SunColor.rgb * spec * _SunIntensity;

                // ---- 泡沫：岸线（深度差）+ 浪尖（高度）两路
                float depthFoam = 1.0 - saturate(depth / max(0.01, _FoamDistance));
                depthFoam *= depthFoam;

                float2 foamUvA = i.worldPos.xz * 0.35 + _WaterTime * 0.05;
                float2 foamUvB = i.worldPos.xz * 0.80 - _WaterTime * 0.09;
                float noiseA = tex2D(_NormalMap2, foamUvA).r;
                float noiseB = tex2D(_NormalMap1, foamUvB).r;

                float crest = saturate((i.worldPos.y - _WaterLevel) / max(0.05, _MaxWaveHeight));
                float foam = depthFoam * (0.55 + 0.45 * noiseA)
                           + crest * 0.35 * noiseB;
                foam *= _FoamStrength * _UseFoam;

                color = lerp(color, _FoamColor.rgb, saturate(foam));

                // ---- 相机在水下时整体偏青，模拟水体吸收
                color = lerp(color, color * float3(0.35, 0.70, 0.90), _CameraUnderwater * 0.5);

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
