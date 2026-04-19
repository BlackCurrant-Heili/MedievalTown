// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_SHADOWS_H
#define CREST_SHADOWS_H

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"

#if CREST_BIRP
TEXTURE2D_X(_Crest_ScreenSpaceShadowTexture);
float4 _Crest_ScreenSpaceShadowTexture_TexelSize;
#endif // CREST_BIRP

#if CREST_URP
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif // CREST_URP

#if CREST_HDRP
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

#ifndef SHADERGRAPH_PREVIEW
#if CREST_HDRP_FORWARD_PASS
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
#endif // CREST_HDRP_FORWARD_PASS
#endif // SHADERGRAPH_PREVIEW
#endif // CREST_HDRP

m_CrestNameSpace

// Position: SRP = WS / BIRP = SS (z ignored)
half PrimaryLightShadows(const float3 i_Position, const float2 i_ScreenPosition)
{
    // Unshadowed.
    half shadow = 1;

#if CREST_URP
    // We could skip GetMainLight but this is recommended approach which is likely more robust to API changes.
    float4 shadowCoord = TransformWorldToShadowCoord(i_Position);
    Light light = GetMainLight(TransformWorldToShadowCoord(i_Position));
    shadow = light.shadowAttenuation;
#endif

#ifndef SHADERGRAPH_PREVIEW
#if CREST_HDRP_FORWARD_PASS
    DirectionalLightData light = _DirectionalLightDatas[_DirectionalShadowIndex];
    HDShadowContext context = InitShadowContext();
    context.directionalShadowData = _HDDirectionalShadowData[_DirectionalShadowIndex];

    float3 positionWS = GetCameraRelativePositionWS(i_Position);
    // From Unity:
    // > With XR single-pass and camera-relative: offset position to do lighting computations from the combined center view (original camera matrix).
    // > This is required because there is only one list of lights generated on the CPU. Shadows are also generated once and shared between the instanced views.
    ApplyCameraRelativeXR(positionWS);

    // TODO: Pass in screen space position and scene normal.
    shadow = GetDirectionalShadowAttenuation
    (
        context,
        0, // positionSS
        positionWS,
        0, // normalWS
        light.shadowIndex,
        -light.forward
    );

    // Apply shadow strength from main light.
    shadow = LerpWhiteTo(shadow, light.shadowDimmer);
#endif // CREST_HDRP_FORWARD_PASS
#endif // SHADERGRAPH_PREVIEW

#if CREST_BIRP
    shadow = LOAD_TEXTURE2D_X(_Crest_ScreenSpaceShadowTexture, min(i_ScreenPosition, _Crest_ScreenSpaceShadowTexture_TexelSize.zw - 1.0)).r;
#if DIRECTIONAL_COOKIE
    const half attenuation = tex2D(_LightTexture0, mul(unity_WorldToLight, float4(i_Position, 1.0)).xy).w;
    shadow = min(attenuation, shadow);
#endif
#endif

    return shadow;
}

m_CrestNameSpaceEnd

#endif
