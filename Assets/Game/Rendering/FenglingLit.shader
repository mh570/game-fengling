// 极简 forward Lit：漫反射 + 主光 + SH 环境光 + 轻微高光。
// 走自建管线 FenglingPipeline 的 "FenglingForward" 通道。
// 目的：替掉 HDRP/Lit，让地形/石头/鱼在新管线下不变粉。

Shader "Fengling/Lit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.75, 0.72, 0.65, 1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _Metallic ("Metallic", Range(0, 1)) = 0
        _AmbientBoost ("Ambient Boost", Range(0, 3)) = 1
        _AmbientFloor ("Ambient Floor", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "FenglingForward"
            Tags { "LightMode" = "FenglingForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _BaseColor;
            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float _Smoothness;
            float _Metallic;
            float _AmbientBoost;
            float _AmbientFloor;
            float4 _FenglingAmbientColor;
            float _FenglingAmbientScale;
            float4 _FenglingFogColor;
            float _FenglingFogDensity;

            // 由 FenglingPipeline 每帧写入
            float4 _FenglingSunDir;
            float4 _FenglingSunColor;
            float4 _FenglingFillDir;
            float4 _FenglingFillColor;

            sampler2D _FenglingShadowAtlas;
            float4x4 _FenglingShadowView0;
            float4x4 _FenglingShadowView1;
            float4x4 _FenglingShadowView2;
            float4x4 _FenglingShadowView3;
            float4x4 _FenglingShadowVP0;
            float4x4 _FenglingShadowVP1;
            float4x4 _FenglingShadowVP2;
            float4x4 _FenglingShadowVP3;
            float4 _FenglingShadowSpheres[4];
            float4 _FenglingShadowSplits;
            float4 _FenglingShadowTile0;
            float4 _FenglingShadowTile1;
            float4 _FenglingShadowTile2;
            float4 _FenglingShadowTile3;
            float _FenglingShadowTexel;
            float _FenglingShadowNormalBias;
            float _FenglingShadowSoftness;
            float _FenglingShadowEnabled;
            float _FenglingShadowStrength;
            float _FenglingCloudShadowStrength;
            float _FenglingCloudShadowScale;
            float4 _FenglingCloudWind;

            sampler2D _FenglingMoonShadowMap;
            float4x4 _FenglingMoonShadowView;
            float4x4 _FenglingMoonShadowProj;
            float _FenglingMoonShadowTexel;
            float _FenglingMoonShadowEnabled;
            float _FenglingMoonShadowStrength;
            float4 _FenglingMoonShadowSphere;
            float4 _FenglingMoonDir;
            float4 _FenglingMoonColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.pos = UnityWorldToClipPos(o.worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            float CloudShadowNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float CloudShadowFactor(float2 worldXZ)
            {
                if (_FenglingCloudShadowStrength <= 0.001) return 1.0;
                float2 p = worldXZ * _FenglingCloudShadowScale +
                           _FenglingCloudWind.xy * (_Time.y * 0.01);
                float n = CloudShadowNoise(p) * 0.65;
                n += CloudShadowNoise(p * 2.03) * 0.35;
                float cloud = smoothstep(0.44, 0.68, n);
                return 1.0 - cloud * _FenglingCloudShadowStrength;
            }

            float SampleCascade(sampler2D atlas, float4x4 vp, float4x4 view, float4 tile,
                                float3 worldPos, float bias, float texelSize)
            {
                float4 c = mul(vp, float4(worldPos, 1.0));
                c.xyz /= c.w;
                float2 uvLocal = c.xy * 0.5 + 0.5;
                if (any(uvLocal < 0.001) || any(uvLocal > 0.999)) return 1.0;
                float2 uv = uvLocal * tile.xy + tile.zw;

                float surfaceDepth = -mul(view, float4(worldPos, 1.0)).z;
                float2 t = float2(texelSize, texelSize) * max(0.25, _FenglingShadowSoftness);
                float lit = 0;
                // 5x5 PCF：比 4-tap 更接近 URP 的软阴影边缘。
                for (int y = -2; y <= 2; y++)
                {
                    for (int x = -2; x <= 2; x++)
                    {
                        float2 offset = float2(x * t.x, y * t.y);
                        lit += tex2D(atlas, uv + offset).r > surfaceDepth - bias ? 1 : 0;
                    }
                }
                return lit / 25.0;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNormal);
                float3 L = normalize(_FenglingSunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                float3 albedo = _BaseColor.rgb * tex2D(_BaseMap, i.uv).rgb;

                // 漫反射
                float ndl = saturate(dot(N, L));
                float3 diffuse = albedo * _FenglingSunColor.rgb * ndl;

                // 很弱的填充光：只负责把逆光面从纯黑拉起来
                float3 fillL = normalize(_FenglingFillDir.xyz);
                float fillNdl = saturate(dot(N, fillL));
                diffuse += albedo * _FenglingFillColor.rgb * fillNdl;

                // 环境光（球谐，来自天空盒）。加一个很小的保底，避免逆光面纯黑。
                // 自建 SRP 的 Skybox SH 不会随时间自动刷新；时间系统用全局 scale/color 驱动。
                float3 ambientSH = ShadeSH9(float4(N, 1.0));
                float3 ambient = ambientSH * _FenglingAmbientScale * albedo * _AmbientBoost
                               + _FenglingAmbientColor.rgb * albedo;
                ambient = max(ambient, albedo * _AmbientFloor);

                // Blinn-Phong 高光
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), exp2(lerp(1.0, 10.0, _Smoothness)));
                float3 specular = _FenglingSunColor.rgb * spec * _Smoothness * (1.0 - _Metallic);

                // 阴影（4 级级联，按 culling sphere 选级）
                float shadow = 1.0;
                if (_FenglingShadowEnabled > 0.5)
                {
                    float texel = max(0.00001, _FenglingShadowTexel);
                    float camDepth = -mul(UNITY_MATRIX_V, float4(i.worldPos, 1.0)).z;
                    // 沿法线偏移采样点，减轻斜面 acne（normal bias）。
                    float3 shadowPos = i.worldPos + N * _FenglingShadowNormalBias;
                    int cascade = 3;
                    for (int ci = 0; ci < 4; ci++)
                    {
                        float4 sphere = _FenglingShadowSpheres[ci];
                        if (distance(i.worldPos, sphere.xyz) < sphere.w)
                        {
                            cascade = ci;
                            break;
                        }
                    }

                    float shadowNext = 1.0;
                    if (cascade == 0)
                    {
                        shadow = SampleCascade(_FenglingShadowAtlas, _FenglingShadowVP0, _FenglingShadowView0, _FenglingShadowTile0, shadowPos, 0.08, texel);
                        shadowNext = SampleCascade(_FenglingShadowAtlas, _FenglingShadowVP1, _FenglingShadowView1, _FenglingShadowTile1, shadowPos, 0.08, texel);
                    }
                    else if (cascade == 1)
                    {
                        shadow = SampleCascade(_FenglingShadowAtlas, _FenglingShadowVP1, _FenglingShadowView1, _FenglingShadowTile1, shadowPos, 0.08, texel);
                        shadowNext = SampleCascade(_FenglingShadowAtlas, _FenglingShadowVP2, _FenglingShadowView2, _FenglingShadowTile2, shadowPos, 0.08, texel);
                    }
                    else if (cascade == 2)
                    {
                        shadow = SampleCascade(_FenglingShadowAtlas, _FenglingShadowVP2, _FenglingShadowView2, _FenglingShadowTile2, shadowPos, 0.08, texel);
                        shadowNext = SampleCascade(_FenglingShadowAtlas, _FenglingShadowVP3, _FenglingShadowView3, _FenglingShadowTile3, shadowPos, 0.08, texel);
                    }
                    else
                    {
                        shadow = SampleCascade(_FenglingShadowAtlas, _FenglingShadowVP3, _FenglingShadowView3, _FenglingShadowTile3, shadowPos, 0.08, texel);
                    }

                    // 级联交界混合 + 最远距离淡出，避免一条硬切边。
                    float split = cascade == 0 ? _FenglingShadowSplits.x :
                                  cascade == 1 ? _FenglingShadowSplits.y :
                                  cascade == 2 ? _FenglingShadowSplits.z : _FenglingShadowSplits.w;
                    float blend = saturate((camDepth - split * 0.9) / max(0.001, split * 0.1));
                    shadow = lerp(shadow, shadowNext, blend * 0.5);
                    float farFade = saturate((camDepth - _FenglingShadowSplits.w * 0.9) /
                                             max(0.001, _FenglingShadowSplits.w * 0.1));
                    shadow = lerp(shadow, 1.0, farFade);
                }

                // 低太阳时淡化阴影强度，避免影子拉成一条长矛。
                float shadowStrength = _FenglingShadowStrength;
                if (shadowStrength <= 0.001) shadowStrength = 1.0;
                shadow = lerp(1.0, shadow, saturate(shadowStrength));
                shadow *= CloudShadowFactor(i.worldPos.xz);

                // 月亮冷色漫反射 + moon shadow（只在 culling sphere 内采样）。
                float3 Lmoon = normalize(_FenglingMoonDir.xyz);
                float moonShadow = 1.0;
                if (_FenglingMoonShadowEnabled > 0.5 &&
                    distance(i.worldPos, _FenglingMoonShadowSphere.xyz) < _FenglingMoonShadowSphere.w)
                {
                    float4x4 moonVp = mul(_FenglingMoonShadowProj, _FenglingMoonShadowView);
                    moonShadow = SampleCascade(_FenglingMoonShadowMap, moonVp, _FenglingMoonShadowView,
                                               float4(1.0, 1.0, 0.0, 0.0),
                                               i.worldPos, 0.25, max(0.00001, _FenglingMoonShadowTexel));
                }
                moonShadow = lerp(1.0, moonShadow, saturate(_FenglingMoonShadowStrength));
                float3 moonDiffuse = albedo * _FenglingMoonColor.rgb *
                                     saturate(dot(N, Lmoon)) * moonShadow;

                float3 color = (diffuse + specular) * shadow + moonDiffuse + ambient;
                float fogDist = distance(_WorldSpaceCameraPos, i.worldPos);
                float fogFactor = 1.0 - exp(-max(0.0, _FenglingFogDensity) * fogDist);
                color = lerp(color, _FenglingFogColor.rgb, saturate(fogFactor));
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float depth : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(worldPos);
                o.depth = -mul(UNITY_MATRIX_V, float4(worldPos, 1.0)).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(i.depth, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float depth : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(worldPos);
                o.depth = -mul(UNITY_MATRIX_V, float4(worldPos, 1.0)).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(i.depth, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
