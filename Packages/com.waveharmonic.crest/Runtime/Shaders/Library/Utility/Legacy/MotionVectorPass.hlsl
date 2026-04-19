// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Adapted from:
// Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/MotionVectorPass.hlsl

// This file is subject to the Unity Companion License:
// https://github.com/Unity-Technologies/Graphics/blob/61584ec20cf305929dae85cec7b94ff2ed3942f3/LICENSE.md

#ifndef SG_MOTION_VECTORS_PASS_INCLUDED
#define SG_MOTION_VECTORS_PASS_INCLUDED

#undef BuildVaryings
#undef TransformObjectToWorld

float2 CalcNdcMotionVectorFromCsPositions(float4 posCS, float4 prevPosCS)
{
    // Note: unity_MotionVectorsParams.y is 0 is forceNoMotion is enabled
    bool forceNoMotion = unity_MotionVectorsParams.y == 0.0;
    if (forceNoMotion)
        return float2(0.0, 0.0);

    // Non-uniform raster needs to keep the posNDC values in float to avoid additional conversions
    // since uv remap functions use floats
    float2 posNDC = posCS.xy * rcp(posCS.w);
    float2 prevPosNDC = prevPosCS.xy * rcp(prevPosCS.w);

    float2 velocity;
    {
        // Calculate forward velocity
        velocity = (posNDC.xy - prevPosNDC.xy);
        #if UNITY_UV_STARTS_AT_TOP
        velocity.y = -velocity.y;
        #endif

        // Convert velocity from NDC space (-1..1) to UV 0..1 space
        // Note: It doesn't mean we don't have negative values, we store negative or positive offset in UV space.
        // Note: ((posNDC * 0.5 + 0.5) - (prevPosNDC * 0.5 + 0.5)) = (velocity * 0.5)
        velocity.xy *= 0.5;
    }

    return velocity;
}

struct MotionVectorPassAttributes
{
    float3 previousPositionOS  : TEXCOORD4; // Contains previous frame local vertex position (for skinned meshes)
};

// Note: these will have z == 0.0f in the pixel shader to save on bandwidth
struct MotionVectorPassVaryings
{
    float4 positionCSNoJitter;
    float4 previousPositionCSNoJitter;
};

struct PackedMotionVectorPassVaryings
{
    float3 positionCSNoJitter         : CLIP_POSITION_NO_JITTER;
    float3 previousPositionCSNoJitter : PREVIOUS_CLIP_POSITION_NO_JITTER;
};

PackedMotionVectorPassVaryings PackMotionVectorVaryings(MotionVectorPassVaryings regularVaryings)
{
    PackedMotionVectorPassVaryings packedVaryings;
    packedVaryings.positionCSNoJitter = regularVaryings.positionCSNoJitter.xyw;
    packedVaryings.previousPositionCSNoJitter = regularVaryings.previousPositionCSNoJitter.xyw;
    return packedVaryings;
}

MotionVectorPassVaryings UnpackMotionVectorVaryings(PackedMotionVectorPassVaryings packedVaryings)
{
    MotionVectorPassVaryings regularVaryings;
    regularVaryings.positionCSNoJitter = float4(packedVaryings.positionCSNoJitter.xy, 0, packedVaryings.positionCSNoJitter.z);
    regularVaryings.previousPositionCSNoJitter = float4(packedVaryings.previousPositionCSNoJitter.xy, 0, packedVaryings.previousPositionCSNoJitter.z);
    return regularVaryings;
}

float3 GetLastFrameDeformedPosition(Attributes input, MotionVectorPassOutput currentFrameMvData, float3 previousPositionOS)
{
    Attributes lastFrameInputAttributes = input;
    lastFrameInputAttributes.positionOS = previousPositionOS;

    VertexDescriptionInputs lastFrameVertexDescriptionInputs = BuildVertexDescriptionInputs(lastFrameInputAttributes);
#if defined(AUTOMATIC_TIME_BASED_MOTION_VECTORS) && defined(GRAPH_VERTEX_USES_TIME_PARAMETERS_INPUT)
    lastFrameVertexDescriptionInputs.TimeParameters = _LastTime.yxz;
#endif

    VertexDescription lastFrameVertexDescription = VertexDescriptionFunction(lastFrameVertexDescriptionInputs);
    previousPositionOS = lastFrameVertexDescription.Position.xyz;

    return previousPositionOS;
}

// -------------------------------------
// Vertex
void vert(
    Attributes input,
    MotionVectorPassAttributes passInput,
    out PackedMotionVectorPassVaryings packedMvOutput,
    out PackedVaryings packedOutput)
{
    Varyings output = (Varyings)0;
    MotionVectorPassVaryings mvOutput = (MotionVectorPassVaryings)0;
    MotionVectorPassOutput currentFrameMvData = (MotionVectorPassOutput)0;
    output = BuildVaryings(input, currentFrameMvData);
    packedOutput = PackVaryings(output);

    const bool forceNoMotion = unity_MotionVectorsParams.y == 0.0;

    if (!forceNoMotion)
    {
        const bool hasDeformation = unity_MotionVectorsParams.x == 1; // Mesh has skinned deformation
        float3 previousPositionOS = hasDeformation ? passInput.previousPositionOS : input.positionOS;

    #if defined(AUTOMATIC_TIME_BASED_MOTION_VECTORS) && defined(GRAPH_VERTEX_USES_TIME_PARAMETERS_INPUT)
        const bool applyDeformation = true;
    #else
        const bool applyDeformation = hasDeformation;
    #endif

#if defined(FEATURES_GRAPH_VERTEX)
    if (applyDeformation)
        previousPositionOS = GetLastFrameDeformedPosition(input, currentFrameMvData, previousPositionOS);
    else
        previousPositionOS = currentFrameMvData.positionOS;

    #if defined(FEATURES_GRAPH_VERTEX_MOTION_VECTOR_OUTPUT)
        previousPositionOS -= currentFrameMvData.motionVector;
    #endif
#endif

        mvOutput.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(currentFrameMvData.positionWS, 1.0f));
        mvOutput.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, float4(previousPositionOS, 1.0f)));
    }

    packedMvOutput = PackMotionVectorVaryings(mvOutput);
}

// -------------------------------------
// Fragment
float4 frag(
    // Note: packedMvInput needs to be before packedInput as otherwise we get the following error in the speed tree 8 SG:
    // "Non system-generated input signature parameter () cannot appear after a system generated value"
    PackedMotionVectorPassVaryings packedMvInput,
    PackedVaryings packedInput) : SV_Target
{
    Varyings input = UnpackVaryings(packedInput);
    MotionVectorPassVaryings mvInput = UnpackMotionVectorVaryings(packedMvInput);
    UNITY_SETUP_INSTANCE_ID(input);
    SurfaceDescription surfaceDescription = BuildSurfaceDescription(input);

#if defined(_ALPHATEST_ON)
    clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
#endif

    return float4(CalcNdcMotionVectorFromCsPositions(mvInput.positionCSNoJitter, mvInput.previousPositionCSNoJitter), 0, 0);
}
#endif
