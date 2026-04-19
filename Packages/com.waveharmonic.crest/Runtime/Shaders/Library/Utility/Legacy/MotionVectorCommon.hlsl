// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// TODO:
// #if defined(USING_STEREO_MATRICES)
// float4x4 _StereoNonJitteredVP[2];
// float4x4 _StereoPreviousVP[2];
// #else
// float4x4 _NonJitteredVP;
// float4x4 _PreviousVP;
// #endif

float4x4 _PreviousM;
float4x4 _PreviousVP;
float4x4 _NonJitteredVP;

bool _HasLastPositionData;
bool _ForceNoMotion;
float _MotionVectorDepthBias;

#undef UNITY_PREV_MATRIX_M
#define UNITY_PREV_MATRIX_M _PreviousM
#define _PrevViewProjMatrix _PreviousVP
#define _NonJitteredViewProjMatrix _NonJitteredVP

// X : Use last frame positions (right now skinned meshes are the only objects that use this
// Y : Force No Motion
// Z : Z bias value
const static float4 unity_MotionVectorsParams = float4(_HasLastPositionData, !_ForceNoMotion, _MotionVectorDepthBias, 0);

// Unity will populate this, but could not see when in source.
float4 _LastTime;

// We want to gather some internal data from the BuildVaryings call to
// avoid rereading and recalculating these values again in the ShaderGraph motion vector pass
struct MotionVectorPassOutput
{
    float3 positionOS;
    float3 positionWS;
};

SurfaceDescription BuildSurfaceDescription(Varyings varyings)
{
    SurfaceDescriptionInputs surfaceDescriptionInputs = BuildSurfaceDescriptionInputs(varyings);
    SurfaceDescription surfaceDescription = SurfaceDescriptionFunction(surfaceDescriptionInputs);
    return surfaceDescription;
}

// Very hacky, but works!
#define BuildVaryings(content) BuildVaryings(content, inout MotionVectorPassOutput motionVectorOutput)
#define TransformObjectToWorld(content) TransformObjectToWorld(content); motionVectorOutput.positionOS = input.positionOS; motionVectorOutput.positionWS = positionWS;
