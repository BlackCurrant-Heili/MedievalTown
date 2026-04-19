// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/Editor/ShaderGraph/Includes/LegacyBuilding.hlsl"

//
// Transparent Objects Receives Shadows
//

#if d_Crest_ShadowsOverriden

#define unity_WorldToShadow _Crest_WorldToShadow

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Shadows.hlsl"

#if defined(SHADER_API_MOBILE)
#define m_UnitySampleShadowmap_PCF UnitySampleShadowmap_PCF5x5
#else
#define m_UnitySampleShadowmap_PCF UnitySampleShadowmap_PCF7x7
#endif

// Same as UnityComputeShadowFadeDistance, except it uses keywords.
float ComputeShadowFadeDistance(float3 positionWS, float viewZ)
{
    // Use keyword instead of unity_ShadowFadeCenterAndType.w, as we are already
    // dependent on keywords anyway.
    return
#if SHADOWS_SPLIT_SPHERES
        distance(positionWS, unity_ShadowFadeCenterAndType.xyz);
#else
        viewZ;
#endif
}

float GetShadows(float3 positionWS, float4 uvLightMap)
{
    float viewZ = -UnityWorldToViewPos(positionWS).z;
    float4 weights = GET_CASCADE_WEIGHTS(positionWS, viewZ);
    float4 coordinates = GET_SHADOW_COORDINATES(float4(positionWS, 1.0), weights);
#if SHADOWS_SOFT
    half shadow = m_UnitySampleShadowmap_PCF(coordinates, 0);
#else
    half shadow = UNITY_SAMPLE_SHADOW(_ShadowMapTexture, coordinates);
#endif
    shadow = lerp(_LightShadowData.r, 1.0, shadow);

    // Shadow Mask + mixed sun + static
#if LIGHTMAP_ON && SHADOWS_SHADOWMASK && LIGHTMAP_SHADOW_MIXING
    float fade = UnityComputeShadowFade(ComputeShadowFadeDistance(positionWS, viewZ));
    half mask = UnitySampleBakedOcclusion(uvLightMap.xy, positionWS);
    shadow = UnityMixRealtimeAndBakedShadows(shadow, mask, fade);
#endif

    return shadow;
}

#ifdef DIRECTIONAL
#undef UNITY_LIGHT_ATTENUATION
#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
    fixed destName = GetShadows(worldPos, input.lmap);
#endif

#ifdef DIRECTIONAL_COOKIE
#undef UNITY_LIGHT_ATTENUATION
#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
    DECLARE_LIGHT_COORD(input, worldPos); \
    fixed destName = tex2D(_LightTexture0, lightCoord).w * GetShadows(worldPos, input.lmap);
#endif

#endif // d_Crest_ShadowsOverriden


//
// Specular
//

#ifdef _SPECULAR_SETUP
#define SurfaceOutputStandard SurfaceOutputStandardSpecular
#define BuildStandardSurfaceOutput BuildStandardSpecularSurfaceOutput
#define LightingStandard LightingStandardSpecular
#define LightingStandard_GI LightingStandardSpecular_GI
#define LightingStandard_Deferred LightingStandardSpecular_Deferred

#if SHADERPASS == SHADERPASS_FORWARD_ADD
#undef LightingStandard
#define LightingStandard(x, y, z) LightingStandardSpecular(x, y, z); c.rgb += o.Emission;
#endif
#endif

#ifndef _SPECULAR_SETUP
#if SHADERPASS == SHADERPASS_FORWARD_ADD
#define LightingStandard(x, y, z) LightingStandard(x, y, z); c.rgb += o.Emission;
#endif // SHADERPASS_FORWARD_ADD
#endif // _SPECULAR_SETUP

SurfaceOutputStandardSpecular BuildStandardSpecularSurfaceOutput(SurfaceDescription surfaceDescription, InputData inputData)
{
    SurfaceData surface = SurfaceDescriptionToSurfaceData(surfaceDescription);

    SurfaceOutputStandardSpecular o = (SurfaceOutputStandardSpecular)0;
    o.Albedo = surface.albedo;
    o.Normal = inputData.normalWS;
    o.Specular = surface.specular;
    o.Smoothness = surface.smoothness;
    o.Occlusion = surface.occlusion;
    o.Emission = surface.emission;
    o.Alpha = surface.alpha;
    return o;
}
