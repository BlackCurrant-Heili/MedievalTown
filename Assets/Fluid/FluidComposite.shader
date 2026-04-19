Shader "Fluid/Composite"
{
    Properties
    {
        _FluidColor("Fluid Color", Color) = (0.2, 0.55, 0.6, 0.8)
        _AbsorptionCoeff("Absorption Coefficient", Float) = 0.1
        _ThicknessScale("Thickness Scale", Range(0.01, 2.0)) = 1  
        _TransparencyScale("Transparency Scale", Range(0.1, 5.0)) = 0.15 
        _RefractStrength("Refraction Strength", Range(0, 0.1)) = 0.02
        _FresnelPower("Fresnel Power", Range(0.5, 5.0)) = 2.0
        _FresnelScale("Fresnel Scale", Range(0, 1)) = 0.8
        _SpecularPower("Specular Power", Range(1, 128)) = 32         // ⚠️ 建议将默认值从 64 降低到 32，高光更柔和
        _SpecularIntensity("Specular Intensity", Range(0, 2)) = 0.1
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.05
        _MinTransparency("Min Transparency", Range(0, 1)) = 0.01[Header(Anti Grain Magic Settings)]
        _BlurSpread("Thickness/Normal Blur Spread", Range(0, 5)) = 2.0     // ✅ 新增：微模糊半径
        _NormalFlatten("Deep Water Flattening", Range(0, 1)) = 0.9         // ✅ 新增：深水区法线压平强度
        _FlattenThreshold("Flatten Thickness Threshold", Range(0, 2)) = 0.5// ✅ 新增：多厚的水域开始压平
    }
    
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "FluidComposite"
            
            Blend One OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma prefer_hlslcc gles
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewDirVS : TEXCOORD1;
            };

            TEXTURE2D(_FluidSmoothDepth);
            SAMPLER(sampler_FluidSmoothDepth);
            TEXTURE2D(_FluidNormal);
            SAMPLER(sampler_FluidNormal);
            TEXTURE2D(_FluidThickness);
            SAMPLER(sampler_FluidThickness);
            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _FluidColor;
                float _AbsorptionCoeff;
                float _ThicknessScale;        
                float _TransparencyScale;     
                float _RefractStrength;
                float _FresnelPower;
                float _FresnelScale;
                float _SpecularPower;
                float _SpecularIntensity;
                float _ReflectionStrength;
                float _MinTransparency;       
                
                // ✅ 新增三个魔法参数
                float _BlurSpread;
                float _NormalFlatten;
                float _FlattenThreshold;
                
                float4x4 _InvProjectionMatrix;
                float4x4 _ViewToWorldMatrix;
            CBUFFER_END

            float3 ReconstructViewPos(float2 uv, float deviceDepth)
            {
                float4 ndc = float4(uv * 2.0 - 1.0, deviceDepth, 1.0);
                float4 viewPos = mul(_InvProjectionMatrix, ndc);
                return viewPos.xyz / viewPos.w;
            }
            
            float Fresnel(float cosTheta, float power, float scale)
            {
                return scale * pow(saturate(1.0 - cosTheta), power);
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy, 0, 1);
                output.uv = input.uv;
                
                float3 viewPos = ReconstructViewPos(input.uv, 1.0);
                output.viewDirVS = normalize(-viewPos);
                
                return output;
            }

            half3 CalculateFakeReflection(float3 viewDirVS, float3 normalVS)
            {
                float3 reflectDirVS = reflect(-viewDirVS, normalVS);
                float3 reflectDirWS = TransformViewToWorldDir(reflectDirVS);
                reflectDirWS = normalize(reflectDirWS);
                half4 skyColor = SAMPLE_TEXTURECUBE(unity_SpecCube0, samplerunity_SpecCube0, reflectDirWS);
                half3 reflectionColor = DecodeHDREnvironment(skyColor, unity_SpecCube0_HDR);
                
                return reflectionColor;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // ========================================
                // Block 1: UV坐标准备 & 早期剔除
                // ========================================
                float2 uv = input.uv;
                uv.y = 1.0 - uv.y;
                
                float fluidDepth = SAMPLE_TEXTURE2D(_FluidSmoothDepth, sampler_FluidSmoothDepth, uv).r;
                float cameraDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                float cameraLinearDepth = LinearEyeDepth(cameraDepth, _ZBufferParams);

                if(cameraLinearDepth < fluidDepth)
                {
                    return 0;
                }

                // ========================================
                // ✅ Block 2: 表面属性采样（加入抗颗粒微模糊与法线压平）
                // ========================================
                float2 texel = 1.0 / _ScreenParams.xy;
                float2 offsets[4] = { float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1) };
                
                // 中心采样
                float rawThickness = SAMPLE_TEXTURE2D(_FluidThickness, sampler_FluidThickness, uv).r;
                float3 normalVS = SAMPLE_TEXTURE2D(_FluidNormal, sampler_FluidNormal, uv).rgb * 2.0 - 1.0;
                
                // 5-Tap 微模糊（极其轻量，有效抹平厚度和法线噪点）
                float blurRadius = _BlurSpread * texel;
                [unroll]
                for (int i = 0; i < 4; i++) {
                    float2 offsetUV = uv + offsets[i] * blurRadius;
                    rawThickness += SAMPLE_TEXTURE2D(_FluidThickness, sampler_FluidThickness, offsetUV).r;
                    normalVS += SAMPLE_TEXTURE2D(_FluidNormal, sampler_FluidNormal, offsetUV).rgb * 2.0 - 1.0;
                }
                rawThickness *= 0.2; // 算出平均厚度
                normalVS = normalize(normalVS); // 平均后的法线重新归一化

                // 法线压平魔法（水域越厚，表面越像平整的玻璃，消灭沸腾感）
                float flattenFactor = saturate(rawThickness / max(_FlattenThreshold, 0.001));
                normalVS = normalize(lerp(normalVS, float3(0, 0, 1), flattenFactor * _NormalFlatten));

                // 遮罩计算（切除孤立噪波水滴）
                float fluidMask = smoothstep(0.05, 0.2, rawThickness); 

                float thickness = rawThickness * _ThicknessScale;
                float3 viewDirVS = input.viewDirVS;
                
                // ========================================
                // Block 3: 反射计算（Reflection）
                // ========================================
                half3 reflectionColor = CalculateFakeReflection(viewDirVS, normalVS);

                // ========================================
                // Block 4: 折射计算 + 体积吸收（Refraction + Absorption）
                // ========================================
                float2 refractOffset = normalVS.xy * _RefractStrength; 
                float2 refractUV = uv - refractOffset; 
                refractUV.y = 1.0 - refractUV.y;
                refractUV = saturate(refractUV); 

                half3 backgroundColor = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refractUV).rgb;
                float transmittance = exp(-_AbsorptionCoeff * thickness);
                half3 refractionColor = lerp(_FluidColor.rgb, backgroundColor, transmittance);
                
                // ========================================
                // Block 5: 菲涅尔混合（Fresnel Blending）
                // ========================================
                float cosTheta = max(dot(normalVS, viewDirVS), 0.0);
                float fresnel = Fresnel(cosTheta, _FresnelPower, _FresnelScale);

                half3 finalColor = lerp(refractionColor, reflectionColor, fresnel * _ReflectionStrength);

                // ========================================
                // Block 6: 镜面高光（Specular / Blinn-Phong）
                // ========================================
                Light mainLight = GetMainLight();
                float3 lightColor = mainLight.color.rgb; 
                float3 lightDirWS = normalize(mainLight.direction); 
                float3 halfDir = normalize(viewDirVS + TransformWorldToViewDir(lightDirWS));
                float specular = pow(max(dot(normalVS, halfDir), 0.0), _SpecularPower) * _SpecularIntensity;
                finalColor += lightColor * specular;

                // ========================================
                // Block 7: 透明度计算 & 预乘Alpha输出
                // ========================================
                float volumeAlpha = saturate(thickness * _TransparencyScale);
                float baseAlpha = _FluidColor.a;
                float edgeAlpha = fresnel * 0.5;

                float finalAlpha = saturate(volumeAlpha + baseAlpha + edgeAlpha);
                finalAlpha = max(finalAlpha, _MinTransparency);
                finalAlpha *= fluidMask; 
                finalColor *= finalAlpha;

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
}