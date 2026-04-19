#ifndef _GRASS_COMMON_HLSL
#define _GRASS_COMMON_HLSL

#include "./Math.hlsl"
#include "Common.hlsl"

// #define _CURVATION_LO   (PI * 0.1)
// #define _CURVATION_HI   (PI * 0.8)
// #define _DIST_RANGE     0.125
// #define _BASE_P         0.0125      // base penetration
// #define _BASE_H         0.0250      // base height
// #define _HEIGHT         0.5000      // height
// #define _WIDTH          0.0300
// #define _OFFSET         -0.0125

#define LOD_MIN_DISTANCE 10.0
#define LOD_MAX_DISTANCE 100.0
#define LOD_LEVELS 4.0
#define LOD_MAX 4
#define LOD_MIN 0

struct GrassParams {
    float _HEIGHT; 
    float _WIDTH;
};

RWStructuredBuffer<GrassParams> _GrassParams;

float3 _WorldSpaceCameraPos;
float4x4 _ViewProj;

Texture2D<float> _HiZTex;
float2 _HizTex_TexelSize;

Texture2D<float4> _CurCollision;

int __lodLevel(float3 positionWS) {
    float dist = distance(positionWS, _WorldSpaceCameraPos);
    float f = 1.0 - clamp((dist - LOD_MIN_DISTANCE) / (LOD_MAX_DISTANCE - LOD_MIN_DISTANCE), 0.01, 1.00);
    f = (f*f)*(f*f);
    int lod = int(LOD_LEVELS * f);
    return lod;
}

//视锥剔除
bool FrustumCull(float3 positionWS, out float4 positionNDC) {
    positionWS.y += _GrassParams[0]._HEIGHT + 0.1;  // 用草尖的位置

    positionNDC = mul(_ViewProj, float4(positionWS, 1.0));
    positionNDC /= positionNDC.w;

    const float TOLLERANCE = 0.08;
    //判断该点是否在 [-1, 1] 的包围盒中
    bool isInsideFrustum = 
    positionNDC.x >= (-1.0 - TOLLERANCE) && positionNDC.x <= (1.0 + TOLLERANCE) &&
    positionNDC.y >= (-1.0 - TOLLERANCE) && positionNDC.y <= (1.0 + TOLLERANCE) &&
    positionNDC.z >= (-1.0) && positionNDC.z <= (1.0);
    return !isInsideFrustum;
}

//基于 depth buffer 剔除
bool HiZCull(float4 positionNDC) {
    float4 pos = positionNDC*0.5 + 0.5;

    //采样 HiZ 图
    float sampledDepth = _HiZTex[pos.xy * _HizTex_TexelSize].r;

    //根据不同的图形 API 定义 _DX 或 _GL 宏。
#ifdef SHADER_API_D3D11
#define _DX 1
#endif

#ifdef SHADER_API_D3D11_9X
#define _DX 1
#define _DX 1
#endif

#ifdef SHADER_API_GLCORE
#define _GL 1
#endif

#ifdef SHADER_API_GLES
#define _GL 1
#endif

#ifdef SHADER_API_GLES3
#define _GL 1
#endif

    //用于避免深度比较时的精度问题，防止误剔除。
    const float BIAS = 0.005;

    //根据不同的平台进行不同的深度比较
#ifdef _DX
    float z = pos.z;
    z = 1.0 - z;   // UNITY_REVERSED_Z
    return z < sampledDepth-BIAS;
#endif

#ifdef _GL
    float z = pos.z;
    return z > sampledDepth+BIAS;
#endif

    //默认情况下，丢弃所有像素（即不进行渲染）。
    return true;  // discard all
}

// void LinesGen(float3 positionWS, uint index) {  
//     int _NUM_BLADES = 1;
//     int _NUM_SEGS = 5;
//     int _REAL_NUM_BLADES = 0;

//     //for every blades
//     for(int i=0; i<_NUM_BLADES; i++) 
//     {
//         float height = _GrassParams[0]._HEIGHT + 0.5;
//         float _BODY_H = height / (float)(_NUM_SEGS + 1);
//         float3 rnd = rand3(positionWS + i) * 2 - 1;
//         float2 cs = float2(sin(rnd.z * PI), cos(rnd.z * PI));
//         float3 right    = float3(cs.x, 0, cs.y);
//         float3 normalWS = float3(-cs.y, 0, cs.x);
//         float3 v0 = positionWS + float3(rnd.x, 0, rnd.y);

//         TriangleData tri;
//         LineData lin;
//         VertexData vert;

//         float curvation = 0;
//         float w1 = (float)(0) / (float)(_NUM_SEGS + 2);
//         float w2 = (float)(1) / (float)(_NUM_SEGS + 2);
//         float3x3 rot1 = _AngleAxis3x3(w1 * curvation, right);
//         float3x3 rot2 = _AngleAxis3x3(w2 * curvation, right);
//         float3 n1 = mul(rot1, normalWS);
//         float3 n2 = mul(rot2, normalWS);

