Shader "Hidden/BurstTriangulator/AlphaShapeDemo"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Wire Color", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Name "BASE"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_base
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag_base(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }

        Pass
        {
            Name "WIREFRAME"
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag_wire
            #pragma target 4.0
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2g
            {
                float4 pos : POSITION;
            };

            struct g2f
            {
                float4 pos : SV_POSITION;
            };

            v2g vert(appdata v)
            {
                v2g o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            [maxvertexcount(6)]
            void geom(triangle v2g input[3], inout LineStream<g2f> outputStream)
            {
                for (int i = 0; i < 3; ++i)
                {
                    g2f o1, o2;
                    o1.pos = input[i].pos;
                    o2.pos = input[(i + 1) % 3].pos;
                    outputStream.Append(o1);
                    outputStream.Append(o2);
                }
            }

            fixed4 frag_wire(g2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}