Shader "Fengling/Mesh Rain"
{
    Properties
    {
        _Color ("Color", Color) = (0.7, 0.8, 0.95, 0.85)
        _Rain ("Rain", Range(0, 1)) = 0
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
            float _Rain;

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
            };

            v2f vert(appdata v)
            {
                v2f o;
                float seed = v.color.r;
                float3 p = v.vertex.xyz;
                float fall = frac(seed + _Time.y * (0.35 + seed * 0.2));
                p.y = 20.0 - fall * 20.0;

                float3 world = mul(unity_ObjectToWorld, float4(p, 1.0));
                float3 camRight = float3(UNITY_MATRIX_V[0][0], UNITY_MATRIX_V[1][0], UNITY_MATRIX_V[2][0]);
                world += camRight * (v.uv.x - 0.5) * 0.045;
                world += float3(0.0, (v.uv.y - 0.5) * 1.1, 0.0);

                o.pos = UnityWorldToClipPos(world);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float alpha = _Color.a * saturate(_Rain) * i.color.a;
                return float4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
