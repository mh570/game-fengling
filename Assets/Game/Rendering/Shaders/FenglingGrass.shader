Shader "Fengling/Grass"
{
    // 草：billboard 面片 + 顶点色 + 风摆。
    // 用 GPU Instancing 一次画几万株（对应视频里"地上铺满草"的密度）。
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.42, 0.60, 0.28, 1)
        _TipColor  ("Tip Color",  Color) = (0.62, 0.78, 0.40, 1)
        _WindStrength ("Wind", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "RenderPipeline" = "FenglingForward" "RenderType" = "Opaque" "Queue" = "AlphaTest" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags { "LightMode" = "FenglingForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _BaseColor;
            float4 _TipColor;
            float _WindStrength;

            float3 _FenglingSunDir;
            float3 _FenglingSunColor;
            float3 _FenglingAmbientColor;
            float _FenglingAmbientScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                // 实例矩阵
                float4x4 m = unity_ObjectToWorld;
                float3 origin = mul(m, float4(0, 0, 0, 1)).xyz;

                float objHeight = max(0.001, length(mul(m, float4(0, 1, 0, 0)).xyz));

                // billboard：在相机空间构造面向相机的基
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 up = float3(0, 1, 0);

                // 从实例矩阵里取出缩放（否则每株都是 1x1 米）
                float sx = length(mul(m, float4(1, 0, 0, 0)).xyz);
                float sy = length(mul(m, float4(0, 1, 0, 0)).xyz);

                // 风摆：按世界位置和时间扰动
                float wind = sin(_Time.y * 1.7 + origin.x * 0.7 + origin.z * 0.9)
                           * _WindStrength * v.uv.y;
                float3 offset = camRight * v.vertex.x * sx + up * v.vertex.y * sy;
                offset.x += wind * sy;

                float3 world = origin + offset;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.uv = v.uv;
                o.worldPos = float4(world, 1.0);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 从根部到叶尖的颜色渐变
                float3 c = lerp(_BaseColor.rgb, _TipColor.rgb, saturate(i.uv.y));

                // 半兰伯特 + 环境
                float ndl = saturate(dot(float3(0, 1, 0), normalize(-_FenglingSunDir)));
                float wrap = ndl * 0.6 + 0.4;
                float3 lit = c * (_FenglingSunColor.rgb * wrap + _FenglingAmbientColor.rgb * _FenglingAmbientScale);
                lit = saturate(lit);
                return float4(lit, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
