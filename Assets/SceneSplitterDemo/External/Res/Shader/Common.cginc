#ifndef COMMON_CG_INCLUDED
#define COMMON_CG_INCLUDED

#include "UnityCG.cginc"
#define HALF_MIN 0.001

float2 GradientNoise_Dir_float(float2 p)
{
    // Permutation and hashing used in webgl-nosie goo.gl/pX7HtC
    p = p % 289;
    // need full precision, otherwise half overflows when p > 1
    float x = float(34 * p.x + 1) * p.x % 289 + p.y;
    x = (34 * x + 1) * x % 289;
    x = frac(x / 41) * 2 - 1;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

float GradientNoise_float(float2 UV, float Scale)
{
    float2 p = UV * Scale;
    float2 ip = floor(p);
    float2 fp = frac(p);
    float d00 = dot(GradientNoise_Dir_float(ip), fp);
    float d01 = dot(GradientNoise_Dir_float(ip + float2(0, 1)), fp - float2(0, 1));
    float d10 = dot(GradientNoise_Dir_float(ip + float2(1, 0)), fp - float2(1, 0));
    float d11 = dot(GradientNoise_Dir_float(ip + float2(1, 1)), fp - float2(1, 1));
    fp = fp * fp * fp * (fp * (fp * 6 - 15) + 10);
    return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x) + 0.5;
}

float2 TilingAndOffset(float2 UV, float2 Tiling, float2 Offset)
{
    return UV * Tiling + Offset;
}

float3 Rotate_About_Axis_Degrees(float3 In, float3 Axis, float Rotation)
{
    Rotation = radians(Rotation);

    float s = sin(Rotation);
    float c = cos(Rotation);
    float one_minus_c = 1.0 - c;

    Axis = normalize(Axis);

    float3x3 rot_mat = { one_minus_c * Axis.x * Axis.x + c,            one_minus_c * Axis.x * Axis.y - Axis.z * s,     one_minus_c * Axis.z * Axis.x + Axis.y * s,
                              one_minus_c * Axis.x * Axis.y + Axis.z * s,   one_minus_c * Axis.y * Axis.y + c,              one_minus_c * Axis.y * Axis.z - Axis.x * s,
                              one_minus_c * Axis.z * Axis.x - Axis.y * s,   one_minus_c * Axis.y * Axis.z + Axis.x * s,     one_minus_c * Axis.z * Axis.z + c
                            };

    return mul(rot_mat,  In);
}

float Remap(float In, float2 InMinMax, float2 OutMinMax)
{
    return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
}

float FresnelEffect(float3 Normal, float3 ViewDir, float Power)
{
    return pow((1.0 - saturate(dot(normalize(Normal), normalize(ViewDir)))), Power);
}

//置灰处理
fixed4 SetGray(fixed4 color, float rate)
{
    float grey = dot(color.rgb, float3(0.299, 0.587, 0.114));
    color.rgb = lerp(color.rgb, float3(grey, grey, grey), rate);

    return color;
}

float CheckUVInRange(float2 uv, float2 range)
{
    float xInRange = step(range.x, uv.x) * step(uv.x, range.y);
    float yInRange = step(range.x, uv.y) * step(uv.y, range.y);
    float inRange = xInRange * yInRange;
    return lerp(0, 1, inRange);
}

float3 SafeNormalize(float3 inVec)
{
    float dp3 = max(0.001f, dot(inVec, inVec));
    return inVec * rsqrt(dp3);
}

// TBN 工具函数集合

#define TBN_WORLD_COORDS3x3(idx1, idx2, idx3)\
            float3 tangentToWorld0 : TEXCOORD##idx1;\
            float3 tangentToWorld1 : TEXCOORD##idx2;\
            float3 tangentToWorld2 : TEXCOORD##idx3; 

#define TBN_WORLD_COORDS(idx1, idx2, idx3)\
            float4 tangentToWorld0 : TEXCOORD##idx1;\
            float4 tangentToWorld1 : TEXCOORD##idx2;\
            float4 tangentToWorld2 : TEXCOORD##idx3; 

