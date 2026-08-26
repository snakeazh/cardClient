Shader "UI/Split"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SplitSide ("Split Side", Float) = -1
        _CrackAmplitude ("Crack Amplitude", Range(0, 0.5)) = 0.18
        _CrackScale ("Crack Scale", Float) = 6
        _CrackSeed ("Crack Seed", Float) = 0
        _Gap ("Gap", Range(0, 0.2)) = 0
        _EdgeWidth ("Edge Width", Range(0.001, 0.2)) = 0.035
        [HDR] _EdgeColor ("Edge Color", Color) = (1, 0.95, 0.8, 0)
        _SplitRect ("Split Rect", Vector) = (0, 0, 1, 1)
        _ClipRect ("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)

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
                float2 splitUV : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            // Canvas.vertexColorAlwaysGammaSpace 为真时，顶点色以 Gamma 传入；
            // 线性工作流下必须转成 Linear，否则会整体发浅（内置 UI/Default 同样处理）。
            int _UIVertexColorAlwaysGammaSpace;

            float _SplitSide;
            float _CrackAmplitude;
            float _CrackScale;
            float _CrackSeed;
            float _Gap;
            float _EdgeWidth;
            float4 _EdgeColor;
            float4 _SplitRect;

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float CrackX(float2 uv)
            {
                float n = ValueNoise(float2(uv.y * _CrackScale, _CrackSeed));
                n = n * 0.65 + ValueNoise(float2(uv.y * _CrackScale * 2.17, _CrackSeed + 17.3)) * 0.35;
                return 0.5 + (n - 0.5) * _CrackAmplitude;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                if (_UIVertexColorAlwaysGammaSpace)
                {
                    if (!IsGammaSpace())
                    {
                        v.color.rgb = GammaToLinearSpace(v.color.rgb);
                    }
                }
                OUT.color = v.color * _Color;
                float2 worldXY = mul(unity_ObjectToWorld, v.vertex).xy;
                float2 size = max(_SplitRect.zw, float2(1e-5, 1e-5));
                OUT.splitUV = (worldXY - _SplitRect.xy) / size;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = IN.color * (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                float crack = CrackX(IN.splitUV);
                float dist = IN.splitUV.x - crack;
                float keep = _SplitSide < 0.0
                    ? crack - _Gap - IN.splitUV.x
                    : IN.splitUV.x - crack - _Gap;
                clip(keep);

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;
                return color;
            }
        ENDCG
        }
    }
    Fallback "UI/Default"
}
