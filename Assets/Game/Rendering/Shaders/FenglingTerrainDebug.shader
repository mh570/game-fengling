Shader "Fengling/TerrainDebug"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.5, 0.6, 0.4, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "FenglingForward" "RenderType" = "Opaque" "Queue" = "Geometry" }
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _BaseColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float worldY : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                // 最小路径：只做常规 MVP，不加任何 instancing 处理
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldY = mul(unity_ObjectToWorld, v.vertex).y;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 简单的按高度分色：低=沙  中=草  高=岩
                float3 sand  = float3(0.79, 0.70, 0.58);
                float3 grass = float3(0.50, 0.63, 0.35);
                float3 rock  = float3(0.43, 0.42, 0.48);
                float3 c = lerp(sand, grass, saturate((i.worldY + 50.0) / 120.0));
                c = lerp(c, rock, saturate((i.worldY - 150.0) / 120.0));
                return float4(c * _BaseColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
