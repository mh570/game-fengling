Shader "Custom/GodRay"
{
    // 体积光面片：平行于光线、面向相机的薄片，加法叠加
    // 参考视频 03：「绘制平行于光线且垂直于视角的面片，并进行叠加」
    Properties
    {
        _Color ("Color", Color) = (1.0, 0.95, 0.80, 1.0)
        _Intensity ("Intensity", Range(0, 4)) = 1.0
        _EdgeSoftness ("Edge Softness", Range(0.01, 1.0)) = 0.45
        _TopFade ("Top Fade", Range(0.0, 1.0)) = 0.15
        _BottomFade ("Bottom Fade", Range(0.0, 1.0)) = 0.55
        _NoiseTex ("Noise", 2D) = "white" {}
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.35
        _NoiseScale ("Noise Scale", Float) = 1.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Blend SrcAlpha One          // 加法叠加
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GodRay"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            sampler2D _NoiseTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _EdgeSoftness;
                float _TopFade;
                float _BottomFade;
                float4 _NoiseTex_ST;
                float _NoiseStrength;
                float _NoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                // 绕 Y 轴朝向相机（柱面 billboard）：薄片始终侧对镜头
                float3 camRight = UNITY_MATRIX_IT_MV[0].xyz;   // 相机右方向（世界空间）
                float3 toCam = _WorldSpaceCameraPos - positionWS;
                toCam.y = 0;
                float len = length(toCam);
                toCam = len > 1e-4 ? toCam / len : float3(0, 0, 1);

                float3 right = normalize(cross(float3(0, 1, 0), toCam));
                float3 up = float3(0, 1, 0);

                // 用物体空间的 X 表示横向偏移、Y 表示高度
                float3 offset = right * IN.positionOS.x + up * IN.positionOS.y;

                positionWS += offset;

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _NoiseTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 横向：中心亮，边缘淡出
                float dx = abs(IN.uv.x - 0.5) * 2.0;
                float edge = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, dx);

                // 纵向：底部淡入、顶部淡出
                float vy = IN.uv.y;
                float bottom = smoothstep(0.0, max(0.001, _BottomFade), vy);
                float top = 1.0 - smoothstep(1.0 - max(0.001, _TopFade), 1.0, vy);
                float vert = bottom * top;

                // 噪声打碎光柱，避免死板
                float2 nuv = IN.uv * _NoiseScale;
                float n = tex2D(_NoiseTex, nuv).r;
                float noiseMask = lerp(1.0, n, _NoiseStrength);

                float alpha = edge * vert * noiseMask * _Color.a * _Intensity * IN.color.a;

                return half4(_Color.rgb * IN.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
