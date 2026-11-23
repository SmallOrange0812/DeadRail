Shader "DeadRail/UI/Depot Blur"
{
    Properties { _MainTex ("Depot", 2D) = "white" {} _Radius ("Blur radius", Float) = 4 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            sampler2D _MainTex; float4 _MainTex_TexelSize; float _Radius;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float2 d=_MainTex_TexelSize.xy*_Radius*0.5;
                const float weights[5]={1,4,6,4,1};
                fixed3 c=0;
                [unroll] for (int x=0;x<5;x++)
                    [unroll] for (int y=0;y<5;y++)
                        c+=tex2D(_MainTex,i.uv+float2(x-2,y-2)*d).rgb*weights[x]*weights[y];
                return fixed4(c/256.0,1);
            }
            ENDCG
        }
    }
}
