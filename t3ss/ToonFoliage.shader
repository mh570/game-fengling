Shader "Custom/ToonFoliage"
{
    // 叶片卡片专用：Toon 分档 + alpha 裁剪
    // 基于 t3ssel8r 管线的 ToonLit 思路，去掉 patch/云影，专供树叶

    Properties
    {
        _MainTex ("Leaf Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.4
        _BaseColor ("Base Color", Color) = (0.30, 0.50, 0.22, 1)
        _HighlightColor ("Highlight", Color) = (0.62, 0.80, 0.40, 1)
        _ShadowColor ("Shadow", Color) = (0.16, 0.28, 0.16, 1)
        _Cuts ("Cuts", Range(1,8)) = 3
        _DitherStrength ("Dither", Range(0,1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float _Cutoff;
            float4 _BaseColor, _HighlightColor, _ShadowColor;
            float _Cuts, _DitherStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = n.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            // 4x4 Bayer 抖动
            float Bayer4(float2 uv)
            {
                int2 p = int2(fmod(uv, 4.0));
                float bayer[16] = {
                     0,  8,  2, 10,
                    12,  4, 14,  6,
                     3, 11,  1,  9,
                    15,  7, 13,  5
                };
                return bayer[p.y * 4 + p.x] / 16.0;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a - _Cutoff);

                float3 N = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float ndl = dot(N, mainLight.direction);
                float lit = saturate(ndl * 0.5 + 0.5);

                // 卡通量化 + Bayer 抖动打散色带
                float steps = max(1.0, _Cuts);
                float q = floor(lit * steps) / steps;
                float dither = Bayer4(IN.positionCS.xy) * _DitherStrength;
                q = saturate(q + (dither - 0.5 * _DitherStrength) / steps);

                float3 col = lerp(_ShadowColor.rgb, _HighlightColor.rgb, q);
                col *= _BaseColor.rgb * tex.rgb * mainLight.color;

                return half4(col, 1);
            }
            ENDHLSL
        }

    }

    FallBack Off
}
