Shader "DeadRail/Environment/Distant City"
{
    Properties
    {
        _HazeColor("Night horizon haze", Color) = (0.14901961,0.18039216,0.24705882,1)
        _HazeStart("Haze start", Float) = 140
        _HazeEnd("Haze end", Float) = 950
        _FadeStart("Far fade start", Float) = 740
        _FadeEnd("Far fade end", Float) = 930
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Name "DistantCity"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 color:TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                float _HazeStart;
                float _HazeEnd;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS=p.positionCS;output.positionWS=p.positionWS;
                float3 normal=TransformObjectToWorldNormal(input.normalOS);
                float shade=0.72+0.28*saturate(dot(normal,normalize(float3(-.4,.7,-.6))));
                half3 color=input.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    color=SRGBToLinear(color);
                #endif
                output.color=color*shade;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float distanceXZ=length(input.positionWS.xz-_WorldSpaceCameraPos.xz);
                // Fade geometry before the camera far plane instead of clipping a whole skyline abruptly.
                float coverage=1.0-smoothstep(_FadeStart,_FadeEnd,distanceXZ);
                float haze=smoothstep(_HazeStart,_HazeEnd,distanceXZ);
                float groundHaze=1-smoothstep(-3,27,input.positionWS.y);
                haze=max(haze,groundHaze*.92);
                half3 color=lerp(input.color,_HazeColor.rgb,min(.94,haze));
                return half4(color,coverage);
            }
            ENDHLSL
        }
    }
}
