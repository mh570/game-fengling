// 自建 SRP 的后处理：曝光 → Bloom → ACES → 对比度/饱和度。
// 三个 pass 共用这个 shader：
//   0 Composite：场景 + Bloom → 最终画面
//   1 Bright：提取高光
//   2 Blur：单向高斯模糊（水平/垂直各一次）
Shader "Fengling/PostProcess"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloomTex;
            float _FenglingExposure;
            float _FenglingBloomIntensity;
            float _FenglingContrast;
            float _FenglingSaturation;

            sampler2D _FenglingSceneDepth;
            float4x4 _FenglingInvProj;
            float4x4 _FenglingCameraToWorld;
            float4 _FenglingCameraPos;
            float4 _FenglingSunDir;
            float4 _FenglingSunColor;
            float _FenglingVolumetricDensity;
            float _FenglingGodRayDebug;
            float4 _FenglingSunScreenPos;
            float _FenglingGodRayIntensity;
            float _FenglingGodRayThreshold;

            sampler2D _FenglingShadowAtlas;
            float4x4 _FenglingShadowView0;
            float4x4 _FenglingShadowView1;
            float4x4 _FenglingShadowView2;
            float4x4 _FenglingShadowView3;
            float4x4 _FenglingShadowVP0;
            float4x4 _FenglingShadowVP1;
            float4x4 _FenglingShadowVP2;
            float4x4 _FenglingShadowVP3;
            float4 _FenglingShadowTile0;
            float4 _FenglingShadowTile1;
            float4 _FenglingShadowTile2;
            float4 _FenglingShadowTile3;
            float4 _FenglingShadowSplits;

            float Hash12(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float SampleShadowCascade(float4x4 vp, float4x4 view, float4 tile, float3 worldPos)
            {
                float4 sc = mul(vp, float4(worldPos, 1.0));
                sc.xyz /= sc.w;
                float2 uv = sc.xy * 0.5 + 0.5;
                if (any(uv < 0.001) || any(uv > 0.999)) return 1.0;
                uv = uv * tile.xy + tile.zw;
                float surface = -mul(view, float4(worldPos, 1.0)).z;
                float stored = tex2D(_FenglingShadowAtlas, uv).r;
                return stored > surface - 0.15 ? 1.0 : 0.0;
            }

            float SampleSunShadow(float3 worldPos)
            {
                float dist = distance(_FenglingCameraPos.xyz, worldPos);
                if (dist < _FenglingShadowSplits.x) return SampleShadowCascade(_FenglingShadowVP0, _FenglingShadowView0, _FenglingShadowTile0, worldPos);
                if (dist < _FenglingShadowSplits.y) return SampleShadowCascade(_FenglingShadowVP1, _FenglingShadowView1, _FenglingShadowTile1, worldPos);
                if (dist < _FenglingShadowSplits.z) return SampleShadowCascade(_FenglingShadowVP2, _FenglingShadowView2, _FenglingShadowTile2, worldPos);
                if (dist < _FenglingShadowSplits.w) return SampleShadowCascade(_FenglingShadowVP3, _FenglingShadowView3, _FenglingShadowTile3, worldPos);
                return 1.0;
            }

            float3 ScreenSpaceGodRays(float2 uv)
            {
                if (_FenglingSunScreenPos.z < 0.5 || _FenglingGodRayIntensity <= 0.001) return 0;
                float2 delta = (_FenglingSunScreenPos.xy - uv) / 32.0;
                float2 coord = uv;
                float3 acc = 0;
                float weight = 0;
                for (int i = 0; i < 32; i++)
                {
                    coord += delta;
                    float3 c = tex2D(_MainTex, coord).rgb;
                    float luma = dot(c, float3(0.2126, 0.7152, 0.0722));
                    float w = smoothstep(_FenglingGodRayThreshold, _FenglingGodRayThreshold + 0.25, luma);
                    acc += c * w;
                    weight += w;
                }
                return acc / max(weight, 1.0) * _FenglingGodRayIntensity;
            }

            float3 VolumetricLight(float2 uv)
            {
                if (_FenglingVolumetricDensity <= 0.0001) return 0;

                float depth = tex2D(_FenglingSceneDepth, uv).r;
                if (depth <= 0.0 || depth > 5000.0) depth = 90.0;
                depth = min(depth, 90.0);

                float2 ndc = uv * 2.0 - 1.0;
                float4 clip = float4(ndc, 0.5, 1.0);
                float4 view = mul(_FenglingInvProj, clip);
                view.xyz /= view.w;
                float3 dir = normalize(mul((float3x3)_FenglingCameraToWorld, normalize(view.xyz)));

                float3 sunDir = normalize(_FenglingSunDir.xyz);
                if (sunDir.y <= 0.0) return 0;

                if (_FenglingGodRayDebug > 0.5)
                {
                    float3 dbgP = _FenglingCameraPos.xyz + dir * min(depth, 25.0);
                    float s = SampleSunShadow(dbgP);
                    return float3(s, 0.0, 1.0 - s);
                }

                const int STEPS = 18;
                float stepLen = depth / STEPS;
                float jitter = Hash12(uv * 713.0 + _Time.y * 0.37);
                float transmittance = 1.0;
                float3 acc = 0;

                for (int i = 0; i < STEPS; i++)
                {
                    float t = (i + jitter) * stepLen;
                    float3 p = _FenglingCameraPos.xyz + dir * t;
                    float heightDensity = 0.55 + 0.45 * exp(-max(0.0, p.y) * 0.035);
                    float density = _FenglingVolumetricDensity * heightDensity;
                    float shadow = SampleSunShadow(p);
                    float cosTheta = saturate(dot(dir, sunDir));
                    float phase = 0.35 + 0.65 * pow(cosTheta, 6.0);
                    float3 inscatter = _FenglingSunColor.rgb * (density * stepLen * shadow * phase);
                    acc += transmittance * inscatter;
                    transmittance *= exp(-density * stepLen);
                }

                return acc * 0.5;
            }

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 ACESFilm(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 scene = tex2D(_MainTex, i.uv).rgb;
                float3 bloom = tex2D(_BloomTex, i.uv).rgb;

                scene += VolumetricLight(i.uv);
                scene += ScreenSpaceGodRays(i.uv);
                scene += bloom * _FenglingBloomIntensity;
                scene *= exp2(_FenglingExposure);
                scene = ACESFilm(scene);

                float luma = dot(scene, float3(0.2126, 0.7152, 0.0722));
                scene = lerp(luma.xxx, scene, _FenglingSaturation);
                scene = saturate((scene - 0.5) * _FenglingContrast + 0.5);

                return float4(scene, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Bright"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FenglingBloomThreshold;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float brightness = max(c.r, max(c.g, c.b));
                float contribution = max(0.0, brightness - _FenglingBloomThreshold);
                c *= contribution / max(brightness, 1e-4);
                return float4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _FenglingBlurTexel;
            float4 _FenglingBlurDir;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 offset = _FenglingBlurTexel.xy * _FenglingBlurDir.xy;
                float3 c = tex2D(_MainTex, i.uv).rgb * 0.227027;
                c += tex2D(_MainTex, i.uv + offset * 1.384615).rgb * 0.316216;
                c += tex2D(_MainTex, i.uv - offset * 1.384615).rgb * 0.316216;
                c += tex2D(_MainTex, i.uv + offset * 3.230769).rgb * 0.070270;
                c += tex2D(_MainTex, i.uv - offset * 3.230769).rgb * 0.070270;
                return float4(c, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
