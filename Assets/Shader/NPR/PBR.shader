Shader "URP/URPPBR"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_Tint("Tint", Color) = (1 ,1 ,1 ,1)
		[Gamma] _Metallic("Metallic", Range(0, 1)) = 0 //金属度要经过伽马校正
		_Smoothness("Smoothness", Range(0, 1)) = 0.5
		_LUT("LUT", 2D) = "white" {}
	}

	SubShader
	{
		Tags {
		"RenderType" = "Opaque"
		"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Tags {
				"LightMode" = "UniversalForward"
			}

			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			#define HALF_MIN_SQRT 0.0078125
			#define HALF_MIN 6.103515625e-5
			#define PI 3.1415926

			CBUFFER_START(UnityPerMaterial)
				float4 _Tint;
				float _Metallic;
				float _Smoothness;
				float4 _MainTex_ST;
			CBUFFER_END

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);
			TEXTURE2D(_LUT);
			SAMPLER(sampler_LUT);

			struct a2v
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float3 positionWS : TEXCOORD2;
			};


			v2f vert(a2v v)
			{
				v2f o;
				o.positionCS = TransformObjectToHClip(v.positionOS);
				o.positionWS = TransformObjectToWorld(v.positionOS);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.normalWS = TransformObjectToWorldNormal(v.normalOS);
				o.normalWS = normalize(o.normalWS);
				return o;
			}

			half4 frag(v2f i) : SV_Target
			{
        //入射光方向、观察方向、物体法线方向、半角方向
		        float4 kDieletricSpec = float4(0.04, 0.04, 0.04, 1.0);
				
				Light mainLight = GetMainLight();
				float3 normalWS = normalize(i.normalWS);
				float3 lightDirWS = mainLight.direction;
				float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz-i.positionWS.xyz);
				float3 halfVector = normalize(lightDirWS+viewDirWS);
      
				float nDotL = max(0, dot(normalWS, lightDirWS));
				float nDotV = max(0, dot(normalWS, viewDirWS));
				float vDotH = max(0, dot(halfVector, viewDirWS));
				float lDotH = max(0, dot(halfVector, lightDirWS));
				float lDotH2 = max(0.1f, pow(lDotH,2));
				float nDotH = max(0, dot(normalWS, halfVector));
        
        // Brdf直接光
				float3 albedo = _Tint * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
				float Kd = kDieletricSpec.a - _Metallic * kDieletricSpec.a;
        
				float perceptualRoughness = 1 - _Smoothness;
				float roughness = max(perceptualRoughness * perceptualRoughness, HALF_MIN_SQRT);
				float roughness2 = max(roughness*roughness, HALF_MIN);
        
        // D函数
				float divisorD = pow(nDotH*nDotH*(roughness2-1)+1.00001f,2);
				float D = roughness2/divisorD;
        // G函数
				float V = 1/(lDotH2*(roughness+0.5));
				float Fspec = D*V;

				float Ks = lerp(kDieletricSpec.rgb, albedo, _Metallic);
      
				float3 diffColor = Kd * albedo * mainLight.color * nDotL;
				float3 specColor = Ks * albedo * Fspec * mainLight.color * nDotL;
				float3 DirectLightResult = diffColor + specColor;
        
        //Brdf间接光
        //计算像素的所有方向的积分
				half3 ambient_GI = SampleSH(normalWS);
        //间接光漫反射
				float3 iblDiffuseResult = ambient_GI * Kd * albedo;
        
        //间接光镜面反射
				float mip_roughness = perceptualRoughness * (1.7 - 0.7 * perceptualRoughness);
				float3 refDirWS = reflect(-viewDirWS, normalWS);
				half mip = mip_roughness * UNITY_SPECCUBE_LOD_STEPS;
				half4 encodedIrradiance = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, refDirWS, mip);
        
        //iblSpecular是间接镜面反射的颜色。它表示物体表面反射的环境光中的镜面反射部分。
				float3 iblSpecular = albedo * DecodeHDREnvironment(encodedIrradiance, unity_SpecCube0_HDR);
        
        //LUT
        //表面减少因子 (surfaceReduction): 这个因子用于减少高粗糙度表面的镜面反射强度，使其更加自然。
				float surfaceReduction = 1.0 / (roughness2 + 1.0);
        //当视线接近物体表面时（即视线与表面法线之间的角度接近90度），镜面反射的强度会增加。
				float grazingTerm = saturate(_Smoothness + 1 - Kd);
        //菲涅耳效应描述了当光线从一种介质进入另一种介质时，反射强度的变化。
				float fresnelTerm = pow(1.0 - nDotV, 4);
        //iblBrdf 是间接镜面反射的 BRDF（双向反射分布函数）值。
        //它用于调整间接镜面反射的颜色和强度，考虑了表面粗糙度、边缘效应和菲涅耳效应等因素。
				half3 iblBrdf = surfaceReduction * lerp(Ks, grazingTerm, fresnelTerm);

				float3 iblSpecularResult = iblSpecular * iblBrdf;
				float3 IndirectResult = iblDiffuseResult + iblSpecularResult;

				float4 result = float4(DirectLightResult + IndirectResult, 1);

				return result;
			}
			ENDHLSL
		}
	}
}