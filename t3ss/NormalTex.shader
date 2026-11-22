Shader "Custom/NormalTex"
{
    // 对应工程的 NormalTex.shadergraph
    // 作用：把世界空间法线编码成颜色，写入 _NormalTex（供描边 Pass 做边缘检测）
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass
        {
            Name "NormalTex"
            Tags { "LightMode"="DepthOnly" }   // 必须匹配 ShaderTagId("DepthOnly")

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 法线编码到 0..1
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
