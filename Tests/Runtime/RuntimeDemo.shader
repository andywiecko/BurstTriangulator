Shader "Hidden/BurstTriangulator/RuntimeDemo"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f IN, uint primID : SV_PrimitiveID) : SV_Target
            {
                // A crude random value based on the primitive ID
                float red = frac((float)primID * 0.34563456);
                float green = frac((float)primID * 0.85446);
                float blue = frac((float)primID * 0.212345);
                return float4(red, green, blue, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}