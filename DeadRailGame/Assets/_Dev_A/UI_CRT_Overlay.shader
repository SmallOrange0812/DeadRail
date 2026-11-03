Shader "Custom/UI_CRT_Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.1, 1, 0.2, 0.15)
        _ScanlineCount ("Scanline Count (주사선 개수)", Range(50, 800)) = 300
        _ScanlineSpeed ("Scanline Speed (흐르는 속도)", Range(0, 10)) = 2
        _ScanlineAlpha ("Scanline Intensity (주사선 진하기)", Range(0, 1)) = 0.35
        _VignetteIntensity ("Vignette Intensity (테두리 어두움)", Range(0, 5)) = 1.8
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _ScanlineCount;
            float _ScanlineSpeed;
            float _ScanlineAlpha;
            float _VignetteIntensity;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 1. 위아래로 흐르는 가로 주사선(Scanline) 계산
                float scanline = sin((uv.y * _ScanlineCount) - (_Time.y * _ScanlineSpeed)) * 0.5 + 0.5;
                float lineAlpha = (1.0 - scanline) * _ScanlineAlpha;

                // 2. 브라운관 모니터 특유의 모서리 어두워짐(Vignette) 계산
                float2 center = uv - 0.5;
                float vignette = dot(center, center) * _VignetteIntensity;
                vignette = saturate(vignette);

                // 3. 미세한 화면 깜빡임(Flicker) 효과
                float flicker = sin(_Time.y * 60.0) * 0.015;

                // 최종 색상 및 투명도 합성 (검은색 주사선 + 테두리 그림자 + 기본 틴트)
                fixed4 col = IN.color;
                col.rgb = lerp(col.rgb, fixed3(0, 0, 0), saturate(vignette + lineAlpha));
                col.a = saturate(IN.color.a + lineAlpha + vignette * 0.8 + flicker);

                return col;
            }
            ENDCG
        }
    }
}