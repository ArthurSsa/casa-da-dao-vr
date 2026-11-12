// Brilho aditivo dos portais. _Radial = 1: anel no chão (UV radial). _Radial = 0: feixe que some para cima (UV.y).
// Sem LightMode tag: funciona no URP (SRPDefaultUnlit) e no Built-in, com single-pass/multiview do Quest.
Shader "DAO/Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 1, 0.7, 1)
        _Radial ("Radial", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Radial;

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float a;
                if (_Radial > 0.5)
                {
                    float d = length(i.uv - 0.5) * 2.0;
                    float ring = saturate(1.0 - abs(d - 0.85) / 0.12);
                    float fill = saturate(1.0 - d) * 0.35;
                    a = max(ring, fill) * step(d, 1.0);
                }
                else
                {
                    float f = saturate(1.0 - i.uv.y);
                    a = f * f * 0.7;
                }
                return fixed4(_Color.rgb, a * _Color.a);
            }
            ENDCG
        }
    }
}
