Shader "Custom/NormalLine"
{
    // 对应工程的 NormalLIne.shadergraph
    // 作用：对 _NormalTex 做邻域法线差异检测，差异大的地方判为边缘 → 描边
    // 输入：_NormalTex（NormalTex.shader 生成的全局纹理）
    Properties
    {
        _Edge ("Edge Threshold", Range(0,1)) = 0.0
        _LineColor ("Line Color", Color) = (0,0,0,1)
        _Width ("Width (pixels)", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "NormalLine"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NormalTex);
            SAMPLER(sampler_NormalTex);
            float4 _NormalTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _Edge;
                float4 _LineColor;
                float _Width;
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

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float2 texel = _NormalTex_TexelSize.xy * max(1.0, _Width);

                half3 c = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv).rgb;
                half3 nU = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv + float2(0, texel.y)).rgb;
                half3 nD = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv - float2(0, texel.y)).rgb;
                half3 nL = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv - float2(texel.x, 0)).rgb;
                half3 nR = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv + float2(texel.x, 0)).rgb;

                // 方向性对比（工程风格化管线的做法）：取四个方向差异的最大值
                float dVert = abs(length(nU - nD));
                float dHorz = abs(length(nL - nR));
                float edge = max(dVert, dHorz);

                // _Edge 越大越容易出线
                float thr = lerp(0.35, 0.02, saturate(_Edge));
                float line = smoothstep(thr, thr * 1.6 + 1e-5, edge);

                // 背景（法线为 0,0,0 → 编码后 0.5,0.5,0.5）不打线
                float isBg = 1.0 - step(0.02, abs(length(c - 0.5)));
                line *= isBg;

                half3 outCol = lerp(c, _LineColor.rgb, line);
                return half4(outCol, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
