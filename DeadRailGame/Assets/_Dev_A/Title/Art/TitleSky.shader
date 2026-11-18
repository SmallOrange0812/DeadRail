Shader "DeadRail/Title/Reference Sky"
{
    Properties
    {
        _ViewRight ("Reference camera right", Vector) = (1,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            float4 _ViewRight;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = mul((float3x3)unity_ObjectToWorld, input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float height = direction.y;
                float3 ground = float3(107, 102, 102) / 255.0;
                float3 horizon = float3(192, 205, 207) / 255.0;
                float3 lowerSky = float3(137, 158, 181) / 255.0;
                float3 middleSky = float3(99, 122, 155) / 255.0;
                float3 upperSky = float3(85, 106, 139) / 255.0;
                float3 zenith = float3(77, 97, 128) / 255.0;

                float3 color;
                if (height < 0.012)
                    color = lerp(ground, horizon, smoothstep(-0.065, 0.012, height));
                else if (height < 0.18)
                    color = lerp(horizon, lowerSky, saturate((height - 0.012) / 0.168));
                else if (height < 0.38)
                    color = lerp(lowerSky, middleSky, saturate((height - 0.18) / 0.20));
                else if (height < 0.60)
                    color = lerp(middleSky, upperSky, saturate((height - 0.38) / 0.22));
                else
                    color = lerp(upperSky, zenith, saturate((height - 0.60) / 0.40));

                float side = saturate(-dot(direction, _ViewRight.xyz));
                color += float3(32, 36, 38) / 255.0 * side * smoothstep(0.05, 0.4, height);
                #ifndef UNITY_COLORSPACE_GAMMA
                    color = SRGBToLinear(color);
                #endif
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
