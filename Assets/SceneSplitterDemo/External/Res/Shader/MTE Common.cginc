#ifndef MTE_COMMON_CGINC_INCLUDED
#define MTE_COMMON_CGINC_INCLUDED

#include "UnityCG.cginc"
#include "UnityPBSLighting.cginc"
#include "GlobalShadowMap.cginc"
#include "Lighting.cginc"

#ifdef MTE_STANDARD_SHADER
#define Output SurfaceOutputStandard
#else
#define Output SurfaceOutput
#endif

struct Input
{
	float2 tc_Control : TEXCOORD0;
	float4 tc_Splat01 : TEXCOORD1;
	float4 tc_Splat23 : TEXCOORD2;
	UNITY_FOG_COORDS(3)
};

void MTE_SplatmapFinalColor(Input IN, Output o, inout fixed4 color)
{
	color *= o.Alpha;
	UNITY_APPLY_FOG(IN.fogCoord, color);
}

void MTE_SplatmapFinalPrepass(Input IN, Output o, inout fixed4 normalSpec)
{
	normalSpec *= o.Alpha;
}

void MTE_SplatmapFinalGBuffer(Input IN, Output o, inout half4 diffuse, inout half4 specSmoothness, inout half4 normal, inout half4 emission)
{
	diffuse.rgb *= o.Alpha;
	specSmoothness *= o.Alpha;
	normal.rgb *= o.Alpha;
	emission *= o.Alpha;
}

half4 LightingMTEStandard (SurfaceOutputStandard s, float3 viewDir, UnityGI gi)
{
	s.Normal = normalize(s.Normal);

	half oneMinusReflectivity;
	half3 specColor;
	s.Albedo = DiffuseAndSpecularFromMetallic (s.Albedo, s.Metallic, /*out*/ specColor, /*out*/ oneMinusReflectivity);

	// shader relies on pre-multiply alpha-blend (_SrcBlend = One, _DstBlend = OneMinusSrcAlpha)
	// this is necessary to handle transparency in physically correct way - only diffuse component gets affected by alpha
	half outputAlpha;
	s.Albedo = PreMultiplyAlpha (s.Albedo, s.Alpha, oneMinusReflectivity, /*out*/ outputAlpha);

	half4 c = UNITY_BRDF_PBS (s.Albedo, specColor, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, gi.light, gi.indirect);
	c.a = outputAlpha;
	return c;
}

void LightingMTEStandard_GI (
			SurfaceOutputStandard s,
			UnityGIInput data,
			inout UnityGI gi)
{
#if defined(UNITY_PASS_DEFERRED) && UNITY_ENABLE_REFLECTION_BUFFERS
	gi = UnityGlobalIllumination(data, s.Occlusion, s.Normal);
#else
	Unity_GlossyEnvironmentData g = UnityGlossyEnvironmentSetup(s.Smoothness, data.worldViewDir, s.Normal, lerp(unity_ColorSpaceDielectricSpec.rgb, s.Albedo, s.Metallic));
	gi = UnityGlobalIllumination(data, s.Occlusion, s.Normal, g);
#endif

	gi.light.color *= GetGlobalShadowMapAttention(data.worldPos);
}

inline fixed4 LightingMTELambert (SurfaceOutput s, UnityGI gi)
{
	fixed4 c;
	c = UnityLambertLight (s, gi.light);

#ifdef UNITY_LIGHT_FUNCTION_APPLY_INDIRECT
	c.rgb += s.Albedo * gi.indirect.diffuse;
#endif

	return c;
}

inline void LightingMTELambert_GI (
	SurfaceOutput s,
	UnityGIInput data,
	inout UnityGI gi)
{
	gi = UnityGlobalIllumination (data, 1.0, s.Normal);
	gi.light.color *= GetGlobalShadowMapAttention(data.worldPos);
}

fixed3 MTE_NormalIntensity_fixed(fixed3 normal, fixed Strength)
{
	return lerp(normal, fixed3(0,0,1), - Strength + 1.0);
}


#endif // MTE_COMMON_CGINC_INCLUDED