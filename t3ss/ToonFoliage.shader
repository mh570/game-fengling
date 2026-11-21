Shader "Custom/ToonFoliage"
{
    // 叶片卡片专用：Toon 分档 + alpha 裁剪 + 投影
    // ShadowCaster 的写法参照本仓库的 GrassBlade.shader（CBUFFER + sampler2D + Shadows.hlsl）
    Properties
    {
        _MainTex ("Leaf Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.4
        _BaseColor ("Base Color", Color) = (0.30, 0.50, 0.22, 1)
        _HighlightColor ("Highlight", Color) = (0.78, 0.92, 0.54, 1)
        _ShadowColor ("Shadow", Color) = (0.24, 0.40, 0.22, 1)
        _Cuts ("Cuts", Range(1,32)) = 16
        _DitherStrength ("Dither", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On

        // ---------------- 主光照 ----------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            sampler2D _MainTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                half4 _BaseColor;
                half4 _HighlightColor;
                half4 _ShadowColor;
                float _Cuts;
                float _DitherStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float4 color       : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = n.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            float Bayer4(float2 pos)
            {
                int2 p = int2(fmod(pos, 4.0));
                float b[16] = { 0,8,2,10, 12,4,14,6, 3,11,1,9, 15,7,13,5 };
                return b[p.y * 4 + p.x] / 16.0;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = tex2D(_MainTex, IN.uv);
                clip(tex.a - _Cutoff);

                float3 N = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float ndl = dot(N, mainLight.direction);
                float lit = saturate(ndl * 0.5 + 0.5);
                lit = pow(lit, 0.85) * 1.15;   // 提高主光对比

                // 叶片用平滑着色：量化会在卡片尺度上产生棋盘跳变
                // （参考视频的树冠是连续渐变，没有色带）
                float q = saturate(lit);

                float3 col = lerp(_ShadowColor.rgb, _HighlightColor.rgb, q);
                col *= _BaseColor.rgb * tex.rgb * IN.color.rgb;   // 顶点色（树干/叶片区分）

                // 环境光（天空光）：否则背光叶片全黑，整体被拉暗
                float3 ambient = SampleSH(N) * 0.12;   // 大幅降低
                col += ambient * _BaseColor.rgb * IN.color.rgb * tex.rgb;

                return half4(col, 1);
            }
            ENDHLSL
        }

        // ---------------- 投影（树影） ----------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            sampler2D _MainTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                half4 _BaseColor;
                half4 _HighlightColor;
                half4 _ShadowColor;
                float _Cuts;
                float _DitherStrength;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // 朝光源方向偏移，避免自阴影条纹
                positionWS = ApplyShadowBias(positionWS, normalWS, _LightDirection);

                float4 positionHCS = TransformWorldToHClip(positionWS);
                #if UNITY_REVERSED_Z
                    positionHCS.z = min(positionHCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionHCS.z = max(positionHCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionHCS = positionHCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                clip(tex2D(_MainTex, IN.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
