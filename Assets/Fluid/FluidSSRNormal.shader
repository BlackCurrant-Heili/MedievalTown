Shader "Fluid/NormalFromDepth"
{
    Properties
    {
        [NoScaleOffset] _FluidSmoothDepth("Smooth Depth", 2D) = "white" {}
        _SampleScale("Sample Scale", Range(1, 5)) = 2.0
        _FluidMaxDepth("Fluid Max Depth", Float) = 80.0
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "NormalReconstruction"
            ZTest Always ZWrite Off Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes 
            { 
                float4 positionOS : POSITION; 
                float2 uv : TEXCOORD0; 
            };
            
            struct Varyings 
            { 
                float2 uv : TEXCOORD0; 
                float4 positionCS : SV_POSITION; 
            };

            TEXTURE2D(_FluidSmoothDepth);
            SAMPLER(sampler_FluidSmoothDepth);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _FluidSmoothDepth_TexelSize;
                float _SampleScale;
                float _FluidMaxDepth;
                float4x4 _InvProjectionMatrix;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy, 0, 1);
                output.uv = input.uv;
                return output;
            }

        // ✅ 修复：正确采样 R 通道
            float SampleDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_FluidSmoothDepth, sampler_FluidSmoothDepth, uv).r;
            }

            // ✅ 修复：利用正确的射线法重建 View Space 坐标
            float3 UVToEye(float2 uv, float linearDepth)
            {
                float2 ndc = uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    ndc.y = -ndc.y;
                #endif
                
                // 1. 构建一个位于裁剪空间远平面的点 (NDC Z = 0.5或1.0都行，只是为了获取方向)
                float4 clipPos = float4(ndc, 0.5, 1.0);
                float4 viewPos = mul(_InvProjectionMatrix, clipPos);
                
                // 2. 获取真实的视线射线方向
                float3 ray = viewPos.xyz / viewPos.w;
                
                // 3. 将射线延伸到我们指定的 linearDepth（View Space 的 Z 是负的，所以用 abs 保证符号正确）
                return ray * (linearDepth / abs(ray.z));
            }
            
            float4 frag(Varyings input) : SV_Target
            {
                // ... 保留前面的 UV 定义 ...
                float2 uv = float2(input.uv.x, 1.0 - input.uv.y);
                float2 texel = _FluidSmoothDepth_TexelSize.xy * _SampleScale;
                
                float dC = SampleDepth(uv);
                if (dC <= 0.001) return float4(0.5, 0.5, 1.0, 1.0);
                
                float dL = SampleDepth(uv - float2(texel.x, 0));
                float dR = SampleDepth(uv + float2(texel.x, 0));
                float dT = SampleDepth(uv + float2(0, texel.y));
                float dB = SampleDepth(uv - float2(0, texel.y));
                
                dL = (dL <= 0.001) ? dC : dL;
                dR = (dR <= 0.001) ? dC : dR;
                dT = (dT <= 0.001) ? dC : dT;
                dB = (dB <= 0.001) ? dC : dB;
                
                // ✅ 修复：因为 R 通道直接就是真实米数，直接赋值，删除原来的 * _FluidMaxDepth !
                float viewZC = dC;
                float viewZL = dL;
                float viewZR = dR;
                float viewZT = dT;
                float viewZB = dB;
                
                // ... 下面的差分计算(dx, dy 和 cross) 保持你原来的代码不变 ...
                // 重建View Space位置
                float3 eyeC = UVToEye(uv, viewZC);
                float3 eyeL = UVToEye(uv - float2(texel.x, 0), viewZL);
                float3 eyeR = UVToEye(uv + float2(texel.x, 0), viewZR);
                float3 eyeT = UVToEye(uv + float2(0, texel.y), viewZT);
                float3 eyeB = UVToEye(uv - float2(0, texel.y), viewZB);
                
                // 前向差分（右-中，上-中）和后向差分（中-左，中-下）
                float3 ddxLeft  = eyeC - eyeL;  // 后向差分
                float3 ddxRight = eyeR - eyeC;  // 前向差分
                float3 ddyTop   = eyeT - eyeC;  // 前向差分（注意UV坐标系）
                float3 ddyBottom= eyeC - eyeB;  // 后向差分
                
                // ✅ 智能选择：取Z分量变化较小的差分（避免跨越不连续面）
                // 原理：如果一侧是背景（深度突变），该侧差分的Z分量绝对值会很大
                float3 dx = ddxLeft;
                float3 dy = ddyTop;
                
                if (abs(ddxRight.z) < abs(ddxLeft.z))
                    dx = ddxRight;
                    
                if (abs(ddyBottom.z) < abs(ddyTop.z))
                    dy = ddyBottom;
                
                // 叉乘得到法线
                float3 normalVS = normalize(cross(dx, dy));

                normalVS.y = -normalVS.y;
                
                // 确保朝向相机（View Space +Z朝向相机）
                if (normalVS.z < 0.0)
                    normalVS = -normalVS;
                
                return float4(normalVS * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }
    }
}