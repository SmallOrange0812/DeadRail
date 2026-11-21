Shader "DeadRail/Starting Rail Yard/Overcast Sky"
{
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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = mul((float3x3)unity_ObjectToWorld, input.positionOS.xyz);
                return output;
            }

            float Hash(float2 samplePosition)
            {
                return frac(sin(dot(samplePosition, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 samplePosition)
            {
                float2 cell = floor(samplePosition);
                float2 blend = frac(samplePosition);
                blend = blend * blend * (3.0 - 2.0 * blend);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), blend.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1), blend.x), blend.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 ray = normalize(input.direction);
                // Match the scene light at 2.5 degrees of elevation and a yaw of 120 degrees.
                float3 toSun = normalize(float3(-0.86520, 0.04362, 0.49952));
                float solarAlignment = saturate(dot(ray, toSun));
                float sunset = pow(solarAlignment, 4.0);
                float3 haze = float3(101, 106, 128) / 255.0;
                float3 horizon = lerp(float3(140, 133, 159), float3(237, 128, 100), sunset) / 255.0;
                float3 zenith = float3(36, 47, 86) / 255.0;
                float3 color = lerp(haze, horizon, smoothstep(-0.19, 0.025, ray.y));
                color = lerp(color, zenith, smoothstep(0.025, 0.40, ray.y));
                float2 cloudPlane = ray.xz * (2.4 / max(0.15, ray.y + 0.35));
                float cloud = Noise(cloudPlane * float2(1.15, 0.7)) * 0.65 + Noise(cloudPlane * 2.35) * 0.35;
                color += (cloud - 0.5) * 0.18 * smoothstep(0.005, 0.18, ray.y);
                color += float3(0.15, 0.075, 0.035) * pow(solarAlignment, 80.0);
                float sunDisc = smoothstep(0.99992, 0.99995, solarAlignment);
                color = lerp(color, float3(1.0, 0.77, 0.50), sunDisc);
                #ifndef UNITY_COLORSPACE_GAMMA
                    color = SRGBToLinear(color);
                #endif
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
