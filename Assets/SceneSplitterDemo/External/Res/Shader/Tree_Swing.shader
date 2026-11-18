// Upgrade NOTE: commented out 'half4 unity_LightmapST', a built-in variable
// Upgrade NOTE: commented out 'sampler2D unity_Lightmap', a built-in variable
// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'
// Upgrade NOTE: replaced tex2D unity_Lightmap with UNITY_SAMPLE_TEX2D

Shader "effect/Tree_Swing" {
	Properties {
        _MainTex ("Grass Texture", 2D) = "white" {}

        _Scope("Scope", Range(0.01, 1)) = 0.311
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.175
        _TimeScale ("Time Scale", Range(0.5,2)) = 1
	} 

	SubShader
	{  /////////////////////   高显卡配置 LOD 400
		Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True"}
        LOD 300 
        
        Pass { 
            Tags { "LightMode"="ForwardBase" }
            Cull Off
        
            CGPROGRAM
            
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
            #pragma multi_compile SHADOWS_SHADOWMASK
            #pragma multi_compile_instancing
            
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            
            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _Scope;
            fixed _Cutoff; 
            fixed _TimeScale; 

            
            struct a2v {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 vertexColor : COLOR;
                half4 texcoord : TEXCOORD0;
                #ifndef LIGHTMAP_OFF
                    float2 texcoord1 :TEXCOORD1;
                #endif
            	UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                fixed3 worldNormal : TEXCOORD1;
                half2 uv : TEXCOORD2;
                SHADOW_COORDS(4)

                #ifndef LIGHTMAP_OFF
                    half2 uv2 :TEXCOORD5; //linghtMap uv
                #endif
                UNITY_FOG_COORDS(3)
            	UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            v2f vert(a2v v) {
                v2f o;
            	UNITY_SETUP_INSTANCE_ID(v);
                half4 offset = float4(0,0,0,0);
                offset.x = v.vertexColor.r * sin(_TimeScale * _Time.y * 2 + 0.02 * float(mul(unity_ObjectToWorld, v.vertex).r * mul(unity_ObjectToWorld, v.vertex).g )) * _Scope * 0.1 * 3;

                o.pos = UnityObjectToClipPos(v.vertex + offset);

                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                #ifndef LIGHTMAP_OFF
                    o.uv2 = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
                #endif
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o,o.pos);
                
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target {
            	UNITY_SETUP_INSTANCE_ID(i);
            	
                fixed4 mainTex = tex2D(_MainTex, i.uv);
                fixed3 worldNormal = normalize(i.worldNormal);
                fixed3 worldLightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));

            	float atten = SHADOW_ATTENUATION(i);

                half ase_lightAtten  = atten;//=atten ;
                //#if defined( HANDLE_SHADOWS_BLENDING_IN_GI)
                #ifndef LIGHTMAP_OFF
                    half bakedAtten = UnitySampleBakedOcclusion(i.uv2, i.worldPos);
                    float zDist = dot(_WorldSpaceCameraPos - i.worldPos, UNITY_MATRIX_V[2].xyz);
                    float fadeDist = UnityComputeShadowFadeDistance(i.worldPos, zDist);
                    ase_lightAtten = UnityMixRealtimeAndBakedShadows(atten, bakedAtten, UnityComputeShadowFade(fadeDist));
                #endif
                clip(mainTex.a - _Cutoff);
                
                fixed3 diffuse = _LightColor0.rgb * mainTex.rgb * saturate(dot(worldNormal, worldLightDir));

                fixed3 color =  diffuse * ase_lightAtten;

                
                #ifndef LIGHTMAP_OFF
                    fixed3 lightmap = DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, i.uv2));
                    mainTex.rgb *=  lightmap ;
                #endif
                color = mainTex.xyz + color;

                UNITY_APPLY_FOG(i.fogCoord, color);

                return fixed4(color, 1);
            }
            
            ENDCG
        }

        Pass 
        {
		    Tags { "LightMode" = "ShadowCaster" }
		   
		    Offset 1,1
		 
		    CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_shadowcaster
			#pragma fragmentoption ARB_precision_hint_fastest
		    #pragma multi_compile_instancing
			#include "UnityCG.cginc" 

			struct a2v {
                float4 vertex : POSITION;
                fixed4 vertexColor : COLOR;
                half4 texcoord : TEXCOORD0;
            	UNITY_VERTEX_INPUT_INSTANCE_ID
            };

			struct v2f { 
				V2F_SHADOW_CASTER;
				float2  uv : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
		 
			uniform float4 _MainTex_ST;
			uniform sampler2D _MainTex;
            float _Scope;
            fixed _Cutoff; 
            fixed _TimeScale; 
		 
			v2f vert (a2v v) {
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
                // float offset = v.vertexColor.r * sin(_TimeScale * _Time.y + 0.02 * float(mul(unity_ObjectToWorld, v.vertex).r * mul(unity_ObjectToWorld, v.vertex).g )) * _Scope * 0.1;
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                // v.vertex.x += offset;
				TRANSFER_SHADOW_CASTER(o)
				return o;
			}
		 
		 
			float4 frag (v2f i) : COLOR {
				UNITY_SETUP_INSTANCE_ID(i);
				fixed4 texcol = tex2D(_MainTex, i.uv);
				clip(texcol.a - _Cutoff);
				SHADOW_CASTER_FRAGMENT(i)
			}
			ENDCG

        }

    }
    
	
