Shader "Custom/GrassProcedural"
{
    Properties
    {
        [Header(Color Settings)]
        _BottomColor("Bottom Color (Root)", Color) = (0.05, 0.2, 0.05, 1) // 根部深绿
        _TopColor("Top Color (Tip)", Color) = (0.5, 0.8, 0.2, 1)      // 尖端浅绿
        
        [Header(PBR Settings)]
        _Roughness("Roughness", Range(0, 1)) = 0.8
        _Width("Grass Width", Float) = 0.05

        _WShapeIntensity("W Shape Intensity", Range(0, 1)) = 0.5

        [Header(Macro Color Variation)]
        _DeadColor("Dead Grass Color", Color) = (0.7, 0.5, 0.2, 1) // 枯黄/暖橙色
        _NoiseScale("Noise Scale (Patch Size)", Float) = 0.05      // 斑块大小
        _NoiseIntensity("Noise Intensity", Range(0, 1)) = 0.6      // 枯草混合强度
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off // 保持双面渲染
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define PI 3.14159265359
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Common.hlsl"

            StructuredBuffer<LineData> _Lines;
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BottomColor;
                float4 _TopColor;
                float _Roughness;
                float _Width; 
                float _WShapeIntensity;
                float4 _DeadColor;
                float _NoiseScale;
                float _NoiseIntensity;
            CBUFFER_END
            
            struct VertexInput
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            // 彻底精简传输数据，省带宽！
            struct VertexOutput
            {
                float4 pos : SV_POSITION;
                float3 positionWS : TEXCOORD0; 
                float3 normalWS : TEXCOORD1;     // 正面法线
                float weight : TEXCOORD2;
                float2 uv : TEXCOORD3;           // 传递UV给片元算W型
                float3 rightWS : TEXCOORD4;      // 传递向右的向量给片元
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 计算宽度（不再是死板的线性衰减，而是叶片状）
            float CalculateGrassWidth(float w, float baseWidth)
            {
                // 在草的 20% 高度处最宽，然后慢慢收尖
                if (w < 0.2)
                    return baseWidth * lerp(0.6, 1.0, w / 0.2); // 根部是 0.6 倍宽，向上生长的 20% 处达到 1.0 最宽
                else
                    return baseWidth * lerp(1.0, 0.0, (w - 0.2) / 0.8); // 剩下的 80% 慢慢收缩到 0
            }

            float random2D(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453123);
            }

            // --- 平滑的 2D Value Noise (生成自然的云影/斑块) ---
            float valueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                // 平滑插值曲线 (Smoothstep 变体)
                float2 u = f * f * (3.0 - 2.0 * f);

                // 采样四个角的随机值
                float a = random2D(i);
                float b = random2D(i + float2(1.0, 0.0));
                float c = random2D(i + float2(0.0, 1.0));
                float d = random2D(i + float2(1.0, 1.0));

                // 双线性插值混合
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            VertexOutput vert(VertexInput v)
            {
                VertexOutput o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                uint grassIndex = v.instanceID;
                uint vertexInGrass = v.vertexID;  
                uint segIndex = vertexInGrass / 6;
                uint vertInTri = vertexInGrass % 6;

                LineData lineData = _Lines[grassIndex];
                uint validSegCount = lineData.realSeg > 0 ? lineData.realSeg - 1 : 0;

                if (segIndex >= validSegCount || lineData.isPhysic == 0)
                {
                    o.pos = float4(0, 0, 0, 0);
                    return o;
                }

                VertexDataLine v0 = lineData.vertices[segIndex];
                VertexDataLine v1 = lineData.vertices[segIndex + 1];
                
                // 天然的尖刺塑形！不用透明贴图了！
                float width0 = CalculateGrassWidth(v0.weight, _Width);
                float width1 = CalculateGrassWidth(v1.weight, _Width);
                
                float3 currLeft = v0.positionWS - v0.normalWS * width0;
                float3 currRight = v0.positionWS + v0.normalWS * width0;
                float3 nextLeft = v1.positionWS - v1.normalWS * width1;
                float3 nextRight = v1.positionWS + v1.normalWS * width1;

                float3 finalPos;
                float finalWeight;
                float2 finalUV; // 记录左右的 U 坐标

                if (vertInTri == 0) { finalPos = currRight; finalWeight = v0.weight; finalUV = float2(1, v0.weight); }
                else if (vertInTri == 1) { finalPos = nextRight; finalWeight = v1.weight; finalUV = float2(1, v1.weight); }
                else if (vertInTri == 2) { finalPos = currLeft; finalWeight = v0.weight; finalUV = float2(0, v0.weight); }
                else if (vertInTri == 3) { finalPos = nextRight; finalWeight = v1.weight; finalUV = float2(1, v1.weight); }
                else if (vertInTri == 4) { finalPos = nextLeft; finalWeight = v1.weight; finalUV = float2(0, v1.weight); }
                else { finalPos = currLeft; finalWeight = v0.weight; finalUV = float2(0, v0.weight); }

                // 算出真正面向太阳的“草叶正面法线”
                float3 faceNormal = normalize(cross(v0.normalWS, float3(0, 1, 0))); 

                o.pos = TransformWorldToHClip(finalPos);
                o.positionWS = finalPos;
                o.normalWS = faceNormal;     // 传递正面法线
                o.rightWS = v0.normalWS;     // 传递向右的向量
                o.uv = finalUV;              // 传递 UV
                o.weight = finalWeight;

                return o;
            }

            float4 frag(VertexOutput i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                
                // 1. 核心魔法：颜色渐变 (基于骨架高度权重)
                float3 baseColor = lerp(_BottomColor.rgb, _TopColor.rgb, i.weight);

                // ==========================================
                // 👑 宏观色彩扰动 (Macro Color Variation)
                // ==========================================
                // 用草的【世界坐标的 XZ 轴】去采样噪波！
                // 这样生成的斑块是覆盖在整片大地上连贯的，而不是每根草随机的！
                float noiseVal = valueNoise(i.positionWS.xz * _NoiseScale);

                // 将算出的噪波值乘上强度，然后把原本的绿色和枯黄色进行混合
                float blendFactor = saturate(noiseVal * _NoiseIntensity);
                baseColor = lerp(baseColor, _DeadColor.rgb, blendFactor);

                float wShape = sin(i.uv.x * PI * 2.0); 
                float3 bentNormalWS = normalize(i.normalWS + i.rightWS * wShape * _WShapeIntensity);
                
                // 后续所有的光照，全部使用这根带有 W 型褶皱的法线！
                float3 normalWS = bentNormalWS;
                float roughness = _Roughness;
                
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 lightColor = mainLight.color;
                float3 viewDir = normalize(GetWorldSpaceNormalizeViewDir(i.positionWS));
                
                // --- TA 级定制光照 (双面透射 PBR) ---
                
                float trueNdotL = dot(normalWS, lightDir);
                
                // 正面受光
                float NdotL = saturate(trueNdotL);
                // 【绝杀细节】：背面透射光 (当光在草背后时，让草透出漂亮的颜色)
                float backlight = saturate(-trueNdotL) * 0.4; 
                
                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normalWS, halfDir));
                
                // 使用 abs 防止草的背面出现死黑
                float absNdotV = saturate(abs(dot(normalWS, viewDir))); 
                float absNdotL = saturate(abs(trueNdotL));
                
                // GGX 分布
                float roughness2 = max(roughness * roughness, 0.001);
                float denom = NdotH * NdotH * (roughness2 - 1.0) + 1.0;
                float D = roughness2 / (PI * denom * denom);
                
                // 几何遮蔽
                float k = roughness2 * 0.5;
                float G = (absNdotV / (absNdotV * (1.0 - k) + k)) * (absNdotL / (absNdotL * (1.0 - k) + k));
                
                // Fresnel
                float3 F = 0.04 + (1.0 - 0.04) * pow(1.0 - saturate(dot(halfDir, viewDir)), 5.0);
                
                // 漫反射 (原色 * (正光 + 透光))
                float3 diffuse = baseColor * (1.0 / PI) * (NdotL + backlight) * lightColor;
                
                // 镜面反射
                float3 specular = (D * G * F) / (4.0 * absNdotV * absNdotL + 0.0001) * absNdotL * lightColor;
                
                // 环境光
                float3 ambient = baseColor * SampleSH(normalWS);
                
                // 最终合成
                float3 finalColor = ambient + diffuse + specular;
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}