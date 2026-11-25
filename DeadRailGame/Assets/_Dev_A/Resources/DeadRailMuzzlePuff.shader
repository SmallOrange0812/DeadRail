Shader "DeadRail/Muzzle puff"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Vertex { float4 position : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            Output vert(Vertex v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv * 2 - 1;
                o.color = v.color;
                return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float radius = length(i.uv);
                float edge = .82 + .12 * sin(atan2(i.uv.y, i.uv.x) * 5);
                return fixed4(i.color.rgb, i.color.a * (1 - smoothstep(.15, edge, radius)));
            }
            ENDCG
        }
    }
}
