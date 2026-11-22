Shader "Custom/OKLabQuantize"
{
    // t3ssel8r 原理的「颜色问题」解法：
    //   在 OKLab 感知均匀色彩空间里量化亮度（L 通道），
    //   让色阶之间看起来等距，避免 RGB 量化导致的偏色/发脏。
    //
    // 参考：Björn Ottosson "A perceptual color space for image processing"
    //      Red Giraffe "The Secret to Crisp 3D Pixel Art Rendering"（引用 t3ssel8r）
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Steps ("Luminance Steps", Range(2, 32)) = 6
        _Dither ("Dither Strength", Range(0,1)) = 0.0
        _SaturationBoost ("Saturation Boost", Range(0.5, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "OKLabQuantize"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _Steps;
                float _Dither;
                float _SaturationBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // ---- OKLab 转换（Björn Ottosson）----
            // 输入/输出均为线性 RGB
            float3 LinearRGBToOKLab(float3 c)
            {
                float l = 0.4122214708f*c.r + 0.5363325363f*c.g + 0.0514459929f*c.b;
                float m = 0.2119034982f*c.r + 0.6806995451f*c.g + 0.1073969566f*c.b;
                float s = 0.0883024619f*c.r + 0.2817188376f*c.g + 0.6299787005f*c.b;

                float l_ = pow(abs(l) + 1e-8, 1.0/3.0) * sign(l);
                float m_ = pow(abs(m) + 1e-8, 1.0/3.0) * sign(m);
                float s_ = pow(abs(s) + 1e-8, 1.0/3.0) * sign(s);

                return float3(
                    0.2104542553f*l_ + 0.7936177850f*m_ - 0.0040720468f*s_,
                    1.9779984951f*l_ - 2.4285922050f*m_ + 0.4505937099f*s_,
                    0.0259040371f*l_ + 0.7827717662f*m_ - 0.8086757660f*s_);
            }

            float3 OKLabToLinearRGB(float3 c)
            {
                float l_ = c.x + 0.3963377774f*c.y + 0.2158037573f*c.z;
                float m_ = c.x - 0.1055613458f*c.y - 0.0638541728f*c.z;
                float s_ = c.x - 0.0894841775f*c.y - 1.2914855480f*c.z;

                float l = l_*l_*l_;
                float m = m_*m_*m_;
                float s = s_*s_*s_;

                return float3(
                     4.0767416621f*l - 3.3077115913f*m + 0.2309699292f*s,
                    -1.2684380046f*l + 2.6097574011f*m - 0.3413193965f*s,
                    -0.0041960863f*l - 0.7034186147f*m + 1.7076147010f*s);
            }

            // 简单的 4x4 Bayer 抖动矩阵（可选）
            float Bayer4x4(float2 p)
            {
                float2 f = floor(p);
                int x = (int)fmod(f.x, 4.0);
                int y = (int)fmod(f.y, 4.0);
                const float m[16] = {
                     0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                    12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                     3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                    15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
                };
                return m[y * 4 + x];
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 采样源（RT 是线性空间）
                half3 src = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb;

                // 源已经是线性（RT 未做 sRGB 转换时）
                float3 lin = max(src, 0.0);

                // 线性 RGB → OKLab
                float3 lab = LinearRGBToOKLab(lin);

                // ★ 只量化亮度 L，保留色度（a,b）→ 树冠绿色色相不偏
                float steps = max(2.0, _Steps);
                float dither = 0.0;
                if (_Dither > 0.001)
                    dither = (Bayer4x4(IN.positionHCS.xy) - 0.5) * (_Dither / steps);

                lab.x = floor(lab.x * steps + 0.5 + dither) / steps;

                // 可选：提升饱和度（量化后容易显灰）
                lab.yz *= _SaturationBoost;

                // OKLab → 线性 RGB
                float3 outLin = OKLabToLinearRGB(lab);
                outLin = max(outLin, 0.0);

                return half4(outLin, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
