Shader "Scene/WindSwaySprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _WindStrength ("摆动幅度(局部单位)", Float) = 0.06
        _BendStart ("弯曲起始高度(0树冠以下全固定)", Range(0, 0.95)) = 0.4
        _WindSpeed ("摆动频率", Float) = 1.6
        _WindPhaseGap ("树间相位差", Float) = 0.45
        _GustStrength ("阵风强度", Range(0, 1)) = 0.35
        _GustSpeed ("阵风速度", Float) = 0.5

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _MainTex_ST;

            half _WindStrength;
            half _BendStart;
            half _WindSpeed;
            half _WindPhaseGap;
            half _GustStrength;
            half _GustSpeed;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                // 相位由世界坐标决定:每棵树相位天然错开,无需逐实例脚本或材质参数,不影响合批
                float phase = dot(worldPos.xy, float2(_WindPhaseGap, _WindPhaseGap * 0.73));
                // 阵风:慢速波沿 x 方向扫过树林,调制摆动幅度
                float gust = 1.0 + _GustStrength * sin(_Time.y * _GustSpeed - worldPos.x * 0.35);
                // 弯曲权重:低于 _BendStart 的部分(树干/底座)固定,往上平滑过渡到树顶满幅
                float bend = smoothstep(_BendStart, 1.0, v.texcoord.y);

                v.vertex.x += sin(_Time.y * _WindSpeed + phase) * _WindStrength * gust * bend;

                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                c.rgb *= c.a;
                return c;
            }
        ENDCG
        }
    }
    Fallback "Sprites/Default"
}
