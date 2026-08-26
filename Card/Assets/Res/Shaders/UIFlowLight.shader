Shader "UI/FlowLight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [HDR] _ShineColor ("Shine Color", Color) = (1, 1, 1, 1)
        _ShineIntensity ("Shine Intensity", Range(0, 4)) = 1.4
        _ShineWidth ("Shine Width", Range(0.01, 0.5)) = 0.12
        _ShineAngle ("Shine Angle", Range(-180, 180)) = 35
        _ShineProgress ("Shine Progress", Range(-1, 2)) = 0
        _FlowRect ("Flow Rect", Vector) = (0, 0, 1, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

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
                float4 worldPosition : TEXCOORD1;
                float2 flowUV : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float4 _ShineColor;
            float _ShineIntensity;
            float _ShineWidth;
            float _ShineAngle;
            float _ShineProgress;
            float4 _FlowRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                float2 worldXY = mul(unity_ObjectToWorld, v.vertex).xy;
                float2 size = max(_FlowRect.zw, float2(1e-5, 1e-5));
                OUT.flowUV = (worldXY - _FlowRect.xy) / size;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 texcol = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 color = IN.color * texcol;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                // 先按源 Alpha 预乘，清掉透明像素里残留的 RGB（PNG / 压缩常见）。
                color.rgb *= color.a;

                float rad = radians(_ShineAngle);
                float2 uv = IN.flowUV - 0.5;
                float t = uv.x * cos(rad) + uv.y * sin(rad);
                float pos = lerp(-0.85, 0.85, _ShineProgress);
                float band = 1.0 - smoothstep(0.0, max(_ShineWidth, 1e-4), abs(t - pos));
                // 压缩后 Alpha 往往不是绝对 0，用 smoothstep 压掉空洞和描边漏光。
                float shineMask = smoothstep(0.15, 0.75, color.a);
                color.rgb += color.rgb * _ShineColor.rgb * band * _ShineIntensity * _ShineColor.a * shineMask;

                return color;
            }
        ENDCG
        }
    }
}