#define TBN_LIGHT_COORDS(idx1, idx2, idx3)\
            half4 tangentToWorld0 : TEXCOORD##idx1;\
            half4 tangentToWorld1 : TEXCOORD##idx2;\
            half4 tangentToWorld2 : TEXCOORD##idx3; 

#define TBN_VIEWDIR_COORDS(idx1, idx2, idx3)\
            half4 tangentToWorld0 : TEXCOORD##idx1;\
            half4 tangentToWorld1 : TEXCOORD##idx2;\
            half4 tangentToWorld2 : TEXCOORD##idx3;                             

#define TRANSFER_TBN_ROTATION(o, worldTangent, worldBinormal, worldNormal)\
            o.tangentToWorld0.xyz = worldTangent;\
            o.tangentToWorld1.xyz = worldBinormal;\
            o.tangentToWorld2.xyz = worldNormal;

#define TRANSFER_TBN_ROTATION3x3(o, worldPos, worldBinormal, worldNormal)\
            o.tangentToWorld0.xyz = worldPos;\
            o.tangentToWorld1.xyz = worldBinormal;\
            o.tangentToWorld2.xyz = worldNormal;

#define TRANSFER_WORLD_POS(o, worldPos)\
            o.tangentToWorld0.w = worldPos.x;\
            o.tangentToWorld1.w = worldPos.y;\
            o.tangentToWorld2.w = worldPos.z;    

#define TRANSFER_LIGHT_DIR(o, lightDir)\
            o.tangentToWorld0.w = lightDir.x;\
            o.tangentToWorld1.w = lightDir.y;\
            o.tangentToWorld2.w = lightDir.z;       

#define TRANSFER_VIEW_DIR(o, viewDir)\
            o.tangentToWorld0.w = viewDir.x;\
            o.tangentToWorld1.w = viewDir.y;\
            o.tangentToWorld2.w = viewDir.z;                               

#define UNPACK_WORLD_NORMAL(_BumpMap, uv, i) PerPixelWorldNormal(_BumpMap, uv, i.tangentToWorld0, i.tangentToWorld1, i.tangentToWorld2);
#define UNITY_WORLD_POS(i) UnPackWorldPos(i.tangentToWorld0, i.tangentToWorld1, i.tangentToWorld2);        
#define UNITY_WORLD_POS3x3(i) (i.tangentToWorld0);  
#define UNITY_WORLD_TANGENT(i) i.tangentToWorld0.xyz;
#define UNITY_WORLD_BINORMAL(i) i.tangentToWorld1.xyz;
#define UNITY_WORLD_NORMAL(i) i.tangentToWorld2.xyz;
#define UNITY_WORLD_LIGHT(i) UnPackNormalizeDir(i.tangentToWorld0, i.tangentToWorld1, i.tangentToWorld2);                                    
#define UNITY_WORLD_VIEWDIR(i) UnPackNormalizeDir(i.tangentToWorld0, i.tangentToWorld1, i.tangentToWorld2);


#define XColorSpaceDielectricSpec half4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)

#define XColorSpaceDielectricSpecHair half4(0.08, 0.08, 0.08, 1.0 - 0.08) // standard dielectric reflectivity coef at incident angle (= 4%)


inline half Min3(half3 x) 
{ 
	return min(x.x, min(x.y, x.z)); 
}

inline half Min3(half x, half y, half z) 
{ 
	return min(x, min(y, z)); 
}

inline half Max3(half3 x) 
{
	return max(x.x, max(x.y, x.z)); 
}

inline half Max3(half x, half y, half z) 
{
	return max(x, max(y, z)); 
}

inline half  Pow2(half  x) 
{
	return x * x; 
}

inline half2 Pow2(half2 x) 
{
	return x * x; 
}

inline half3 Pow2(half3 x) 
{
	return x * x; 
}

inline half4 Pow2(half4 x)
{
	return x * x; 
}


half3 XGammaToLinearSpace(half3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA 
	return GammaToLinearSpace(c.rgb);
#else
	return c.rgb;
#endif
}

half3 XLinearToGammaSpace(half3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA 
	return LinearToGammaSpace(c.rgb);
#else
	return c.rgb;
#endif
}

half3 Gamma20ToLinear(half3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA 
	return c.rgb * c.rgb;
#else
	return c.rgb;
#endif
}

