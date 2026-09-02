// 遮罩缩放显示效果（UGUI / 普通网格通用）
// 黑白遮罩图：黑色不可见，白色可见，遮罩区域支持缩放（以UV中心0.5为基准）与偏移
// 注意：内容贴图命名为_ContentTex，避免UGUI把Image的Sprite强制绑定到_MainTex
// 内容UV变换：支持Tiling/Offset与按时间滚动；默认值不影响已有材质
// 平铺或滚动时内容贴图的Wrap Mode需设为Repeat
Shader "Effect/MaskScaleReveal"
{
    Properties
    {
        [Header(Main)]_Color("颜色", Color) = (1,1,1,1)
        _ContentTex("内容贴图", 2D) = "white" {}
        _UVScrollX("内容UV滚动_X(单位/秒)", Float) = 0
        _UVScrollY("内容UV滚动_Y(单位/秒)", Float) = 0
        [Header(Mask)]
        [NoScaleOffset]_MaskTex("遮罩贴图(黑不可见白可见)", 2D) = "white" {}
        _MaskScale("遮罩缩放(1为原大小,0为完全隐藏)", Float) = 1
        _MaskOffsetX("遮罩偏移_X", Float) = 0
        _MaskOffsetY("遮罩偏移_Y", Float) = 0
        _MaskSoftness("遮罩边缘软硬度", Range(0.001, 1)) = 0.1
        [Header(Blend)]
        [Enum(AlphaBlend,0,Additive,1)] _BlendMode("混合模式", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode("剔除模式", Float) = 0
        [Header(UGUI)]
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
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull [_CullMode]
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        ColorMask [_ColorMask]

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityUI.cginc"
        #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
        #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
        #pragma target 2.0

        sampler2D _ContentTex;
        float4 _ContentTex_ST;
        sampler2D _MaskTex;
        fixed4 _Color;
        float _MaskScale;
        float _MaskOffsetX;
        float _MaskOffsetY;
        float _MaskSoftness;
        float _UVScrollX;
        float _UVScrollY;
        float _BlendMode;
        float4 _ClipRect;

        struct appdata
        {
            float4 vertex : POSITION;
            fixed4 color : COLOR;
            float2 texcoord : TEXCOORD0;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            fixed4 color : COLOR;
            float2 uv : TEXCOORD0;
            float2 uvMask : TEXCOORD1;
            float4 worldPosition : TEXCOORD2;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.worldPosition = v.vertex;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.color = v.color * _Color;
            // 内容贴图UV：先应用Tiling/Offset，再叠加按时间滚动的偏移
            // 注意整体偏移量对所有顶点相同，不能在此处取frac（顶点两端取模后会插值出错）
            o.uv = TRANSFORM_TEX(v.texcoord, _ContentTex) + float2(_UVScrollX, _UVScrollY) * _Time.y;
            // 遮罩以UV中心(0.5,0.5)缩放：_MaskScale即图形大小系数
            // 1为原大小，往0缩小图形向中心收缩，0时UV远超边界，Clamp取样黑边=完全不可见
            float s = max(_MaskScale, 1e-6);
            float2 uvMask = (v.texcoord - 0.5) / s + 0.5;
            uvMask += float2(_MaskOffsetX, _MaskOffsetY);
            o.uvMask = uvMask;
            return o;
        }

        fixed4 frag(v2f i) : SV_Target
        {
            half4 content = tex2D(_ContentTex, i.uv);
            half mask = tex2D(_MaskTex, i.uvMask).r;
            half soft = _MaskSoftness;

            // 黑色不可见、白色可见：mask值作为透明度，边缘用软硬度过渡
            half maskAlpha = smoothstep(0.5 - soft * 0.5, 0.5 + soft * 0.5, mask);

            half alpha = content.a * maskAlpha * i.color.a;
            #ifdef UNITY_UI_ALPHACLIP
            clip(alpha - 0.001);
            #endif

            #ifdef UNITY_UI_CLIP_RECT
            half m = UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
            alpha *= m;
            #endif

            fixed4 col = i.color * content;
            col.a = alpha;
            // 预乘处理：AlphaBlend用预乘输出；Additive时alpha置0只保留加亮部分
            col.rgb *= alpha;
            if (_BlendMode > 0.5)
                col.a = 0;
            return col;
        }
        ENDCG

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
