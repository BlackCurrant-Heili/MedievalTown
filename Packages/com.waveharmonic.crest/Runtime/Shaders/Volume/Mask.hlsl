// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_Mask
#define d_WaveHarmonic_Crest_Mask

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Constants.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/InputsDriven.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Helpers.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Cascade.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Geometry.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Depth.hlsl"

#if (CREST_PORTALS != 0)
#include "Packages/com.waveharmonic.crest.portals/Runtime/Shaders/Library/Portals.hlsl"
#endif

m_CrestNameSpace

struct Attributes
{
    // The old unity macros require this name and type.
    float4 positionCS : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
#if d_LodInput
    float3 positionWS : TEXCOORD;
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vertex(const Attributes i_Input)
{
    // This will work for all pipelines.
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(i_Input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    const uint slice0 = _Crest_LodIndex;
    const uint slice1 = _Crest_LodIndex + 1;

    const Cascade cascade0 = Cascade::Make(slice0);
    const Cascade cascade1 = Cascade::Make(slice1);

    float3 positionWS = mul(UNITY_MATRIX_M, float4(i_Input.positionCS.xyz, 1.0)).xyz;

#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
    positionWS.xz += _WorldSpaceCameraPos.xz;
#endif

    float alpha;
    SnapAndTransitionVertLayout(_Crest_ChunkMeshScaleAlpha, cascade0, _Crest_ChunkGeometryGridWidth, positionWS, alpha);

    {
        // Scale up by small "epsilon" to solve numerical issues. Expand slightly about tile center.
        // :WaterGridPrecisionErrors
        float2 tileCenterXZ = UNITY_MATRIX_M._m03_m23;
#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
        tileCenterXZ += _WorldSpaceCameraPos.xz;
#endif
        const float2 cameraPositionXZ = abs(_WorldSpaceCameraPos.xz);
        positionWS.xz = lerp(tileCenterXZ, positionWS.xz, lerp(1.0, 1.01, max(cameraPositionXZ.x, cameraPositionXZ.y) * 0.00001));
    }

    const float weight0 = (1.0 - alpha) * cascade0._Weight;
    const float weight1 = (1.0 - weight0) * cascade1._Weight;

    const float2 positionXZ = positionWS.xz;

    // Data that needs to be sampled at the undisplaced position.
    if (weight0 > m_CrestSampleLodThreshold)
    {
        Cascade::MakeAnimatedWaves(slice0).SampleDisplacement(positionXZ, weight0, positionWS);
    }
    if (weight1 > m_CrestSampleLodThreshold)
    {
        Cascade::MakeAnimatedWaves(slice1).SampleDisplacement(positionXZ, weight1, positionWS);
    }

#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
    positionWS.xz -= _WorldSpaceCameraPos.xz;
#endif

    output.positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));

#if d_LodInput
    output.positionWS = positionWS;
#endif

    return output;
}

half4 Fragment(const Varyings i_Input, const bool i_FrontFace)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i_Input);

#if d_LodInput
    return half4(i_Input.positionWS.y - g_Crest_WaterCenter.y, 0, 0, 1);
#endif

    half result = 0.0;

#if (CREST_PORTALS != 0)
#if !d_Tunnel
    if (m_CrestPortal)
    {
        Portal::EvaluateMask(i_Input.positionCS);
    }
#endif
#endif

    if (IsUnderWater(i_FrontFace, g_Crest_ForceUnderwater))
    {
        result = CREST_MASK_BELOW_SURFACE;
    }
    else
    {
        result = CREST_MASK_ABOVE_SURFACE;
    }

#if (CREST_PORTALS != 0)
#if d_Crest_NegativeVolumePass
    result = Portal::FixMaskForNegativeVolume(result, i_Input.positionCS.xy);
#endif

#if d_Tunnel
    const float2 positionSS = i_Input.positionCS.xy;
    const float ffz = LOAD_DEPTH_TEXTURE_X(_Crest_PortalFogBeforeTexture, positionSS);
    const float bfz = LOAD_DEPTH_TEXTURE_X(_Crest_PortalFogAfterTexture, positionSS);
    if (ffz <= 0.0 && bfz > 0.0)
    {
        result = CREST_MASK_ABOVE_SURFACE;
    }
#endif
#endif

    return (half4)result;
}

m_CrestNameSpaceEnd

m_CrestVertex
m_CrestFragmentWithFrontFace(half4)

#endif // d_WaveHarmonic_Crest_Mask
