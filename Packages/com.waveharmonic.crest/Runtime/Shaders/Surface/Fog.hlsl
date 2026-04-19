// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_SurfaceFog
#define d_WaveHarmonic_Crest_SurfaceFog

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"

#define d_Crest_WaterSurface 1

#if (CREST_LEGACY_UNDERWATER != 1)
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Volume/Graph/IntegrateWaterVolume.hlsl"
#endif

m_CrestNameSpace

#if (CREST_LEGACY_UNDERWATER != 0)
static bool s_IsUnderWater;
#endif

void SetUpFog(bool i_Underwater, float3 i_PositionWS, float i_Multiplier, float i_FogDistance, half3 i_ViewWS, float2 i_PositionSS)
{
    s_IsUnderWater = i_Underwater;

#if (CREST_LEGACY_UNDERWATER != 1)
    s_PositionSS = i_PositionSS;
    s_PositionWS = i_PositionWS;
    s_ViewWS = i_ViewWS;
    s_FogDistance = i_FogDistance;
    s_DepthRaw = 0;
    s_FogMultiplier = i_Multiplier;
#endif
}

m_CrestNameSpaceEnd

#if (CREST_LEGACY_UNDERWATER != 0)
#if (CREST_DISCARD_ATMOSPHERIC_SCATTERING != 0)

#if CREST_BIRP
#ifdef UNITY_PASS_FORWARDADD
#define m_Unity_FogColor fixed4(0, 0, 0, 0)
#else
#define m_Unity_FogColor unity_FogColor
#endif // UNITY_PASS_FORWARDADD

#undef UNITY_APPLY_FOG
#define UNITY_APPLY_FOG(coord, color) \
if (!m_Crest::s_IsUnderWater) \
{ \
    UNITY_APPLY_FOG_COLOR(coord, color, m_Unity_FogColor); \
}
#endif // CREST_BIRP

#if CREST_HDRP
#define EvaluateAtmosphericScattering(i, V, color) m_Crest::s_IsUnderWater ? color : EvaluateAtmosphericScattering(i, V, color)
#endif

#if CREST_URP
#define MixFog(color, coord) m_Crest::s_IsUnderWater ? color : MixFog(color, coord)
#endif

#endif // CREST_DISCARD_ATMOSPHERIC_SCATTERING
#endif // CREST_LEGACY_UNDERWATER

#endif // d_WaveHarmonic_Crest_SurfaceFog
