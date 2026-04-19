Shader "URP/NPR&PBR"
{
	Properties
	{
		_Color ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Range(0.001, 0.1)) = 0.003
		_OffsetX ("Offset X", Range(0, 1)) = 0.3
        _OffsetY ("Offset Y", Range(0, 1)) = 0.3
		_ZOffset ("Z Offset", Range(0, 1)) = 0.05
		_RimIntensity("RimIntensity", Range(0, 5)) = 0.05
		_RimPower("RimPower", Range(0, 5)) = 0.05
		_RimColor("RimColor", Color) = (1, 1, 1, 1)
		_MainTex("Texture", 2D) = "white" {}
		_Tint("Tint", Color) = (1 ,1 ,1 ,1)
		[Gamma] _Metallic("Metallic", Range(0, 1)) = 0 //金属度要经过伽马校正
		_Smoothness("Smoothness", Range(0, 1)) = 0.5
		_LUT("LUT", 2D) = "white" {}
		_AO("AO", 2D) = "white" {}
		_1stColorStep("threshold1", Range(0, 1)) = 0.5 
		_1stColorFeature("Width1", Range(0, 1)) = 0.3
		_set1stShaderColor("Color1", Color) = (0.5, 0.5, 0.5, 1)
		_2ndColorStep("threshold2", Range(0, 1)) = 0.3 
		_2ndColorFeature("Width2", Range(0, 1)) = 0.2
		_set2ndShaderColor("Color2", Color) = (0.3, 0.3, 0.3, 1)
	}

	SubShader
	{
		Tags {
		"RenderType" = "Opaque"
		"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			#define HALF_MIN_SQRT 0.0078125
			#define HALF_MIN 6.103515625e-5
			#define PI 3.1415926
			#define INV_PI 0.31830988618f 

			CBUFFER_START(UnityPerMaterial)
			    float _RimIntensity;
				float _RimPower;
				float4 _RimColor;
				float4 _Tint;
				float _Metallic;
				float _Smoothness;
				float4 _MainTex_ST;
				float _1stColorStep;
		        float _1stColorFeature;
		        float4 _set1stShaderColor;
		        float _2ndColorStep;
		        float _2ndColorFeature;
		        float4 _set2ndShaderColor;
			CBUFFER_END

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);
			TEXTURE2D(_LUT);
			SAMPLER(sampler_LUT);
			TEXTURE2D(_AO);
			SAMPLER(sampler_AO);

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
            
			float3 PBRDirectDiffuse(float3 mainLightColor, float3 normalWS, float3 lightDirWS, float3 albedo)
			{
				float4 kDieletricSpec = float4(0.04, 0.04, 0.04, 1.0);

				//形成一个平滑过渡的暗面
				float nDotL = max(0, dot(normalWS, lightDirWS));
				float Kd = kDieletricSpec.a - _Metallic * kDieletricSpec.a;
				float3 diffColor = Kd * albedo * mainLightColor * nDotL;
	            return diffColor;
			}

			float3 NPRDirectDiffuse(float3 normalWS, float3 lightDirWS, float3 albedo)
			{
				//NPR中一般将明暗面的强烈色阶变化来营造动漫感
				//1stColorStep阴影开始变暗的起始阈值, 1stColorFeature阴影过渡区域的宽度
                float halfLambertDiffuse = dot(normalWS, lightDirWS) * 0.5 + 0.5;
				float set1stFinalShadowMask = saturate(1.0 + ((-halfLambertDiffuse + (_1stColorStep - _1stColorFeature))) / _1stColorFeature);
                float set2ndFinalShadowMask = saturate(1.0 + ((-halfLambertDiffuse + (_2ndColorStep - _2ndColorFeature))) / _2ndColorFeature);
				float3 setFinalBaseColor = lerp(albedo, lerp(_set1stShaderColor, _set2ndShaderColor, set2ndFinalShadowMask), set1stFinalShadowMask);
				return setFinalBaseColor;
			}

			float3 PBRDirectSpecular(float3 mainLightColor, float nDotL, float nDotH, float lDotH2, float roughness, float roughness2, float3 albedo)
			{
				float4 kDieletricSpec = float4(0.04, 0.04, 0.04, 1.0);
                //表现一些金属材质的时候表现不错
                //D函数
				float divisorD = pow(nDotH*nDotH*(roughness2-1)+1.00001f, 2);
				float D = roughness2/divisorD;
                //G函数
				float V = 1/(lDotH2*(roughness+0.5));
				//F函数
				float Fspec = D*V;
				float Ks = lerp(kDieletricSpec.rgb, albedo, _Metallic);
				float3 specColor = Ks * albedo * Fspec * mainLightColor * nDotL;
				return specColor;
			}

			float3 NPRDirectSpecular(float3 mainLightColor, float nDotL, float nDotH, float lDotH2, float roughness, float roughness2, float3 albedo, float3 ao)
			{
				float4 kDieletricSpec = float4(0.04, 0.04, 0.04, 1.0);
				//NPR中需要更加风格化的高光
				//对D项的修改比较多，主要对D项进行了重新映射，映射基于AO贴图中的B，R通道。
				float divisorD = pow(nDotH*nDotH*(roughness2-1)+1.00001f, ao.r);
				float D = INV_PI * roughness2/(divisorD * divisorD + 1e-7f);
                //G函数
				float V = 1/(lDotH2*(roughness+0.5));
				//F函数
				float Fspec = D*V;
				float Ks = lerp(kDieletricSpec.rgb, albedo, _Metallic);
				float3 specColor = Ks * albedo * Fspec * mainLightColor * nDotL;
				return specColor;
			}

			half3 PBRIndirectDiffuse(float3 normalWS, float3 albedo)
			{
				float4 kDieletricSpec = float4(0.04, 0.04, 0.04, 1.0);
				float Kd = kDieletricSpec.a - _Metallic * kDieletricSpec.a;
				half3 ambient_GI = SampleSH(normalWS);
				float3 iblDiffuseResult = ambient_GI * Kd * albedo;
				return iblDiffuseResult;
			}

			float3 PBRIndirectSpecular(float roughness2, float perceptualRoughness, float3 viewDirWS, float3 normalWS, float3 albedo)
			{
				float4 kDieletricSpec = float4(0.04, 0.04, 0.04, 1.0);
				float nDotV = max(0, dot(normalWS, viewDirWS));
				float Kd = kDieletricSpec.a - _Metallic * kDieletricSpec.a;
				float Ks = lerp(kDieletricSpec.rgb, albedo, _Metallic);
                float mip_roughness = perceptualRoughness * (1.7 - 0.7 * perceptualRoughness);
                float3 refDirWS = reflect(-viewDirWS, normalWS);
				half mip = mip_roughness * UNITY_SPECCUBE_LOD_STEPS;
				half4 encodedIrradiance = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, refDirWS, mip);
				float3 iblSpecular = albedo * DecodeHDREnvironment(encodedIrradiance, unity_SpecCube0_HDR); //iblSpecular是间接镜面反射的颜色。它表示物体表面反射的环境光中的镜面反射部分。
				float surfaceReduction = 1.0 / (roughness2 + 1.0);//表面减少因子 (surfaceReduction): 这个因子用于减少高粗糙度表面的镜面反射强度，使其更加自然。
				float grazingTerm = saturate(_Smoothness + 1 - Kd);//当视线接近物体表面时（即视线与表面法线之间的角度接近90度），镜面反射的强度会增加。
				float fresnelTerm = pow(1.0 - nDotV, 4);//菲涅耳效应描述了当光线从一种介质进入另一种介质时，反射强度的变化。
				half3 iblBrdf = surfaceReduction * lerp(Ks, grazingTerm, fresnelTerm);//iblBrdf 是间接镜面反射的 BRDF（双向反射分布函数）值。它用于调整间接镜面反射的颜色和强度，考虑了表面粗糙度、边缘效应和菲涅耳效应等因素。
				float3 iblSpecularResult = iblSpecular * iblBrdf;//间接光镜面反射，需要根据AO的高光做限制
				return iblSpecularResult;
			}

			float3 RIMLight(float nDotV, float3 positionWS)
			{
				float distanceFactor = 1.0 / (length(positionWS - _WorldSpaceCameraPos.xyz) + 1); //距离越近越大，为0时，为10， 为5时，为0.2
				float rimIntensity = step(0.0, nDotV) - step(0.0002/distanceFactor, nDotV); //当θ非常靠近90°时，才会为1
				rimIntensity = pow(rimIntensity, _RimPower) * _RimIntensity;
				float3 rimColor = _RimColor * rimIntensity;
				return rimColor;
			}

			half4 frag(v2f i) : SV_Target
			{
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
				float3 albedo = _Tint * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
				float Kd = kDieletricSpec.a - _Metallic * kDieletricSpec.a;
				float perceptualRoughness = 1 - _Smoothness;
				float roughness = max(perceptualRoughness * perceptualRoughness, HALF_MIN_SQRT);
				float roughness2 = max(roughness*roughness, HALF_MIN);
				float3 ao = SAMPLE_TEXTURE2D(_AO, sampler_AO, i.uv);//D法线分布项, F菲涅尔项, G几何遮蔽项
				float Ks = lerp(kDieletricSpec.rgb, albedo, _Metallic);
				float3 NPRDirectDiff = NPRDirectDiffuse(normalWS, lightDirWS, albedo);
				float3 PBRDirectDiff = PBRDirectDiffuse(mainLight.color, normalWS, lightDirWS, albedo);
				float3 NPRDirectSpec = NPRDirectSpecular(mainLight.color, nDotL, nDotH, lDotH2, roughness, roughness2, albedo, ao);
				float3 PBRDirectSpec = PBRDirectSpecular(mainLight.color, nDotL, nDotH, lDotH2, roughness, roughness2, albedo);
				float3 diffColor = NPRDirectDiff;
				float3 specColor = NPRDirectSpec;
				float3 DirectLightResult = diffColor + specColor;
				float3 iblDiffuseResult = PBRIndirectDiffuse(normalWS, albedo);//间接光漫反射和间接光镜面反射
				float3 iblSpecularResult = PBRIndirectSpecular(roughness2, perceptualRoughness, viewDirWS, normalWS, albedo);
				float3 IndirectResult = iblDiffuseResult + iblSpecularResult;
                float3 RIMResult = RIMLight(nDotV, i.positionWS); //自发光来添加的，比如说眼睛的高光，Matcap反射，边缘光之类的。
				float4 result = float4(DirectLightResult + IndirectResult + RIMResult, 1);

				return result;
			}
			ENDHLSL
		}

		Pass
        {
			Cull Front
            Name "Outline"

            Tags
            {
            "LightMode" = "SRPDefaultUnlit"
            }

            HLSLPROGRAM
		    #pragma target 3.0
		    #pragma vertex vert
		    #pragma fragment frag

		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

		    struct Attributes
		    {
    		    float4 positionOS      : POSITION;
   		        float2 uv0             : TEXCOORD0;
   		        float3 tangent         : TANGENT;
   		        float3 normal          : NORMAL;
   		        float4 vertColor       : COLOR;
		    };

		    struct Varyings
		    {
    		    float2 uv0        : TEXCOORD0;
   		        float4 positionCS : SV_POSITION;
		    };

			CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _OutlineWidth;
				float _OffsetX;
                float _OffsetY;
                float _ZOffset;
            CBUFFER_END
            
            Varyings vert(Attributes input)  //NDC空间的距离外扩
	        {
                Varyings output = (Varyings)0;
                half fovy = 1 / unity_CameraProjection[1].y; //获取FOV
                float3 positionVS = mul(UNITY_MATRIX_MV, input.positionOS).xyz;//获取与相机的距离
                float viewDepth = abs(positionVS.z);
                half factor = log2(fovy * viewDepth * 0.03 + 1 + _OffsetX) - log2(1 + _OffsetX) + _OffsetX; //粗细与Fov和相机距离的关系
                half3 biasDir = input.tangent;//首先在tangent中得到外扩的方向，并转换到ViewSpace下
                half3 biasViewDir = mul((float3x3)UNITY_MATRIX_IT_MV, biasDir);//然后只取这个方向平行于投影平面的部分（把它拍扁）
                biasViewDir = normalize(half3(biasViewDir.xy, 0.001));
                float3 outlineOffsetXY = biasViewDir.xyz * factor * _OutlineWidth * input.vertColor.r;//然后外扩，记得乘上上一步得到的粗细控制参数。把外扩的量加到顶点的position上，得到ViewSpace下顶点的位置就完成了。
                float3 outlinePos = float3(positionVS.xy + outlineOffsetXY.xy, positionVS.z);
                float3 clipPos = mul(UNITY_MATRIX_P, float4(outlinePos, 1)).xyw; //去除杂乱的线条的方法一般被大家称为ZOffset，顾名思义就是在ViewSpace下沿Z方向远离相机的方向推一段距离，使front能盖住杂乱的背面外扩的杂乱部分，基本只保留外描边。
                float viewOffsetZ = input.vertColor.g * _ZOffset + clipPos.z;
                float clipZ = clipPos.z * (-UNITY_MATRIX_P[2].z * viewOffsetZ + UNITY_MATRIX_P[2].w);
                output.positionCS.xyw = clipPos;
                output.positionCS.z = clipZ / viewOffsetZ;
                output.uv0 = input.uv0;
                return output;
            }

            half4 frag(Varyings input): SV_Target
	        {
                return _Color;
            }

            ENDHLSL
        }
	}

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}