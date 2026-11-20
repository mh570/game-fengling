Shader "Fengling/Mesh Snow"
{
    Properties
    {
        _Color ("Color", Color) = (0.92, 0.96, 1, 0.9)
        _Snow ("Snow", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Snow;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float seed = v.color.r;
                float3 p = v.vertex.xyz;
                float fall = frac(seed + _Time.y * (0.035 + seed * 0.03));
                p.y = 20.0 - fall * 20.0;

                float3 world = mul(unity_ObjectToWorld, float4(p, 1.0));
                float3 camRight = float3(UNITY_MATRIX_V[0][0], UNITY_MATRIX_V[1][0], UNITY_MATRIX_V[2][0]);
                float3 camUp = float3(UNITY_MATRIX_V[0][1], UNITY_MATRIX_V[1][1], UNITY_MATRIX_V[2][1]);
                float size = 0.12 + seed * 0.1;
                world += camRight * (v.uv.x - 0.5) * size;
                world += camUp * (v.uv.y - 0.5) * size;
                world.x += sin(_Time.y * 0.5 + seed * 9.0) * 0.5;
                world.z += cos(_Time.y * 0.4 + seed * 7.0) * 0.4;

                o.worldPos = world;
                o.pos = UnityWorldToClipPos(world);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float shape = smoothstep(0.5, 0.05, length(p));
                float dist = distance(_WorldSpaceCameraPos, i.worldPos);
                float fade = smoothstep(90.0, 8.0, dist);
                float alpha = _Color.a * saturate(_Snow) * i.color.a * shape * fade;
                return float4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
