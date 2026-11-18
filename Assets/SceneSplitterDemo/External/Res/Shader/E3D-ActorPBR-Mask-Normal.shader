// Made with Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "E3D/Actor/PBR-Mask-Normal"
{
	Properties
	{
		_AlbedoMap("AlbedoMap", 2D) = "white" {}
		_BaseColor("BaseColor", Color) = (1,1,1,0)
		_NormalMap("NormalMap", 2D) = "bump" {}
		_MaskMap("MaskMap", 2D) = "white" {}
		[HDR]_MaxColor("MaxColor", Color) = (1,1,1,1)
		_Smooth("Smooth", Range( 0 , 2)) = 1
		_Metallic("Metallic", Range( 0 , 2)) = 1
		_SideLightColor("SideLightColor", Color) = (0.8439122,0.9225554,0.9485294,1)
		_SideLightScale("SideLightScale", Range( 0 , 1)) = 0.6
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" "SceneShadow" = "true" }
		Cull Back
		CGINCLUDE
		#include "E3D-ActorPBR-Common.cginc"

		uniform float _SideLightScale;
		uniform float4 _SideLightColor;
		uniform float4 _BaseColor;
		uniform sampler2D _AlbedoMap;
		uniform float4 _AlbedoMap_ST;
		uniform sampler2D _NormalMap;
		uniform float4 _NormalMap_ST;
		uniform sampler2D _MaskMap;
		uniform float4 _MaskMap_ST;
		uniform float _Metallic;
		uniform float _Smooth;
		uniform float4 _MaxColor;

		inline half4 LightingStandardCustomLighting( inout SurfaceOutputCustomLightingCustom s, half3 viewDir, UnityGI gi )
		{
			UnityGIInput data = s.GIData;
			Input i = s.SurfInput;
			half4 c = 0;
			#ifdef UNITY_PASS_FORWARDBASE
			float ase_lightAtten = data.atten;
			if( _LightColor0.a == 0)
			ase_lightAtten = 0;
			#else
			float3 ase_lightAttenRGB = gi.light.color / ( ( _LightColor0.rgb ) + 0.000001 );
			float ase_lightAtten = max( max( ase_lightAttenRGB.r, ase_lightAttenRGB.g ), ase_lightAttenRGB.b );
			#endif
			#if defined(HANDLE_SHADOWS_BLENDING_IN_GI)
			half bakedAtten = UnitySampleBakedOcclusion(data.lightmapUV.xy, data.worldPos);
			float zDist = dot(_WorldSpaceCameraPos - data.worldPos, UNITY_MATRIX_V[2].xyz);
			float fadeDist = UnityComputeShadowFadeDistance(data.worldPos, zDist);
			ase_lightAtten = UnityMixRealtimeAndBakedShadows(data.atten, bakedAtten, UnityComputeShadowFade(fadeDist));
			#endif
			float3 ase_worldPos = i.worldPos;
			float3 ase_worldViewDir = normalize( UnityWorldSpaceViewDir( ase_worldPos ) );
			float3 ase_worldNormal = WorldNormalVector( i, float3( 0, 0, 1 ) );
			float fresnelNdotV11 = dot( ase_worldNormal, ase_worldViewDir );
			float fresnelNode11 = ( 0.0 + _SideLightScale * pow( 1.0 - fresnelNdotV11, 5.0 ) );
			SurfaceOutputStandard s6 = (SurfaceOutputStandard ) 0;
			float2 uv_AlbedoMap = i.uv_texcoord * _AlbedoMap_ST.xy + _AlbedoMap_ST.zw;
			s6.Albedo = ( _BaseColor * tex2D( _AlbedoMap, uv_AlbedoMap ) ).rgb;
			float2 uv_NormalMap = i.uv_texcoord * _NormalMap_ST.xy + _NormalMap_ST.zw;
			s6.Normal = WorldNormalVector( i , UnpackNormal( tex2D( _NormalMap, uv_NormalMap ) ) );
			s6.Emission = float3( 0,0,0 );
			float2 uv_MaskMap = i.uv_texcoord * _MaskMap_ST.xy + _MaskMap_ST.zw;
			float4 tex2DNode1 = tex2D( _MaskMap, uv_MaskMap );
			s6.Metallic = ( tex2DNode1.r * _Metallic );
			s6.Smoothness = ( tex2DNode1.g * _Smooth );
			s6.Occlusion = tex2DNode1.b;

			data.light = gi.light;

			UnityGI gi6 = Custom_GlobalIllumination(data,s6.Occlusion,s6.Smoothness,s6.Normal, true);
			
			float3 surfResult6 = LightingStandard ( s6, viewDir, gi6 ).rgb;
			surfResult6 += s6.Emission;

			#ifdef UNITY_PASS_FORWARDADD//6
			surfResult6 -= s6.Emission;
			#endif//6
			float3 clampResult9 = clamp( surfResult6 , float3( 0,0,0 ) , _MaxColor.rgb );
			c.rgb = ( saturate( ( (0.0 + (( ase_lightAtten * 1.3 ) - 0.4) * (1.0 - 0.0) / (1.0 - 0.4)) * fresnelNode11 * _SideLightColor ) ) + float4( clampResult9 , 0.0 ) ).rgb;
			c.a = 1;
			return c;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf StandardCustomLighting vertex:CustomVert keepalpha fullforwardshadows

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float2 customPack1 : TEXCOORD1;
				float4 tSpace0 : TEXCOORD2;
				float4 tSpace1 : TEXCOORD3;
				float4 tSpace2 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				Input customInputData;
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				half3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
				half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				half3 worldBinormal = cross( worldNormal, worldTangent ) * tangentSign;
				o.tSpace0 = float4( worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x );
				o.tSpace1 = float4( worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y );
				o.tSpace2 = float4( worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z );
				o.customPack1.xy = customInputData.uv_texcoord;
				o.customPack1.xy = v.texcoord;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : SV_POSITION
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.customPack1.xy;
				float3 worldPos = float3( IN.tSpace0.w, IN.tSpace1.w, IN.tSpace2.w );
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldPos = worldPos;
				surfIN.worldNormal = float3( IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z );
				surfIN.internalSurfaceTtoW0 = IN.tSpace0.xyz;
				surfIN.internalSurfaceTtoW1 = IN.tSpace1.xyz;
				surfIN.internalSurfaceTtoW2 = IN.tSpace2.xyz;
				SurfaceOutputCustomLightingCustom o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputCustomLightingCustom, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
}