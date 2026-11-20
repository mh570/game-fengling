Shader "Fengling/Blob Shadow"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 0.38)
        _Softness ("Softness", Range(0.01, 1)) = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float d = saturate(length(p) * 2.0);
                float alpha = smoothstep(1.0, 1.0 - max(0.01, _Softness), d);
                return float4(_Color.rgb, _Color.a * alpha);
            }
            ENDHLSL
        }
    }
}