////////////////////低配置显卡 LOD 200
	SubShader{
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True"}
		LOD 200 
		Pass{
            Tags { "LightMode"="ForwardBase" }
			Cull Off 

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc" 
			#include "Lighting.cginc"
            #include "AutoLight.cginc"
			#pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile_fwdbase
            #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
			#pragma multi_compile_instancing

			sampler2D _MainTex; 
            fixed _Cutoff;
			float4 _MainTex_ST;

			struct a2v {
				half4 vertex : POSITION;
				half4 texcoord : TEXCOORD0;
				float3 normal : NORMAL;
				#ifndef LIGHTMAP_OFF
					float2 texcoord1 :TEXCOORD1;
				#endif
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			struct v2f {
				half4 pos : SV_POSITION;
				half2 uv : TEXCOORD0;
				fixed3 worldNormal : TEXCOORD1;
			#ifndef LIGHTMAP_OFF
				half2 uv2 :TEXCOORD2; //linghtMap uv
			#endif
                fixed3 worldPos : TEXCOORD3;
                SHADOW_COORDS(4)
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			v2f vert(a2v v){
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.texcoord.xy;
			#ifndef LIGHTMAP_OFF
				o.uv2 = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
			#endif
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target{
				UNITY_SETUP_INSTANCE_ID(i);
				fixed4 mainTex = tex2D(_MainTex, i.uv);
                fixed3 worldLightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));

				fixed3 worldNormal = normalize(i.worldNormal);
				fixed3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                fixed3 diffuse = _LightColor0.rgb * mainTex.rgb * saturate(dot(worldNormal, worldLightDir));

                float atten = SHADOW_ATTENUATION(i);
                half ase_lightAtten  = atten;
            #ifndef LIGHTMAP_OFF
                half bakedAtten = UnitySampleBakedOcclusion(i.uv2, i.worldPos);
                float zDist = dot(_WorldSpaceCameraPos - i.worldPos, UNITY_MATRIX_V[2].xyz);
                float fadeDist = UnityComputeShadowFadeDistance(i.worldPos, zDist);
                ase_lightAtten = UnityMixRealtimeAndBakedShadows(atten, bakedAtten, UnityComputeShadowFade(fadeDist));

                fixed3 lightmap = DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, i.uv2));
                mainTex.rgb *=  lightmap ;
            #endif


				mainTex.rgb += diffuse * ase_lightAtten;
				fixed4 c = fixed4(mainTex.rgb, 1);
				clip(mainTex.a - _Cutoff);
				return c;
			}
			ENDCG
		} // pass end
	}// subshader end
}
