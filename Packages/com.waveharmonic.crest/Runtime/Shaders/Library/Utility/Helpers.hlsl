// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Utility_Helpers
#define d_WaveHarmonic_Utility_Helpers

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Macros.hlsl"

m_UtilityNameSpace

void Swap(inout float a, inout float b)
{
    float t = a; a = b; b = t;
}

// Adapted from:
// https://alex.vlachos.com/graphics/Alex_Vlachos_Advanced_VR_Rendering_GDC2015.pdf
float3 ScreenSpaceDither(const float2 i_ScreenPosition)
{
    // Iestyn's RGB dither (7 asm instructions) from Portal 2 X360, slightly modified for VR.
    float3 dither = dot(float2(171.0, 231.0), i_ScreenPosition.xy);
    dither.rgb = frac(dither.rgb / float3(103.0, 71.0, 97.0)) - float3(0.5, 0.5, 0.5);
    return (dither.rgb / 255.0);
}

float2 WorldNormalToScreenDirection(const float3 i_PositionWS, const float3 i_NormalWS, const float4x4 i_MatrixVP, const float i_Offset)
{
    const float3 p0 = i_PositionWS;
    const float3 p1 = p0 + i_NormalWS * i_Offset;

    const float4 clip0 = mul(i_MatrixVP, float4(p0, 1));
    const float4 clip1 = mul(i_MatrixVP, float4(p1, 1));

    const float2 uv0 = (clip0.xy / clip0.w) * 0.5 + 0.5;
    const float2 uv1 = (clip1.xy / clip1.w) * 0.5 + 0.5;

    float2 direction = normalize(uv1 - uv0);

#if UNITY_UV_STARTS_AT_TOP
    direction.y = -direction.y;
#endif

    return direction;
}

m_UtilityNameSpaceEnd

#endif // d_WaveHarmonic_Utility_Helpers
