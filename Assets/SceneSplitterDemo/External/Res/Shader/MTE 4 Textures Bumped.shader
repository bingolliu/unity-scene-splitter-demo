Shader "MTE/Standard/4 Textures/Bumped"
{
    Properties
    {
        _Control ("Control (RGBA)", 2D) = "red" {}
        _Splat0 ("Layer 1", 2D) = "white" {}
        _Splat1 ("Layer 2", 2D) = "white" {}
        _Splat2 ("Layer 3", 2D) = "white" {}
        _Splat3 ("Layer 4", 2D) = "white" {}

        _Normal0 ("Normalmap 1", 2D) = "bump" {}
        _Normal1 ("Normalmap 2", 2D) = "bump" {}
        _Normal2 ("Normalmap 3", 2D) = "bump" {}
        _Normal3 ("Normalmap 4", 2D) = "bump" {}

        [Gamma] _Metallic0("Metallic 0", Range(0.0, 1.0)) = 0.0
        [Gamma] _Metallic1("Metallic 1", Range(0.0, 1.0)) = 0.0
        [Gamma] _Metallic2("Metallic 2", Range(0.0, 1.0)) = 0.0
        [Gamma] _Metallic3("Metallic 3", Range(0.0, 1.0)) = 0.0
        _Smoothness0("Smoothness 0", Range(0.0, 1.0)) = 1.0
        _Smoothness1("Smoothness 1", Range(0.0, 1.0)) = 1.0
        _Smoothness2("Smoothness 2", Range(0.0, 1.0)) = 1.0
        _Smoothness3("Smoothness 3", Range(0.0, 1.0)) = 1.0
        
        _NormalIntensity0 ("Normal Intensity 0", Range(0.01, 10)) = 1.0
        _NormalIntensity1 ("Normal Intensity 1", Range(0.01, 10)) = 1.0
        _NormalIntensity2 ("Normal Intensity 2", Range(0.01, 10)) = 1.0
        _NormalIntensity3 ("Normal Intensity 3", Range(0.01, 10)) = 1.0
        
        [Toggle(SMOOTHNESS_TEXTURE)] SMOOTHNESS_TEXTURE ("Smoothness Texture", Float) = 0
    }

    SubShader//for target 3.0+
    {
        Tags
        {
            "Queue" = "Geometry-99"
            "RenderType" = "Opaque"
        }

        CGPROGRAM
        #pragma surface surf MTEStandard vertex:MTE_SplatmapVert finalcolor:MTE_SplatmapFinalColor finalprepass:MTE_SplatmapFinalPrepass finalgbuffer:MTE_SplatmapFinalGBuffer
        #pragma multi_compile_fog
        #pragma target 3.0
        #pragma shader_feature SMOOTHNESS_TEXTURE
        
        #define MTE_STANDARD_SHADER
        #include "MTE Common.cginc"

        sampler2D _Control;
        float4 _Control_ST;
        sampler2D _Splat0, _Splat1, _Splat2, _Splat3;
        float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
        sampler2D _Normal0, _Normal1, _Normal2, _Normal3;

        half _Metallic0;
        half _Metallic1;
        half _Metallic2;
        half _Metallic3;
        half _Smoothness0;
        half _Smoothness1;
        half _Smoothness2;
        half _Smoothness3;
        
        fixed _NormalIntensity0, _NormalIntensity1, _NormalIntensity2, _NormalIntensity3;
        

        void MTE_SplatmapVert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            data.tc_Control.xy = TRANSFORM_TEX(v.texcoord, _Control);
            data.tc_Splat01.xy = TRANSFORM_TEX(v.texcoord, _Splat0);
            data.tc_Splat01.zw = TRANSFORM_TEX(v.texcoord, _Splat1);
            data.tc_Splat23.xy = TRANSFORM_TEX(v.texcoord, _Splat2);
            data.tc_Splat23.zw = TRANSFORM_TEX(v.texcoord, _Splat3);
            float4 pos = UnityObjectToClipPos(v.vertex);
            UNITY_TRANSFER_FOG(data, pos);
        }

        void MTE_SplatmapMix(Input IN, half4 defaultAlpha, out half4 splat_control, out half weight,
                             out fixed4 mixedDiffuse, inout float3 mixedNormal)
        {
            splat_control = tex2D(_Control, IN.tc_Control.xy);
            weight = dot(splat_control, half4(1, 1, 1, 1));
            splat_control /= (weight + 1e-3f);

            mixedDiffuse = 0.0f;
            mixedDiffuse += splat_control.r * tex2D(_Splat0, IN.tc_Splat01.xy) * half4(1.0, 1.0, 1.0, defaultAlpha.r);
            mixedDiffuse += splat_control.g * tex2D(_Splat1, IN.tc_Splat01.zw) * half4(1.0, 1.0, 1.0, defaultAlpha.g);
            mixedDiffuse += splat_control.b * tex2D(_Splat2, IN.tc_Splat23.xy) * half4(1.0, 1.0, 1.0, defaultAlpha.b);
            mixedDiffuse += splat_control.a * tex2D(_Splat3, IN.tc_Splat23.zw) * half4(1.0, 1.0, 1.0, defaultAlpha.a);

            fixed3 nrm0 = splat_control.r * UnpackNormalWithScale(tex2D(_Normal0, IN.tc_Splat01.xy), _NormalIntensity0);
            fixed3 nrm1 = splat_control.g * UnpackNormalWithScale(tex2D(_Normal1, IN.tc_Splat01.zw), _NormalIntensity1);
            fixed3 nrm2 = splat_control.b * UnpackNormalWithScale(tex2D(_Normal2, IN.tc_Splat23.xy), _NormalIntensity2);
            fixed3 nrm3 = splat_control.a * UnpackNormalWithScale(tex2D(_Normal3, IN.tc_Splat23.zw), _NormalIntensity3);
            mixedNormal = normalize(nrm0 + nrm1 + nrm2 + nrm3);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 splat_control;
            fixed4 mixedDiffuse;
            half weight;
            half4 defaultSmoothness = half4(_Smoothness0, _Smoothness1, _Smoothness2, _Smoothness3);
            MTE_SplatmapMix(IN, defaultSmoothness, splat_control, weight, mixedDiffuse, o.Normal);
            o.Albedo = mixedDiffuse.rgb;
            o.Alpha = weight;
            o.Smoothness = mixedDiffuse.a;
            o.Metallic = dot(splat_control, half4(_Metallic0, _Metallic1, _Metallic2, _Metallic3));
        }
        ENDCG

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing // allow instanced shadow pass for most of the shaders
            #include "UnityCG.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG

        }
    }

    FallBack "MTE/Standard/4 Textures/Diffuse"
    CustomEditor "MTEShaderGUI"
}