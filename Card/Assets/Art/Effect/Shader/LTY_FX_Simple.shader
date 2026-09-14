// LTY/FX/Simple — 日常特效：主贴图 + 颜色 + 双 Mask（轻量 Vert/Frag）
Shader "LTY/FX/Simple"
{
	Properties
	{
		[Enum(AlphaBlend,10,Additive,1)]_Dst("材质模式", Float) = 1
		[Enum(UnityEngine.Rendering.CullMode)]_CullMode("剔除模式", Float) = 0

		[Header(MainTex)]_maintex("maintex", 2D) = "white" {}
		[HDR]_Maincolor("Maincolor", Color) = (1,1,1,1)
		[Toggle(_A_R_ON)] _A_R("Alpha用A(关=用R)", Float) = 0
		[Toggle(_ONE_UV_ON)] _one_UV("CustomData偏移UV(UV2.zw)", Float) = 0
		_Main_u_speed("Main_u_speed", Float) = 0
		_Main_v_speed("Main_v_speed", Float) = 0

		[Header(Mask)]
		[Toggle(_MASK_ON)] _UseMask("启用Mask1", Float) = 1
		_MASKTEX("MASKTEX", 2D) = "white" {}
		_MASK_u_speed("MASK_u_speed", Float) = 0
		_MASK_v_speed("MASK_v_speed", Float) = 0
		[Toggle(_MASK2_ON)] _UseMask2("启用Mask2", Float) = 0
		_MASKTEX2("MASKTEX2", 2D) = "white" {}
		_MASK2_u_speed("MASK2_u_speed", Float) = 0
		_MASK2_v_speed("MASK2_v_speed", Float) = 0

		[Header(Overdraw)]
		[Toggle(_SOFT_PARTICLE_ON)] _UseSoftParticle("软粒子(采样深度,费)", Float) = 0
		_soft("soft", Float) = 1
		[Toggle(_ALPHACLIP_ON)] _UseAlphaClip("近零Alpha裁剪", Float) = 1
		_AlphaClip("AlphaClip", Range(0, 1)) = 0.01
		_ColorClamp("HDR亮度上限(0=不限制)", Float) = 0

		[Header(UGUIClip)]
		[Toggle] _UseClipRect("启用视口裁剪(UIParticleClipper 写入)", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
			"PreviewType" = "Plane"
		}
		Cull [_CullMode]
		ZWrite Off
		Lighting Off
		Fog { Mode Off }
		Blend SrcAlpha [_Dst]

		Pass
		{
			Name "FORWARD"
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_instancing
			#pragma shader_feature_local _ONE_UV_ON
			#pragma shader_feature_local _A_R_ON
			#pragma shader_feature_local _MASK_ON
			#pragma shader_feature_local _MASK2_ON
			#pragma shader_feature_local _SOFT_PARTICLE_ON
			#pragma shader_feature_local _ALPHACLIP_ON

			#include "UnityCG.cginc"

			sampler2D _maintex;
			float4 _maintex_ST;
			half _Main_u_speed;
			half _Main_v_speed;
			half4 _Maincolor;

			sampler2D _MASKTEX;
			float4 _MASKTEX_ST;
			half _MASK_u_speed;
			half _MASK_v_speed;

			sampler2D _MASKTEX2;
			float4 _MASKTEX2_ST;
			half _MASK2_u_speed;
			half _MASK2_v_speed;

		UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
		half _soft;
		half _AlphaClip;
		half _ColorClamp;
		// UGUI 视口裁剪：UIParticleClipper 经 MaterialPropertyBlock 写入（世界坐标矩形），材质默认 0 不裁
		half _UseClipRect;
		float4 _ClipRect;

			struct appdata
				{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float4 texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				};

		struct v2f
		{
			float4 pos : SV_POSITION;
			half4 color : COLOR;
			float2 uv0 : TEXCOORD0;
			#ifdef _ONE_UV_ON
			half2 customUV : TEXCOORD1;
			#endif
			#ifdef _SOFT_PARTICLE_ON
			float4 screenPos : TEXCOORD2;
			#endif
			float3 worldPos : TEXCOORD3;
			UNITY_VERTEX_OUTPUT_STEREO
		};

		v2f vert(appdata v)
		{
			v2f o;
			UNITY_SETUP_INSTANCE_ID(v);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

			o.pos = UnityObjectToClipPos(v.vertex);
			o.color = v.color;
			o.uv0 = v.texcoord.xy;
			o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
			#ifdef _ONE_UV_ON
				o.customUV = v.texcoord1.zw;
			#endif
			#ifdef _SOFT_PARTICLE_ON
				o.screenPos = ComputeScreenPos(o.pos);
				COMPUTE_EYEDEPTH(o.screenPos.z);
			#endif
			return o;
		}

			half4 frag(v2f i) : SV_Target
				{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float2 mainUV = i.uv0 * _maintex_ST.xy + _maintex_ST.zw;
				#ifdef _ONE_UV_ON
					mainUV += i.customUV;
				#else
					mainUV += _Time.y * half2(_Main_u_speed, _Main_v_speed);
				#endif

				half4 mainTex = tex2D(_maintex, mainUV);
				#ifdef _A_R_ON
					half mainA = mainTex.a;
				#else
					half mainA = mainTex.r;
				#endif

				half3 emis = mainTex.rgb * _Maincolor.rgb * i.color.rgb;
				half alpha = i.color.a * _Maincolor.a * mainA;

				#ifdef _ALPHACLIP_ON
					clip(alpha - _AlphaClip);
				#endif

				#ifdef _MASK_ON
					float2 maskUV = i.uv0 * _MASKTEX_ST.xy + _MASKTEX_ST.zw;
					maskUV += _Time.y * half2(_MASK_u_speed, _MASK_v_speed);
					alpha *= tex2D(_MASKTEX, maskUV).r;
				#endif

				#ifdef _MASK2_ON
					float2 mask2UV = i.uv0 * _MASKTEX2_ST.xy + _MASKTEX2_ST.zw;
					mask2UV += _Time.y * half2(_MASK2_u_speed, _MASK2_v_speed);
					alpha *= tex2D(_MASKTEX2, mask2UV).r;
				#endif

				#ifdef _SOFT_PARTICLE_ON
					float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
					half softFade = saturate(abs(sceneZ - i.screenPos.z) / max(_soft, 1e-5));
					emis *= softFade;
					alpha *= softFade;
				#endif

				#ifdef _ALPHACLIP_ON
				clip(alpha - _AlphaClip);
			#endif

				// UGUI 视口裁剪：世界坐标矩形外 alpha 归零，配合 _ALPHACLIP_ON 把矩形外像素整颗丢弃
				UNITY_BRANCH
			if (_UseClipRect > 0.5)
				{
				float2 inside = step(float2(_ClipRect.x, _ClipRect.y), i.worldPos.xy)
					* step(i.worldPos.xy, float2(_ClipRect.z, _ClipRect.w));
				alpha *= inside.x * inside.y;
				}

				#ifdef _ALPHACLIP_ON
				clip(alpha - _AlphaClip);
			#endif

				UNITY_BRANCH
				if (_ColorClamp > 0)
				emis = min(emis, half3(_ColorClamp, _ColorClamp, _ColorClamp));

			return half4(emis, alpha);
				}
			ENDCG
		}
	}
	FallBack Off
}
