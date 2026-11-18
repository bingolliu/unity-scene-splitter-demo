#include "GlobalShadowMap.cginc"
#include "UnityPBSLighting.cginc"
#include "Lighting.cginc"
#pragma target 3.0
#ifdef UNITY_PASS_SHADOWCASTER
    #undef INTERNAL_DATA
    #undef WorldReflectionVector
    #undef WorldNormalVector
    #define INTERNAL_DATA half3 internalSurfaceTtoW0; half3 internalSurfaceTtoW1; half3 internalSurfaceTtoW2;
    #define WorldReflectionVector(data,normal) reflect (data.worldRefl, half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal)))
    #define WorldNormalVector(data,normal) half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal))
#endif

struct Input
{
    float3 worldPos;
    float3 worldNormal;
    INTERNAL_DATA
    float2 uv_texcoord;
    float4 lightViewClipPos;
};

struct SurfaceOutputCustomLightingCustom
{
    half3 Albedo;
    half3 Normal;
    half3 Emission;
    half Metallic;
    half Smoothness;
    half Occlusion;
    half Alpha;
    Input SurfInput;
    UnityGIInput GIData;
};

void CustomVert(inout appdata_full v, out Input data)
{
    UNITY_INITIALIZE_OUTPUT(Input, data);

    float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    data.worldPos = worldPos;
    data.worldNormal = UnityObjectToWorldNormal(v.normal);
#ifdef USE_SCENE_SHADOW_MAP
    data.lightViewClipPos = mul(_LightViewProjection, float4(data.worldPos, 1));
#endif
}

inline void LightingStandardCustomLighting_GI( inout SurfaceOutputCustomLightingCustom s, UnityGIInput data, inout UnityGI gi )
{
    s.GIData = data;
    gi.light.color *= GetGlobalShadowMapAttentionByClipPos(s.SurfInput.lightViewClipPos);
}

void surf( Input i , inout SurfaceOutputCustomLightingCustom o )
{
    o.SurfInput = i;
    o.Normal = float3(0,0,1);
}

inline half3 Custom_IndirectSpecular(UnityGIInput data, half occlusion, Unity_GlossyEnvironmentData glossIn)
{
    half3 specular;

    #ifdef UNITY_SPECCUBE_BOX_PROJECTION
    // we will tweak reflUVW in glossIn directly (as we pass it to Unity_GlossyEnvironment twice for probe0 and probe1), so keep original to pass into BoxProjectedCubemapDirection
    half3 originalReflUVW = glossIn.reflUVW;
    glossIn.reflUVW = BoxProjectedCubemapDirection (originalReflUVW, data.worldPos, data.probePosition[0], data.boxMin[0], data.boxMax[0]);
    #endif

    #ifdef _GLOSSYREFLECTIONS_OFF
    specular = unity_IndirectSpecColor.rgb;
    #else
    half3 env0 = Unity_GlossyEnvironment (UNITY_PASS_TEXCUBE(unity_SpecCube0), data.probeHDR[0], glossIn);
    specular = env0;
    #endif

    return specular * occlusion;
}

inline UnityGI Custom_GlobalIllumination (UnityGIInput data, half occlusion, half3 normalWorld, Unity_GlossyEnvironmentData glossIn)
{
    UnityGI o_gi = UnityGI_Base(data, occlusion, normalWorld);
    o_gi.indirect.specular = Custom_IndirectSpecular(data, occlusion, glossIn);
    return o_gi;
}


inline UnityGI Custom_GlobalIllumination (UnityGIInput data, half occlusion, half smoothness, half3 normalWorld, bool reflections)
{
    if(reflections)
    {
        Unity_GlossyEnvironmentData g = UnityGlossyEnvironmentSetup(smoothness, data.worldViewDir, normalWorld, float3(0, 0, 0));
        return Custom_GlobalIllumination(data, occlusion, normalWorld, g);
    }
    else
    {
        return UnityGlobalIllumination(data, occlusion, normalWorld);
    }
}