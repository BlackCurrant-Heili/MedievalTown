Shader "Custom/SPH_InstancingDebug"
{
    Properties
    {
        _ParticleSize("Particle Size", Float) = 0.
        
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        // ==================== Pass 0: DepthOnly（仅写入深度） ====================
        Pass
        {
            name "DepthPass"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ParticleSize;
            CBUFFER_END

            struct ParticleData { float3 position; float4 color; };
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 viewPos : TEXCOORD1; float radius : TEXCOORD2; }; 

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<ParticleData> _particlesBuffer;
            #endif

            struct MeshProperties {
                float4x4 mat;
                float4 color;
            };

            StructuredBuffer<MeshProperties> _meshProperties;

            void setup() {}
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                float3 particleWorldPos;
                float particleRadius;
                
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    particleWorldPos = _particlesBuffer[input.instanceID].position.xyz;
                    particleRadius = _ParticleSize * _particlesBuffer[input.instanceID].color.w * 0.5;
                #else
                    particleWorldPos = float3(0,0,0);
                    particleRadius = _ParticleSize * 0.5;
                #endif
                
                // ==================== Billboard 核心逻辑 ====================
                // 1. 将粒子中心转换到 View Space（相机空间）
                float3 viewCenter = mul(UNITY_MATRIX_V, float4(particleWorldPos, 1.0)).xyz;
                
                // 2. 在 View Space 的 XY 平面展开 Quad，使其永远垂直于相机视线
                // 假设传入的 Quad positionOS.xy 范围是 [-0.5, 0.5]，乘以直径 (radius * 2) 缩放
                float2 quadOffset = input.positionOS.xy * (particleRadius * 2.0);
                float3 viewPos = viewCenter + float3(quadOffset, 0.0);
                
                // 3. 转换到裁剪空间
                output.positionCS = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                
                // 4. 传递给片段着色器
                output.viewPos = viewPos; // View Space 的平面坐标
                output.radius = particleRadius;
                output.uv = input.uv;
                
                return output;
            }

            struct FragmentOutput {
                half4 color : SV_Target;
                float depth : SV_Depth;  // ✅ 强制修改硬件 Z-Buffer 深度
            };
            
            FragmentOutput frag(Varyings input) : SV_Target
            {
                // 将 UV [0, 1] 映射到 [-1, 1]
                float2 ndc = input.uv * 2.0 - 1.0;
                float sqrDist = dot(ndc, ndc);

                // 圆形裁剪：丢弃正方形四个角
                if (sqrDist > 1.0)
                    discard;

                // ==================== 伪球形深度计算 ====================
                // 球面方程: z^2 + x^2 + y^2 = r^2 -> z = sqrt(1 - (x^2+y^2)) * r
                float zOffset = sqrt(1.0 - sqrDist) * input.radius; 
                
                // Unity 的 View Space 是右手系，相机看向 -Z 轴。
                // 深度越小（越靠近相机），Z值越大（例如 -5 比 -10 靠近）。
                // 所以我们加上 zOffset，让原本平面的 Quad 向相机方向凸起成半个球。
                float3 sphereViewPos = input.viewPos;
                sphereViewPos.z += zOffset;  
                
                FragmentOutput output;
                
                // 记录真实的 View Space 线性深度 (正数) 供法线重建使用
                float linearDepth = -sphereViewPos.z;
                output.color = half4(linearDepth, 0.0, 0.0, 1.0); 

                // 重新计算并写入正确的硬件 Z 深度
                float4 clipPos = mul(UNITY_MATRIX_P, float4(sphereViewPos, 1.0));
                #if UNITY_REVERSED_Z
                    output.depth = clipPos.z / clipPos.w;
                #else
                    output.depth = (clipPos.z / clipPos.w) * 0.5 + 0.5;
                #endif

                return output;
            }
            ENDHLSL
        } 
        
        // ==================== Pass 1: Thickness (体积厚度图) ====================
        Pass
        {
            Name "ThicknessPass"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            ZTest LEqual        // ✅ 关键：设为 LEqual！被墙壁挡住的粒子不应贡献厚度
            Cull Off
            Blend One One       // ✅ Additive 混合，厚度叠加
            ColorMask R         // 只写入R通道

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ParticleSize;
            CBUFFER_END

            struct ParticleData { float3 position; float4 color; };
            struct Attributes { 
                float4 positionOS : POSITION; 
                float2 uv : TEXCOORD0; 
                uint instanceID : SV_InstanceID; 
            };
            
            struct Varyings { 
                float4 positionCS : SV_POSITION; 
                float2 uv : TEXCOORD0; 
                float radius : TEXCOORD1; 
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<ParticleData> _particlesBuffer;
            #endif

            void setup() {}
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                float3 particleWorldPos;
                float particleRadius;
                
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    particleWorldPos = _particlesBuffer[input.instanceID].position.xyz;
                    particleRadius = _ParticleSize * _particlesBuffer[input.instanceID].color.w * 0.5;
                #else
                    particleWorldPos = float3(0,0,0);
                    particleRadius = _ParticleSize * 0.5;
                #endif
                
                // ✅ 这里和 Depth Pass 的顶点逻辑完全一致，依然是严格正对相机的 Billboard
                float3 viewCenter = mul(UNITY_MATRIX_V, float4(particleWorldPos, 1.0)).xyz;
                float2 quadOffset = input.positionOS.xy * (particleRadius * 2.0);
                float3 viewPos = viewCenter + float3(quadOffset, 0.0);
                
                output.positionCS = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                output.uv = input.uv; 
                output.radius = particleRadius;
                
                return output;
            }

            half4 frag(Varyings input) : SV_TARGET
            {
                float2 ndc = input.uv * 2.0 - 1.0;
                float r2 = dot(ndc, ndc);
                
                // ✅ 软边过渡：在 0.8~1.0 范围渐变淡出，避免锯齿硬边
                float softMask = 1.0 - smoothstep(0.81, 1.0, r2);  // 提前一点开始淡出
                
                // ✅ 真实弦长：2 * sqrt(R² - d²)，物理上就是视线穿过球体的距离
                // 使用 sqrt 替代 x³，过渡更自然（x³ 在中心太平，边缘太陡）
                float chord = 2.0 * sqrt(max(0.0, 1.0 - r2));
                
                // 结合软边掩码
                float thickness = input.radius * chord * softMask;
                
                return half4(thickness, 0, 0, 0);
            }

            // half4 frag(Varyings input) : SV_TARGET
            // {
            //     float2 ndc = input.uv * 2.0 - 1.0;
            //     float sqrDist = dot(ndc, ndc);
                
            //     if (sqrDist > 1.0) 
            //         discard;

            //     // ==================== 视线方向弦长 (Thickness) ====================
            //     // 一条射线穿过球体，穿梭的距离就是弦长。
            //     // 弦长公式 = 2 * 球体半径 * sqrt(1 - (距离圆心比例)^2)
            //     float falloff = 1.0 - sqrDist;
            //     float thickness = input.radius * 2.0 * (falloff * falloff * falloff);
                
            //     // 乘上粒子的密度权重 (如果你有的话)，否则直接返回 thickness
            //     // 这将会通过 Blend One One 累加到 Target 的 R 通道上
            //     return half4(thickness, 0, 0, 0);
            // }
            ENDHLSL
        }

        // ==================== Pass 2: Shadow Map ====================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            ZTest Off
            Cull Off
            Blend One Zero       
            ColorMask RGBA         

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma multi_compile _ DEBUG_SHADOW  // 添加调试开关
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ParticleSize;
            CBUFFER_END

            struct ParticleData { float3 position; float4 color; };
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 positionCST : TEXCOORD1;};

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<ParticleData> _particlesBuffer;
            #endif

            float4x4 _LightViewProj;  // 光源 ViewProj 矩阵

            struct MeshProperties {
                float4x4 mat;
                float4 color;
            };

            StructuredBuffer<MeshProperties> _meshProperties;

            void setup()
            {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                ParticleData data = _particlesBuffer[unity_InstanceID];
                    // 手动构建ObjectToWorld矩阵
                float scale = _ParticleSize * data.color.w;
                unity_ObjectToWorld._11_21_31_41 = float4(scale, 0, 0, 0);      // X缩放
                unity_ObjectToWorld._12_22_32_42 = float4(0, scale, 0, 0);      // Y缩放
                unity_ObjectToWorld._13_23_33_43 = float4(0, 0, scale, 0);      // Z缩放
                unity_ObjectToWorld._14_24_34_44 = float4(data.position.xyz, 1);          // 位置
                
            #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                uint instanceID = input.instanceID;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    float4x4 mat = _meshProperties[instanceID].mat;
                    float4 worldPos = float4(_particlesBuffer[instanceID].position.xyz, 1.0f) + mul(mat, input.positionOS);
                    worldPos.w = 1.0f;
                    // 光源空间
                    output.positionCS = mul(_LightViewProj, worldPos);
                    output.positionCST = mul(_LightViewProj, worldPos);
                #else
                    output.positionCS = float4(0, 0, 0, 1);
                #endif
                output.uv = input.uv;
                return output;
            }
            // 输出Shadow
            half4 frag(Varyings input) : SV_TARGET
            {
                float depth = input.positionCST.z / input.positionCST.w;
                //float linearDepth01 = saturate(depth);
                
                return float4(depth, 0, 0, 1);
            }

            ENDHLSL
        }
    }
}