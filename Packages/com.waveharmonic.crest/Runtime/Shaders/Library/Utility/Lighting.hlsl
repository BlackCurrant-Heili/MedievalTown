// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Based on tutorial: https://connect.unity.com/p/adding-your-own-hlsl-code-to-shader-graph-the-custom-function-node

#ifndef CREST_LIGHTING_H
#define CREST_LIGHTING_H

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"

#if CREST_URP
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Unity renamed keyword.
#ifdef USE_FORWARD_PLUS
#define USE_CLUSTER_LIGHT_LOOP USE_FORWARD_PLUS
#endif // USE_FORWARD_PLUS
#endif // CREST_URP

#if CREST_HDRP
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

#if UNITY_VERSION < 202310
#define GetMeshRenderingLayerMask GetMeshRenderingLightLayer
#endif // UNITY_VERSION

#if UNITY_VERSION < 60000000
#if PROBE_VOLUMES_L1
#define AMBIENT_PROBE_BUFFER 1
#endif // PROBE_VOLUMES_L1
#endif // UNITY_VERSION
#endif // CREST_HDRP

m_CrestNameSpace

void PrimaryLight
(
    const float3 i_PositionWS,
    out half3 o_Color,
    out half3 o_Direction
)
{
#if CREST_HDRP
    // We could get the main light the same way we get the main light shadows,
    // but most of the data would be missing (including below horizon
    // attenuation) which would require re-running the light loop which is expensive.
    o_Direction = g_Crest_PrimaryLightDirection;
    o_Color = g_Crest_PrimaryLightIntensity;
#elif CREST_URP
    // Actual light data from the pipeline.
    Light light = GetMainLight();
    o_Direction = light.direction;
    o_Color = light.color;
#elif CREST_BIRP
#ifndef USING_DIRECTIONAL_LIGHT
    // Yes. This function wants the world position of the surface.
    o_Direction = normalize(UnityWorldSpaceLightDir(i_PositionWS));
#else
    o_Direction = _WorldSpaceLightPos0.xyz;
    // Prevents divide by zero.
    if (all(o_Direction == 0)) o_Direction = half3(0.0, 1.0, 0.0);
#endif
    o_Color = _LightColor0.rgb;
#if SHADERPASS == SHADERPASS_FORWARD_ADD
#if !SHADOWS_SCREEN
    // FIXME: undeclared identifier 'IN' in Pass: BuiltIn ForwardAdd, Vertex program with DIRECTIONAL SHADOWS_SCREEN
    UNITY_LIGHT_ATTENUATION(attenuation, IN, i_PositionWS)
    o_Color *= attenuation;
#endif
#endif
#endif
}

half3 AmbientLight(const half3 i_AmbientLight)
{
    half3 ambient = i_AmbientLight;

#ifndef SHADERGRAPH_PREVIEW
#if CREST_HDRP
    // Allows control of baked lighting through volume framework.
    // We could create a BuiltinData struct which would have rendering layers on it, but it seems more complicated.
    ambient *= GetIndirectDiffuseMultiplier(GetMeshRenderingLayerMask());
#endif // CREST_HDRP
#endif // SHADERGRAPH_PREVIEW

    return ambient;
}

half3 AmbientLight()
{
    // Use the constant term (0th order) of SH stuff - this is the average.
    const half3 ambient =
#if AMBIENT_PROBE_BUFFER
        half3(_AmbientProbeData[0].w, _AmbientProbeData[1].w, _AmbientProbeData[2].w);
#else
        half3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
#endif

    return AmbientLight(ambient);
}

half3 AdditionalLighting(const float3 i_PositionWS, const float4 i_ScreenPosition, const float2 i_StaticLightMapUV)
{
    half3 color = 0.0;

#if CREST_URP
#if defined(_ADDITIONAL_LIGHTS)

    // Shadowmask.
#if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
    half4 shadowMask = SAMPLE_SHADOWMASK(i_StaticLightMapUV);
#elif !defined(LIGHTMAP_ON)
    half4 shadowMask = unity_ProbesOcclusion;
#else
    half4 shadowMask = half4(1, 1, 1, 1);
#endif

    uint pixelLightCount = GetAdditionalLightsCount();

#ifdef _LIGHT_LAYERS
    uint meshRenderingLayers = GetMeshRenderingLayer();
#endif

#if USE_CLUSTER_LIGHT_LOOP
    InputData inputData = (InputData)0;
    // For Foward+ LIGHT_LOOP_BEGIN macro uses inputData.normalizedScreenSpaceUV and inputData.positionWS.
    inputData.normalizedScreenSpaceUV = i_ScreenPosition.xy / i_ScreenPosition.w;
    inputData.positionWS = i_PositionWS;
#endif

LIGHT_LOOP_BEGIN(pixelLightCount)
    // Includes shadows and cookies.
    Light light = GetAdditionalLight(lightIndex, i_PositionWS, shadowMask);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
    {
        color += light.color * (light.distanceAttenuation * light.shadowAttenuation);
    }
LIGHT_LOOP_END
#endif // _ADDITIONAL_LIGHTS
#endif // CREST_URP

    // HDRP todo.
    // BIRP has additional lights as additional passes. Handled elsewhere.

    return color;
}

m_CrestNameSpaceEnd

#endif
