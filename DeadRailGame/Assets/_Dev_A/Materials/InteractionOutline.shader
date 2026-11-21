Shader "DeadRail/Interaction Outline"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (0.22, 1.65, 2.5, 1)
        _OutlinePixels ("Outline Width", Range(1, 8)) = 2.35
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-30" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlinePixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                float4 position = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 normalCS = mul(GetWorldToHClipMatrix(), float4(normalWS, 0.0));
                float2 direction = (normalCS.xy * position.w - position.xy * normalCS.w) * _ScaledScreenParams.xy;
                float2 unitDirection = direction * rsqrt(max(dot(direction, direction), 1e-8));
                position.xy += unitDirection * (_OutlinePixels * 2.0 / _ScaledScreenParams.xy) * position.w;
                return position;
            }

            half4 Frag() : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
