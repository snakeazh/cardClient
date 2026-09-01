// Made with Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "LTY/shader/ALL"
{
	Properties
	{
		[Enum(AlphaBlend,10,Additive,1)]_Dst("材质模式", Float) = 1
		[Enum(UnityEngine.Rendering.CullMode)]_CullMode("剔除模式", Float) = 0
		[Header(MainTex)]_maintex("maintex", 2D) = "white" {}
		[Toggle(_ONE_UV_ON)] _one_UV("one_UV", Float) = 0
		[Toggle(_A_R_ON)] _A_R("A_R", Float) = 0
		[HDR]_Maincolor("Maincolor", Color) = (1,1,1,1)
		_soft("soft", Float) = 0
		_Main_u_speed("Main_u_speed", Float) = 0
		_Main_v_speed("Main_v_speed", Float) = 0
		[Header(MASKTEX)]_MASKTEX("MASKTEX", 2D) = "white" {}
		_MASK_u_speed("MASK_u_speed", Float) = 0
		_MASK_v_speed("MASK_v_speed", Float) = 0
		[Header(XuanZhuan)]_rotator("rotator", Float) = 0
		_rotatorspeed("rotatorspeed", Float) = 0
		_rotator_U_point("rotator_U_point", Range( 0 , 1)) = 0.5
		_rotator_V_point("rotator_V_point", Range( 0 , 1)) = 0.5
		[Header(DissovleTex)]_DissovleTex("DissovleTex", 2D) = "white" {}
		[Toggle(_USE_DISSLOVE_ON)] _use_disslove("use_disslove", Float) = 0
		[Toggle(_DISSSC_ON)] _DissSC("Diss,S/C", Float) = 0
		_smooth("smooth", Range( 0.5 , 1)) = 0.5
		_Disspower("Disspower", Float) = 0
		_Dissovle_U_speed("Dissovle_U_speed", Float) = 0
		_Dissovle_V_speed("Dissovle_V_speed", Float) = 0
		[Header(NIUQU_Tex)]_NIUQU_Tex("NIUQU_Tex", 2D) = "white" {}
		_NIUQU_Power("NIUQU_Power", Float) = 0
		[Toggle(_NIUQUONOFF_ON)] _NIUQUONOFF("NIUQU,ON/OFF", Float) = 0
		_Niuqu_U_speed("Niuqu_U_speed", Float) = 0
		_Niuqu_V_speed("Niuqu_V_speed", Float) = 0
		[HDR][Header(Fresnel)]_FFFREncolor("FFFREncolor", Color) = (1,1,1,1)
		[Toggle(_FRE_ONOFF_ON)] _FRE_ONOFF("FRE_ON/OFF", Float) = 0
		[Toggle(_FRE_BF_ON)] _FRE_BF("FRE_B/F", Float) = 1
		_fre_scale("fre_scale", Float) = 1
		_fre_power("fre_power", Float) = 5
		[Header(Wpo_Tex)]_wpo_tex("wpo_tex", 2D) = "white" {}
		[Toggle(_ONOFF__VERTEX_ON)] _ONOFF__vertex("ON/OFF__vertex", Float) = 0
		[Toggle(_IS_VERTEX_ON)] _IS_vertex("IS_vertex", Float) = 0
		_WPO_tex("WPO_tex", Float) = 0
		_Vertexpower("Vertexpower", Vector) = (0,0,0,0)
		_WPO_U_Speed("WPO_U_Speed", Float) = 0
		_WPO_V_Speed("WPO_V_Speed", Float) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] _tex4coord2( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Custom"  "Queue" = "Transparent+0" "IsEmissive" = "true"  }
		Cull [_CullMode]
		ZWrite Off
		Blend SrcAlpha [_Dst]
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#include "UnityCG.cginc"
		#pragma target 3.0
		#pragma shader_feature _ONOFF__VERTEX_ON
		#pragma shader_feature _IS_VERTEX_ON
		#pragma shader_feature _ONE_UV_ON
		#pragma shader_feature _NIUQUONOFF_ON
		#pragma shader_feature _FRE_ONOFF_ON
		#pragma shader_feature _FRE_BF_ON
		#pragma shader_feature _USE_DISSLOVE_ON
		#pragma shader_feature _DISSSC_ON
		#pragma shader_feature_local _A_R_ON
		#pragma surface surf Unlit keepalpha noshadow noambient novertexlights nolightmap  nodynlightmap nodirlightmap nofog nometa noforwardadd vertex:vertexDataFunc 
		#undef TRANSFORM_TEX
		#define TRANSFORM_TEX(tex,name) float4(tex.xy * name##_ST.xy + name##_ST.zw, tex.z, tex.w)
		struct Input
		{
			float2 uv_texcoord;
			float4 uv2_tex4coord2;
			float4 vertexColor : COLOR;
			float4 screenPos;
			float3 viewDir;
			half3 worldNormal;
		};

		uniform half _Dst;
		uniform half _CullMode;
		uniform half _WPO_tex;
		uniform sampler2D _wpo_tex;
		uniform half _WPO_U_Speed;
		uniform half _WPO_V_Speed;
		uniform float4 _wpo_tex_ST;
		uniform half4 _Vertexpower;
		uniform sampler2D _maintex;
		uniform half _Main_u_speed;
		uniform half _Main_v_speed;
		uniform float4 _maintex_ST;
		uniform half _NIUQU_Power;
		uniform sampler2D _NIUQU_Tex;
		uniform half _Niuqu_U_speed;
		uniform half _Niuqu_V_speed;
		uniform float4 _NIUQU_Tex_ST;
		uniform half _rotator_U_point;
		uniform half _rotator_V_point;
		uniform half _rotator;
		uniform half _rotatorspeed;
		uniform half4 _Maincolor;
		UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
		uniform float4 _CameraDepthTexture_TexelSize;
		uniform half _soft;
		uniform half _fre_power;
		uniform half _fre_scale;
		uniform half4 _FFFREncolor;
		uniform half _smooth;
		uniform sampler2D _DissovleTex;
		uniform half _Dissovle_U_speed;
		uniform half _Dissovle_V_speed;
		uniform float4 _DissovleTex_ST;
		uniform half _Disspower;
		uniform sampler2D _MASKTEX;
		uniform half _MASK_u_speed;
		uniform half _MASK_v_speed;
		uniform float4 _MASKTEX_ST;

		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			half4 temp_cast_0 = (0.0).xxxx;
			#ifdef _IS_VERTEX_ON
				half staticSwitch123 = v.texcoord1.y;
			#else
				half staticSwitch123 = _WPO_tex;
			#endif
			half3 ase_vertexNormal = v.normal.xyz;
			half2 appendResult122 = (half2(_WPO_U_Speed , _WPO_V_Speed));
			float2 uv_wpo_tex = v.texcoord.xy * _wpo_tex_ST.xy + _wpo_tex_ST.zw;
			half2 panner119 = ( 1.0 * _Time.y * appendResult122 + uv_wpo_tex);
			#ifdef _ONOFF__VERTEX_ON
				half4 staticSwitch127 = ( staticSwitch123 * half4( ase_vertexNormal , 0.0 ) * tex2Dlod( _wpo_tex, float4( panner119, 0, 0.0) ) * _Vertexpower );
			#else
				half4 staticSwitch127 = temp_cast_0;
			#endif
			half4 Vertex117 = staticSwitch127;
			v.vertex.xyz += Vertex117.rgb;
			v.vertex.w = 1;
		}

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			half2 appendResult12 = (half2(( _Main_u_speed * _Time.y ) , ( _Main_v_speed * _Time.y )));
			float2 uv_maintex = i.uv_texcoord * _maintex_ST.xy + _maintex_ST.zw;
			float4 uv2s4_maintex = i.uv2_tex4coord2;
			uv2s4_maintex.xy = i.uv2_tex4coord2.xy * _maintex_ST.xy + _maintex_ST.zw;
			half2 appendResult51 = (half2(uv2s4_maintex.z , uv2s4_maintex.w));
			#ifdef _ONE_UV_ON
				half2 staticSwitch52 = ( uv_maintex + appendResult51 );
			#else
				half2 staticSwitch52 = ( appendResult12 + uv_maintex );
			#endif
			half2 appendResult179 = (half2(_Niuqu_U_speed , _Niuqu_V_speed));
			float2 uv_NIUQU_Tex = i.uv_texcoord * _NIUQU_Tex_ST.xy + _NIUQU_Tex_ST.zw;
			half2 panner175 = ( 1.0 * _Time.y * appendResult179 + uv_NIUQU_Tex);
			#ifdef _NIUQUONOFF_ON
				half staticSwitch169 = ( _NIUQU_Power * tex2D( _NIUQU_Tex, panner175 ).r );
			#else
				half staticSwitch169 = 0.0;
			#endif
			half2 appendResult20 = (half2(_rotator_U_point , _rotator_V_point));
			half mulTime26 = _Time.y * _rotatorspeed;
			float cos18 = cos( ( ( ( _rotator * UNITY_PI ) / 180.0 ) + mulTime26 ) );
			float sin18 = sin( ( ( ( _rotator * UNITY_PI ) / 180.0 ) + mulTime26 ) );
			half2 rotator18 = mul( ( staticSwitch52 + staticSwitch169 ) - appendResult20 , float2x2( cos18 , -sin18 , sin18 , cos18 )) + appendResult20;
			half4 tex2DNode1 = tex2D( _maintex, rotator18 );
			float4 ase_screenPos = float4( i.screenPos.xyz , i.screenPos.w + 0.00000000001 );
			half4 ase_screenPosNorm = ase_screenPos / ase_screenPos.w;
			ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
			float screenDepth58 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_screenPosNorm.xy ));
			half distanceDepth58 = abs( ( screenDepth58 - LinearEyeDepth( ase_screenPosNorm.z ) ) / ( _soft ) );
			half temp_output_60_0 = saturate( distanceDepth58 );
			half3 ase_worldNormal = i.worldNormal;
			half dotResult82 = dot( i.viewDir , ase_worldNormal );
			half temp_output_84_0 = saturate( abs( dotResult82 ) );
			#ifdef _FRE_BF_ON
				half staticSwitch88 = ( pow( ( 1.0 - temp_output_84_0 ) , _fre_power ) * ( _fre_scale * 1 ) );
			#else
				half staticSwitch88 = temp_output_84_0;
			#endif
			#ifdef _FRE_ONOFF_ON
				half staticSwitch144 = staticSwitch88;
			#else
				half staticSwitch144 = 1.0;
			#endif
			half2 appendResult186 = (half2(_Dissovle_U_speed , _Dissovle_V_speed));
			float2 uv_DissovleTex = i.uv_texcoord * _DissovleTex_ST.xy + _DissovleTex_ST.zw;
			half2 panner183 = ( 1.0 * _Time.y * appendResult186 + uv_DissovleTex);
			#ifdef _DISSSC_ON
				half staticSwitch171 = uv2s4_maintex.x;
			#else
				half staticSwitch171 = _Disspower;
			#endif
			half smoothstepResult41 = smoothstep( ( 1.0 - _smooth ) , _smooth , saturate( ( ( tex2D( _DissovleTex, panner183 ).r + 1.0 ) - ( staticSwitch171 * 2.0 ) ) ));
			#ifdef _USE_DISSLOVE_ON
				half staticSwitch47 = smoothstepResult41;
			#else
				half staticSwitch47 = 1.0;
			#endif
			o.Emission = ( tex2DNode1 * _Maincolor * i.vertexColor * ( temp_output_60_0 * ( staticSwitch144 * _FFFREncolor ) * staticSwitch47 ) ).rgb;
			#ifdef _A_R_ON
				half staticSwitch187 = tex2DNode1.a;
			#else
				half staticSwitch187 = tex2DNode1.r;
			#endif
			half2 appendResult134 = (half2(_MASK_u_speed , _MASK_v_speed));
			float2 uv_MASKTEX = i.uv_texcoord * _MASKTEX_ST.xy + _MASKTEX_ST.zw;
			half2 panner131 = ( 1.0 * _Time.y * appendResult134 + uv_MASKTEX);
			o.Alpha = ( i.vertexColor.a * _Maincolor.a * ( staticSwitch144 * _FFFREncolor.a * staticSwitch47 ) * temp_output_60_0 * staticSwitch187 * tex2D( _MASKTEX, panner131 ).r );
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=18800
215;139;1461;824;2966.143;-1214.873;1;True;True
Node;AmplifyShaderEditor.ViewDirInputsCoordNode;80;-2755.447,284.8759;Inherit;False;World;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldNormalVector;81;-2760.06,444.638;Inherit;False;False;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;184;-2803.287,1109.687;Inherit;False;Property;_Dissovle_U_speed;Dissovle_U_speed;22;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DotProductOpNode;82;-2590.894,275.0688;Inherit;True;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;185;-2809.402,1195.867;Inherit;False;Property;_Dissovle_V_speed;Dissovle_V_speed;23;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;176;-2760.158,-466.3362;Inherit;False;Property;_Niuqu_U_speed;Niuqu_U_speed;27;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;177;-2758.829,-387.5987;Inherit;False;Property;_Niuqu_V_speed;Niuqu_V_speed;28;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;94;-2778.717,902.6245;Inherit;False;0;33;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.AbsOpNode;83;-2396.996,285.6035;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;13;-2731.663,-1236.465;Inherit;False;Property;_Main_u_speed;Main_u_speed;8;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;186;-2591.872,1112.582;Inherit;True;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleTimeNode;9;-2729.592,-1134.83;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;174;-2757.067,-597.1648;Inherit;False;0;166;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;179;-2553.534,-412.3971;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;14;-2759.865,-1005.541;Inherit;False;Property;_Main_v_speed;Main_v_speed;9;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;84;-2222.831,280.2456;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;50;-2661.977,-877.2964;Inherit;False;1;1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;38;-2453.576,1327.506;Inherit;False;Property;_Disspower;Disspower;21;0;Create;True;0;0;0;False;0;False;0;0.43;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;175;-2410.46,-464.8926;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;10;-2450.446,-1223.8;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;183;-2532.682,913.8054;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode;44;-2821.608,848.0111;Inherit;False;1897.539;553.5128;溶解;1;171;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;11;-2454.605,-1125.625;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;171;-2268.15,1137.361;Inherit;False;Property;_DissSC;Diss,S/C;19;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;12;-2243.188,-1171.196;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;51;-2386.668,-836.1641;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;15;-2514.421,-1010.456;Inherit;False;0;1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;120;-2786.705,1767.427;Inherit;False;Property;_WPO_U_Speed;WPO_U_Speed;39;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;121;-2789.804,1865.694;Inherit;False;Property;_WPO_V_Speed;WPO_V_Speed;40;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;154;-2527.702,726.2903;Inherit;False;Property;_fre_scale;fre_scale;32;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;166;-2234.242,-490.1376;Inherit;True;Property;_NIUQU_Tex;NIUQU_Tex;24;1;[Header];Create;True;1;NIUQU_Tex;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;39;-2222.6,1237.999;Inherit;False;Constant;_Float1;Float 1;11;0;Create;True;0;0;0;False;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;33;-2345.292,879.1146;Inherit;True;Property;_DissovleTex;DissovleTex;17;1;[Header];Create;True;1;DissovleTex;0;0;False;0;False;-1;None;9c839685a69b5574dacf60e5465ec59f;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;35;-2216.915,1063.215;Inherit;False;Constant;_Float0;Float 0;10;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;87;-2541.35,542.7086;Inherit;False;Property;_fre_power;fre_power;33;0;Create;True;0;0;0;False;0;False;5;5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;85;-2203.848,398.6153;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;31;-2747.836,-28.37132;Inherit;False;Property;_rotator;rotator;13;1;[Header];Create;True;1;XuanZhuan;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;172;-2412.632,-559.6588;Inherit;False;Property;_NIUQU_Power;NIUQU_Power;25;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;37;-2032.739,1155.406;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;98;-2783.737,1615.165;Inherit;False;0;97;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;122;-2591.466,1819.102;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;5;-2050.673,-1114.263;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;29;-2592.871,59.1376;Inherit;False;Constant;_Float3;Float 3;6;0;Create;True;0;0;0;False;0;False;180;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;34;-2040.336,903.5818;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;86;-2372.857,545.4606;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleNode;152;-2350.535,687.4194;Inherit;False;1;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PiNode;28;-2611.101,-19.25566;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;27;-2764.205,58.47707;Inherit;False;Property;_rotatorspeed;rotatorspeed;14;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;116;-2810.753,1411.736;Inherit;False;1572.141;543.2564;顶点偏移;10;115;97;113;114;117;125;111;123;128;127;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;167;-1898.683,-393.6576;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;53;-2166.511,-913.1276;Inherit;True;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;168;-1905.086,-467.5471;Inherit;False;Constant;_Float6;Float 6;43;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;125;-2429.513,1443.682;Inherit;False;1;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;156;-2179.075,517.0546;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;52;-1909.239,-1013.07;Inherit;True;Property;_one_UV;one_UV;4;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;FLOAT2;0,0;False;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT2;0,0;False;6;FLOAT2;0,0;False;7;FLOAT2;0,0;False;8;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;115;-2366.509,1612.857;Inherit;False;Property;_WPO_tex;WPO_tex;37;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;169;-1741.839,-425.1599;Inherit;False;Property;_NIUQUONOFF;NIUQU,ON/OFF;26;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;36;-1822.792,905.7346;Inherit;True;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;24;-2398.745,-19.69926;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;26;-2433.769,112.1083;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;42;-1793.744,1163.336;Inherit;False;Property;_smooth;smooth;20;0;Create;True;0;0;0;False;0;False;0.5;1;0.5;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;22;-2744.988,-104.567;Inherit;False;Property;_rotator_V_point;rotator_V_point;16;0;Create;True;0;0;0;False;0;False;0.5;0.5;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;119;-2537.854,1642.176;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;21;-2734.303,-194.0134;Inherit;False;Property;_rotator_U_point;rotator_U_point;15;0;Create;True;0;0;0;False;0;False;0.5;0.5;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;43;-1598.467,1025.978;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;97;-2356.122,1704.393;Inherit;True;Property;_wpo_tex;wpo_tex;34;1;[Header];Create;True;1;Wpo_Tex;0;0;False;0;False;-1;None;14c3bcc509777324491ba2160e3e0cf8;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector4Node;113;-2058.164,1703.362;Inherit;False;Property;_Vertexpower;Vertexpower;38;0;Create;True;0;0;0;False;0;False;0,0,0,0;1,1,1,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.NormalVertexDataNode;111;-1985.181,1523.929;Inherit;False;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CommentaryNode;90;-2814.172,234.8758;Inherit;False;1259.307;580.0871;菲尼尔;2;144;93;;1,1,1,1;0;0
Node;AmplifyShaderEditor.StaticSwitch;123;-2205.592,1527.083;Inherit;False;Property;_IS_vertex;IS_vertex;36;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;59;-1519.516,281.1421;Inherit;False;Property;_soft;soft;7;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;133;-1672.436,60.17553;Inherit;False;Property;_MASK_v_speed;MASK_v_speed;12;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;132;-1673.671,-33.93538;Inherit;False;Property;_MASK_u_speed;MASK_u_speed;11;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;40;-1591.445,919.4226;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;88;-2038.633,355.9439;Inherit;False;Property;_FRE_BF;FRE_B/F;31;0;Create;True;0;0;0;False;0;False;0;1;1;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;145;-1957.431,275.4673;Inherit;False;Constant;_Float5;Float 5;35;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;23;-2241.986,7.838887;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;20;-2385.732,-158.0898;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;173;-1647.543,-830.9522;Inherit;True;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;130;-1695.175,-194.5818;Inherit;False;0;45;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StaticSwitch;144;-1797.143,301.7416;Inherit;False;Property;_FRE_ONOFF;FRE_ON/OFF;30;0;Create;True;0;0;0;False;0;False;0;0;1;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;41;-1434.118,1007.146;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RotatorNode;18;-1430.652,-359.8886;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ColorNode;93;-1847.377,461.3237;Inherit;False;Property;_FFFREncolor;FFFREncolor;29;2;[HDR];[Header];Create;True;1;Fresnel;0;0;False;0;False;1,1,1,1;1,1,1,1;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;134;-1431.224,-2.158306;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DepthFade;58;-1312.982,270.5608;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;114;-1757.011,1603.869;Inherit;False;4;4;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;2;COLOR;0,0,0,0;False;3;FLOAT4;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;128;-2197.1,1445.666;Inherit;False;Constant;_Float4;Float 4;30;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;48;-1367.428,887.7685;Inherit;False;Constant;_Float2;Float 2;14;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;127;-1683.939,1440.976;Inherit;False;Property;_ONOFF__vertex;ON/OFF__vertex;35;0;Create;True;0;0;0;False;0;False;0;0;1;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;COLOR;0,0,0,0;False;0;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;4;COLOR;0,0,0,0;False;5;COLOR;0,0,0,0;False;6;COLOR;0,0,0,0;False;7;COLOR;0,0,0,0;False;8;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;1;-1219.731,-446.1503;Inherit;True;Property;_maintex;maintex;3;1;[Header];Create;True;1;MainTex;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StaticSwitch;47;-1146.546,953.2758;Inherit;False;Property;_use_disslove;use_disslove;18;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;False;True;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;92;-1102.268,487.6889;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;60;-1066.892,283.2765;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;131;-1456.873,-188.4642;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;138;-1103.877,688.8658;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;2;-1115.271,-786.6048;Inherit;False;Property;_Maincolor;Maincolor;6;1;[HDR];Create;True;0;0;0;False;0;False;1,1,1,1;1,1,1,1;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;95;-631.1624,-160.2825;Inherit;False;3;3;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.StaticSwitch;187;-617.2689,336.0484;Inherit;False;Property;_A_R;A_R;5;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;117;-1443.166,1622.602;Inherit;False;Vertex;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.VertexColorNode;3;-865.4141,-904.1125;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;45;-1276.406,-185.9032;Inherit;True;Property;_MASKTEX;MASKTEX;10;1;[Header];Create;True;1;MASKTEX;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;4;-398.6393,-374.236;Inherit;True;4;4;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;135;238.0081,2.133241;Inherit;False;Property;_Dst;材质模式;0;1;[Enum];Create;False;0;2;AlphaBlend;10;Additive;1;0;True;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;118;-175.3489,305.3766;Inherit;False;117;Vertex;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;142;239.4929,85.16483;Inherit;False;Property;_CullMode;剔除模式;1;1;[Enum];Create;False;0;0;1;UnityEngine.Rendering.CullMode;True;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;137;-488.5503,81.28259;Inherit;False;6;6;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;0,0;Half;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;LTY/shader/ALL;False;False;False;False;True;True;True;True;True;True;True;True;False;False;False;False;False;False;False;False;False;Off;2;False;-1;0;False;-1;False;0;False;-1;0;False;-1;False;0;Custom;0.5;True;False;0;True;Custom;;Transparent;All;14;all;True;True;True;True;0;False;-1;False;0;False;-1;255;False;-1;255;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;False;2;15;10;25;False;0.5;False;2;5;False;-1;10;True;135;0;0;False;-1;0;False;-1;0;True;-1;0;False;-1;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;Relative;0;;2;-1;-1;-1;0;False;0;0;True;142;-1;0;False;-1;0;0;0;False;0.1;False;-1;0;False;-1;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.CommentaryNode;139;-1153.877,638.8658;Inherit;False;219;206;ALPHA_MODE;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;30;-2806.188,-1278.837;Inherit;False;1085.259;601.8163;UV流动;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;32;-2814.205,-244.0134;Inherit;False;726.2192;468.7215;旋转;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;181;-2810.158,-647.6354;Inherit;False;1361.22;385.8978;扭曲;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;141;-538.5502,31.28267;Inherit;False;360.4317;223.932;ALPHA模式连到不透明度，ADD模式连到Emission;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;57;-1542.668,237.7491;Inherit;False;609.0189;194.8381;软粒子;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;136;-1742.051,-238.4642;Inherit;False;798.6744;460.0455;MASK;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;149;-1152.268,437.6889;Inherit;False;219;183;Fre颜色;0;;1,1,1,1;0;0
WireConnection;82;0;80;0
WireConnection;82;1;81;0
WireConnection;83;0;82;0
WireConnection;186;0;184;0
WireConnection;186;1;185;0
WireConnection;179;0;176;0
WireConnection;179;1;177;0
WireConnection;84;0;83;0
WireConnection;175;0;174;0
WireConnection;175;2;179;0
WireConnection;10;0;13;0
WireConnection;10;1;9;0
WireConnection;183;0;94;0
WireConnection;183;2;186;0
WireConnection;11;0;14;0
WireConnection;11;1;9;0
WireConnection;171;1;38;0
WireConnection;171;0;50;1
WireConnection;12;0;10;0
WireConnection;12;1;11;0
WireConnection;51;0;50;3
WireConnection;51;1;50;4
WireConnection;166;1;175;0
WireConnection;33;1;183;0
WireConnection;85;0;84;0
WireConnection;37;0;171;0
WireConnection;37;1;39;0
WireConnection;122;0;120;0
WireConnection;122;1;121;0
WireConnection;5;0;12;0
WireConnection;5;1;15;0
WireConnection;34;0;33;1
WireConnection;34;1;35;0
WireConnection;86;0;85;0
WireConnection;86;1;87;0
WireConnection;152;0;154;0
WireConnection;28;0;31;0
WireConnection;167;0;172;0
WireConnection;167;1;166;1
WireConnection;53;0;15;0
WireConnection;53;1;51;0
WireConnection;156;0;86;0
WireConnection;156;1;152;0
WireConnection;52;1;5;0
WireConnection;52;0;53;0
WireConnection;169;1;168;0
WireConnection;169;0;167;0
WireConnection;36;0;34;0
WireConnection;36;1;37;0
WireConnection;24;0;28;0
WireConnection;24;1;29;0
WireConnection;26;0;27;0
WireConnection;119;0;98;0
WireConnection;119;2;122;0
WireConnection;43;0;42;0
WireConnection;97;1;119;0
WireConnection;123;1;115;0
WireConnection;123;0;125;2
WireConnection;40;0;36;0
WireConnection;88;1;84;0
WireConnection;88;0;156;0
WireConnection;23;0;24;0
WireConnection;23;1;26;0
WireConnection;20;0;21;0
WireConnection;20;1;22;0
WireConnection;173;0;52;0
WireConnection;173;1;169;0
WireConnection;144;1;145;0
WireConnection;144;0;88;0
WireConnection;41;0;40;0
WireConnection;41;1;43;0
WireConnection;41;2;42;0
WireConnection;18;0;173;0
WireConnection;18;1;20;0
WireConnection;18;2;23;0
WireConnection;134;0;132;0
WireConnection;134;1;133;0
WireConnection;58;0;59;0
WireConnection;114;0;123;0
WireConnection;114;1;111;0
WireConnection;114;2;97;0
WireConnection;114;3;113;0
WireConnection;127;1;128;0
WireConnection;127;0;114;0
WireConnection;1;1;18;0
WireConnection;47;1;48;0
WireConnection;47;0;41;0
WireConnection;92;0;144;0
WireConnection;92;1;93;0
WireConnection;60;0;58;0
WireConnection;131;0;130;0
WireConnection;131;2;134;0
WireConnection;138;0;144;0
WireConnection;138;1;93;4
WireConnection;138;2;47;0
WireConnection;95;0;60;0
WireConnection;95;1;92;0
WireConnection;95;2;47;0
WireConnection;187;1;1;1
WireConnection;187;0;1;4
WireConnection;117;0;127;0
WireConnection;45;1;131;0
WireConnection;4;0;1;0
WireConnection;4;1;2;0
WireConnection;4;2;3;0
WireConnection;4;3;95;0
WireConnection;137;0;3;4
WireConnection;137;1;2;4
WireConnection;137;2;138;0
WireConnection;137;3;60;0
WireConnection;137;4;187;0
WireConnection;137;5;45;1
WireConnection;0;2;4;0
WireConnection;0;9;137;0
WireConnection;0;11;118;0
ASEEND*/
//CHKSM=888DD8960DA138485B9E3B68D83BD4A258254ACE