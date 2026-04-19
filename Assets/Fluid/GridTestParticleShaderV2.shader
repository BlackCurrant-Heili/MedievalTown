Shader "Custom/SPH_InstancingDebug"
{
    Properties
    {
        _ParticleSize("Particle Size", Float) = 0.
        
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            name "RenderPass"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off  // 深度已由Depth Pass写入
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
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
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; float4 color : COLOR; float2 uv : TEXCOORD1;};

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<ParticleData> _particlesBuffer;
            #endif

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
            
            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;
                
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    float4x4 mat = _meshProperties[instanceID].mat;
                    float4 worldPos = float4(_particlesBuffer[instanceID].position.xyz, 1.0f) + mul(mat, input.positionOS);
                    output.positionCS = TransformWorldToHClip(worldPos);
                    output.screenPos = ComputeScreenPos(output.positionCS);
                    output.color = _meshProperties[instanceID].color;
                    output.uv = input.uv;
                #else
                    // ❌ Instancing 未启用：红色
                    output.positionCS = TransformWorldToHClip(input.positionOS.xyz * _ParticleSize);
                    output.screenPos = ComputeScreenPos(output.positionCS);
                    output.uv = input.uv; 
                    output.color = float4(1,0,0,1);
                #endif
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                float dist = length(input.uv - 0.5);
                clip(0.5 - dist);
                return input.color;
            }
            ENDHLSL
        }
    }
}