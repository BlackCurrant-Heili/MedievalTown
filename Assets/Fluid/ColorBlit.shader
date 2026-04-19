Shader "ColorBlit"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off Cull Off
        
        Pass
        {
            Name "ColorBlitPass"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ✅ 标准顶点输入（匹配你的 Mesh 数据）
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            // ✅ 标准 Vertex Shader（不再用 Blit.hlsl 的 Vert）
            Varyings vert(Attributes input)
            {
                Varyings output;
                // 直接输出 Clip Space 位置（你的 Mesh 已经是 -1 到 1 范围）
                output.positionCS = float4(input.positionOS.xy, 0, 1);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 测试 UV：应该看到左下黑(0,0)，右上黄(1,1,0)
                // return half4(input.uv, 0, 1);
                
                // 正式采样
                return SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, input.uv);
            }
            ENDHLSL
        }   
    }
}