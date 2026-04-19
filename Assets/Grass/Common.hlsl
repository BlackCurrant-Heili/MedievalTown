#ifndef _COMMON_HLSL
#define _COMMON_HLSL

#include "./Math.hlsl"

struct GrassParams {
    float _HEIGHT; 
    float _WIDTH;
    float _WindSize;
    float _WindSpeed;
    float _Stiffness;
    float _LocalConstraintStiffness;
    int _Segment;
    float _WindBase;
    float _WindDirectionX;
    float _WindDirectionZ;
};

struct OriginalGrassData {
    float3 positionWS;
};

struct GrassData {
    int lod;
    float3 positionWS;
};

struct VertexDataTriangle {
    float3 normalWS;
    float3 positionWS;
    float weight;
    float2 uv;           // 新增
    float4 tangentWS;    // 新增
};

struct VertexDataLine {
    float3 normalWS;
    float3 positionWS;
    float3 positionWSGra;
    float3 positionWSOri;
    float weight;
    float length;
    int movable;
    float2 uv;         
    float4 tangentWS;
};

struct TriangleData {
    VertexDataTriangle vertices[3];
};

struct LineData {
    int isPhysic;
    int realSeg;
    VertexDataLine vertices[8];
}; 

struct IndirectArgs {
    uint numVerticesPerInstance;
    uint numInstances;
    uint startVertexIndex;
    uint startInstanceIndex;
    uint startLocation;
};

#endif