Shader "DeadRail/UI/Selection Artwork"
{
    Properties
    {
        [PerRendererData] _MainTex ("Artwork", 2D) = "white" {}
        _Selected ("Selected", Float) = 0
        _Card ("Keep portrait neutral", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "False"
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float _Selected;
            float _Card;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 artwork = tex2D(_MainTex, input.uv);
                float3 color = artwork.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                color = LinearToGammaSpace(color);
                #endif

                float shade = dot(color, float3(0.2126, 0.7152, 0.0722));
                float redShare = (color.r - max(color.g, color.b)) / max(color.r, 0.08);
                float existingRed = smoothstep(0.22, 0.54, redShare);
                float3 neutral = lerp(color, shade * float3(0.86, 0.89, 0.92), existingRed);
                float lightInk = smoothstep(0.38, 0.60, shade);
                float portrait = step(0.24, input.uv.x) * step(input.uv.x, 0.76)
                    * step(0.25, input.uv.y) * step(input.uv.y, 0.73);
                float preserve = max(existingRed, max(lightInk, _Card * portrait * smoothstep(0.22, 0.40, shade)));
                float3 accent = saturate(float3(0.27 + shade, 0.025 + shade * 0.29, 0.02 + shade * 0.22));
                color = lerp(neutral, lerp(accent, color, preserve), _Selected);

                #ifndef UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(color);
                #endif
                return fixed4(color, artwork.a) * input.color;
            }
            ENDCG
        }
    }
}
