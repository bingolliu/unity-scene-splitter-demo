#ifndef GLOBAL_SHADOW_INCLUDED
#define GLOBAL_SHADOW_INCLUDED

#include "Common.cginc"

#pragma multi_compile_local USE_SCENE_SHADOW_MAP

//全局阴影变量
uniform float _ShadowStrength;
uniform float4x4 _LightViewProjection;
uniform sampler2D _ShadowTexture;
uniform float4 _ShadowTexture_TexelSize;

float GetPCFShadow(float selfDepth, float2 remapUV)
{
    float shadow = 0.0;
    int samples = 0;
    for (int x=-2;x<=2;++x)
    {
        for(int y=-2;y<=2;++y)
        {
            float sampleDepth = tex2D(_ShadowTexture, remapUV+float2(x,y)* _ShadowTexture_TexelSize.xy).x;
            shadow += step(selfDepth, sampleDepth);
            samples++;
        }
    }
    return lerp(1, 1 - _ShadowStrength, shadow/samples);
}

//硬阴影
float GetHardShadow(float selfDepth, float2 remapUV)
{
    float shadowDepth = tex2D(_ShadowTexture, remapUV).x;
    float2 uvInRange = CheckUVInRange(remapUV, float2(0, 1));
    shadowDepth = lerp(0, shadowDepth, uvInRange);

    float shadow = step(selfDepth, shadowDepth);
    return lerp(1, 1 - _ShadowStrength, shadow);
}

//软阴影（费性能，不使用）
float GetSoftShadow(float selfDepth, float2 remapUV)
{
    float2 uvInRange = CheckUVInRange(remapUV, float2(0, 1));
    float shadow = 0;
    shadow += step(selfDepth, lerp(0, tex2D(_ShadowTexture, remapUV + float2(0, 1) * _ShadowTexture_TexelSize.xy).x, uvInRange));
    shadow += step(selfDepth, lerp(0, tex2D(_ShadowTexture, remapUV + float2(0, -1) * _ShadowTexture_TexelSize.xy).x, uvInRange));
    shadow += step(selfDepth, lerp(0, tex2D(_ShadowTexture, remapUV + float2(1, 0) * _ShadowTexture_TexelSize.xy).x, uvInRange));
    shadow += step(selfDepth, lerp(0, tex2D(_ShadowTexture, remapUV + float2(-1, 0) * _ShadowTexture_TexelSize.xy).x, uvInRange));
    shadow /= 4.0;
    return lerp(1, 1 - _ShadowStrength, shadow);
}

float GetGlobalShadowMapAttentionByClipPos_DX(float4 clipPos)
{
    float shadowMapAtten = 1;
#ifdef USE_SCENE_SHADOW_MAP
    float4 lightViewClipPos = clipPos;
    //变换到NDC空间
    lightViewClipPos /= lightViewClipPos.w;
    //在裁剪空间中，坐标的范围是[-1, 1]，无论是在DirectX还是OpenGL中。这是因为在进行透视除法后，所有可见的几何形状都会被映射到这个范围内
    // 因为Texture采样的范围是0-1，而我们转换到ClipSpace的坐标是-1到1，所以需要+1再除以2映射到0-1
    float2 remapUV = (lightViewClipPos.xy + 1) / 2;
    // 比较采样出来的深度，如果自己的深度比采样的小，说明离的相机越远（DX和OpenGL需要做区别，这里是DX情况下）
    // 如果出现这样的情况，说明自己在光源视角下被什么东西挡住了，所以光照需要衰减，这里直接减一半
    // 跟计算深度的时候一个计算方法
    float selfDepth = lightViewClipPos.z;
    shadowMapAtten = GetHardShadow(selfDepth, remapUV);
#endif

    return shadowMapAtten;
}

float GetGlobalShadowMapAttentionByClipPos_OpenGL(float4 clipPos)
{
    float shadowMapAtten = 1;
#ifdef USE_SCENE_SHADOW_MAP
    float4 lightViewClipPos = clipPos;
    //变换到NDC空间
    lightViewClipPos /= lightViewClipPos.w;
    //在裁剪空间中，坐标的范围是[-1, 1]，无论是在DirectX还是OpenGL中。这是因为在进行透视除法后，所有可见的几何形状都会被映射到这个范围内
    // 因为Texture采样的范围是0-1，而我们转换到ClipSpace的坐标是-1到1，所以需要+1再除以2映射到0-1
    float2 remapUV = (lightViewClipPos.xy + 1) / 2;
    // 比较采样出来的深度，如果自己的深度比采样的小，说明离的相机越远（DX和OpenGL需要做区别，这里是DX情况下）
    // 如果出现这样的情况，说明自己在光源视角下被什么东西挡住了，所以光照需要衰减，这里直接减一半
    // 跟计算深度的时候一个计算方法，要从 -1到1 映射至 0到1
    float selfDepth = lightViewClipPos.z;
    selfDepth = Remap(selfDepth, float2(-1, 1), float2(0, 1));
    //映射后还需要1-selfDepth，原因如下：
    //dx中，深度缓冲区中1代表近平面，深度缓冲区中0代表远平面;
    //opengl中，深度缓冲区0代表近平面，1代表远平面，故需要1-selfDepth来得出dx的值
    selfDepth = 1 - selfDepth;
    shadowMapAtten = GetHardShadow(selfDepth, remapUV);
#endif

    return shadowMapAtten;
}

float GetGlobalShadowMapAttention_DX(float3 worldPos)
{
    float shadowMapAtten = 1;
#ifdef USE_SCENE_SHADOW_MAP
    float4 lightViewClipPos = mul(_LightViewProjection, float4(worldPos, 1));
    shadowMapAtten = GetGlobalShadowMapAttentionByClipPos_DX(lightViewClipPos);
#endif
    return shadowMapAtten;
}

float GetGlobalShadowMapAttention_OpenGL(float3 worldPos)
{
    float shadowMapAtten = 1;
#ifdef USE_SCENE_SHADOW_MAP
    float4 lightViewClipPos = mul(_LightViewProjection, float4(worldPos, 1));
    shadowMapAtten = GetGlobalShadowMapAttentionByClipPos_OpenGL(lightViewClipPos);
#endif
    return shadowMapAtten;
}

float GetGlobalShadowMapAttention(float3 worldPos)
{
#if UNITY_UV_STARTS_AT_TOP
    float shadowMapAtten = GetGlobalShadowMapAttention_DX(worldPos);
#else
    float shadowMapAtten = GetGlobalShadowMapAttention_OpenGL(worldPos);
#endif

    return shadowMapAtten;
}

float GetGlobalShadowMapAttentionByClipPos(float4 clipPos)
{
#if UNITY_UV_STARTS_AT_TOP
    float shadowMapAtten = GetGlobalShadowMapAttentionByClipPos_DX(clipPos);
#else
    float shadowMapAtten = GetGlobalShadowMapAttentionByClipPos_OpenGL(clipPos);
#endif

    return shadowMapAtten;
}

#endif // GLOBAL_SHADOW_INCLUDED