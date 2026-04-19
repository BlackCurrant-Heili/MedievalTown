#include "Common.hlsl"

StructuredBuffer<TriangleData> _Triangles;
StructuredBuffer<LineData> _Lines;
int _MaxSegment;

// LineData lin = _Lines[vertexID / 6];
// VertexDataLine input = lin.vertices[vertexID % 6];
// positionWS = input.positionWS;
// normalWS = input.normalWS;
// weight = input.weight;

void FetchVertex_float(
    uint vertexID,
    out float3 positionWS,
    out float3 normalWS,
    out float3 tangentWS,
    out float2 uv,
    out float weight) {
    TriangleData tri = _Triangles[vertexID / 3];
    VertexDataTriangle input = tri.vertices[vertexID % 3];
    positionWS = input.positionWS;
    normalWS = input.normalWS;
    tangentWS = input.tangentWS;
    uv = input.uv;
    weight = input.weight;
}