//         float3 v1 = v0;
//         float3 v2 = v0;

//         for(int j=0; j<_NUM_SEGS; j++) 
//         {
//             w1 = w2;
//             w2 = (float)(j + 2) / (float)(_NUM_SEGS + 2);

//             rot1 = rot2;
//             rot2 = _AngleAxis3x3(w2 * curvation, right);
//             n1 = n2;
//             n2 = mul(rot2, normalWS);

//             float3 offset = float3(0, _BODY_H, 0);

//             v2 = v1 + offset;
            
//             vert.normalWS = n2; vert.weight = w2; vert.positionWS = v1; lin.vertices[0] = vert;
//             vert.normalWS = n2; vert.weight = w2; vert.positionWS = v2; lin.vertices[1] = vert;
//             //_Lines[0] = lin;
//             _Lines.Append(lin);
//             v1 = v2;
//         }

//         float wH = 1.0;
//         float3x3 rot3 = AngleAxis3x3(wH*curvation, right);
  
//         float3 vH = v1 + mul(rot3, float3(0, _BODY_H, 0));
//         vert.normalWS = n2; vert.weight = w2; vert.positionWS = v1; lin.vertices[0] = vert;
//         vert.normalWS = n2; vert.weight = wH; vert.positionWS = vH; lin.vertices[1] = vert;
//         _Lines.Append(lin);
//         // _Lines[index * 6 + 5] = lin;
//         _REAL_NUM_BLADES += 1;
//     }

//     InterlockedAdd(_IndirectArgsBuffer[0].numVerticesPerInstance, 12); 
// }

// void GrassGen() {
//     for(int i=0; i<LinesNum; i++) {
//         TriangleData tri;
//         VertexData vert0;
//         VertexData vert1;
        
//         // 每次处理两个点
//         vert0 = _Lines[i].vertices[0];
//         vert1 = _Lines[i].vertices[1];

//         float3 rnd = rand3(vert0.positionWS + i) * 2 - 1;
//         float2 cs = float2(sin(rnd.z * PI), cos(rnd.z * PI));
//         float3 right = float3(cs.x, 0, cs.y);

//         float curvation = 0;
//         float3x3 rot0 = _AngleAxis3x3(vert0.weight * curvation, right);
//         float3x3 rot1 = _AngleAxis3x3(vert1.weight * curvation, right);

//         data.lod = __lodLevel(vert0.positionWS); 
//         int _NUM_SEGS = max(data.lod - 1, 0);

//         float3 baseOffset = right * _GrassParams[0]._WIDTH;
//         float3 deltaOffset = baseOffset / (float)(_NUM_SEGS + 2);
        
//         if(vert1.weight==1.0)
//         {
//             float3 v0 = vert0.positionWS + baseOffset;
//             float3 v1 = vert0.positionWS - baseOffset; 
//             float3 v2 = vert1.positionWS;
//             vert.normalWS = vert1.normalWS; vert.weight = vert0.weight; vert.positionWS = v1; tri.vertices[0] = vert;
//             vert.normalWS = vert1.normalWS; vert.weight = vert0.weight; vert.positionWS = v2; tri.vertices[2] = vert;
//             vert.normalWS = vert1.normalWS; vert.weight = vert1.weight; vert.positionWS = v2; tri.vertices[1] = vert;
//             _Triangles.Append(tri);
//         }
//         else
//         {
//             float3 v0 = vert0.positionWS + baseOffset;
//             float3 v1 = vert0.positionWS - baseOffset; 
//             float3 v2 = vert1.positionWS - deltaOffset;
//             float3 v3 = vert1.positionWS + deltaOffset;

//             vert.normalWS = vert0.normalWS; vert.weight = vert0.normalWS; vert.positionWS = v0; tri.vertices[0] = vert;
//             vert.normalWS = vert0.normalWS; vert.weight = vert0.normalWS; vert.positionWS = v1; tri.vertices[2] = vert;
//             vert.normalWS = vert1.normalWS; vert.weight = vert1.normalWS; vert.positionWS = v2; tri.vertices[1] = vert; 
//             _Triangles.Append(tri);

//             vert.normalWS = vert0.normalWS; vert.weight = vert0.normalWS; vert.positionWS = v1; tri.vertices[0] = vert;
//             vert.normalWS = vert1.normalWS; vert.weight = vert1.normalWS; vert.positionWS = v2; tri.vertices[1] = vert;
//             vert.normalWS = vert1.normalWS; vert.weight = vert1.normalWS; vert.positionWS = v3; tri.vertices[2] = vert;
//             _Triangles.Append(tri);
//         }
//     }

//     // 如果要绘制三角形
//     InterlockedAdd(_IndirectArgsBuffer.numVerticesPerInstance, LinesNum * 2 * 3); 
// }
#endif