half3 LinearToGamma20(half3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA 
	return sqrt(c.rgb);
#else
	return c.rgb;
#endif
}

//从TBN顶点数据提出worldPos
inline float3 UnPackWorldPos(float4 T, float4 B, float4 N)
{
    return float3(T.w, B.w, N.w);   
}

//从TBN顶点数据提出Dir数据
inline half3 UnPackNormalizeDir(half4 T, half4 B, half4 N)
{
    return normalize(half3(T.w, B.w, N.w));   
}

half3 XUnpackScaleNormalRGorAG(half4 packednormal, half bumpScale)
{
#if defined(UNITY_NO_DXT5nm)
	half3 normal = packednormal.xyz * 2 - 1;
	//#if (SHADER_TARGET >= 30)
	//    // SM2.0: instruction count limitation
	//    // SM2.0: normal scaler is not supported
	//    normal.xy *= bumpScale;
	//#endif
	return normal;
#else
	// This do the trick
	packednormal.x *= packednormal.w;

	half3 normal;
	normal.xy = (packednormal.xy * 2 - 1);
	//#if (SHADER_TARGET >= 30)
	//    // SM2.0: instruction count limitation
	//    // SM2.0: normal scaler is not supported
	//    normal.xy *= bumpScale;
	//#endif
	normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
	return normal;
#endif
}

half3 XUnpackScaleNormal(half4 packednormal, half bumpScale)
{
	return XUnpackScaleNormalRGorAG(packednormal, bumpScale);
}


//mobile不支持half4[] 到 float4[] 转换
half3 PerPixelWorldNormal(sampler2D bumpMap, float2 uv, float4 tangentToWorld[3])
{
    half3 tangent = tangentToWorld[0].xyz;
    half3 binormal = tangentToWorld[1].xyz;
    half3 normal = tangentToWorld[2].xyz;

    half3 normalTangent = XUnpackScaleNormal(tex2D(bumpMap, uv), 1.0);
    return normalize(tangent * normalTangent.x + binormal * normalTangent.y + normal * normalTangent.z);    
}

half3 PerPixelWorldNormal(sampler2D bumpMap, float2 uv, half3 tangent, half3 binormal, half3 normal)
{
    half3 normalTangent = XUnpackScaleNormal(tex2D(bumpMap, uv), 1.0);
    return normalize(tangent * normalTangent.x + binormal * normalTangent.y + normal * normalTangent.z);    
}

// Decodes HDR textures
// handles dLDR, RGBM formats
inline half3 XDecodeHDR(half4 data, half4 decodeInstructions)
{
	// Take into account texture alpha if decodeInstructions.w is true(the alpha value affects the RGB channels)
	half alpha = decodeInstructions.w * (data.a - 1.0) + 1.0;

	// If Linear mode is not supported we can skip exponent part
	#if defined(UNITY_COLORSPACE_GAMMA)
	return (decodeInstructions.x * alpha) * data.rgb;
	#else
	#if defined(UNITY_USE_NATIVE_HDR)
	return decodeInstructions.x * data.rgb; // Multiplier for future HDRI relative to absolute conversion.
	#else
	return (decodeInstructions.x * pow(alpha, decodeInstructions.y)) * data.rgb;
	#endif
	#endif
}

half3 XGamma20ToLinear(half3 c)
{
	#ifdef UNITY_COLORSPACE_GAMMA 
	return c.rgb * c.rgb;
	#else
	return c.rgb;
	#endif
}

half3 XLinearToGamma20(half3 c)
{
	#ifdef UNITY_COLORSPACE_GAMMA 
	return sqrt(c.rgb);
	#else
	return c.rgb;
	#endif
}


float2 CalPolarUV(float2 texcoord, float4 tex_ST, float2 uvOffset, float speed)
{
	float2 offsetUV = texcoord * tex_ST.xy - 0.5;
	float theta = atan2(offsetUV.y, offsetUV.x);
	theta = theta/UNITY_PI * 0.5 + 0.5;
	float r = length(offsetUV) + _Time.y*speed + uvOffset;
	return float2(r,theta);
}

#define XEncodeHDR(col) half4(clamp(col.rgb, HALF_MIN, 56), saturate(col.a))

#endif //COMMON_CG_INCLUDED