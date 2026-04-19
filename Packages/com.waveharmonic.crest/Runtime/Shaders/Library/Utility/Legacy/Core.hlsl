// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Builds on Unity's shim for Shader Graph.

#define BUILTIN_TARGET_API 1

#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Defines.hlsl"
#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/Shim/Shims.hlsl"
#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/Core.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/InputsDriven.hlsl"

#ifndef d_WaveHarmonic_Utility_LegacyCore
#define d_WaveHarmonic_Utility_LegacyCore


//
// Inputs
//

#undef UNITY_MATRIX_I_VP

#if defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
float4x4 _Crest_StereoInverseViewProjection[2];
#define UNITY_MATRIX_I_VP _Crest_StereoInverseViewProjection[unity_StereoEyeIndex]
#else
float4x4 _Crest_InverseViewProjection;
#define UNITY_MATRIX_I_VP _Crest_InverseViewProjection
#endif

// Not set and _ScreenParams.zw is "1.0 + 1.0 / _ScreenParams.xy"
#define _ScreenSize float4(_ScreenParams.xy, float2(1.0, 1.0) / _ScreenParams.xy)

#endif // d_WaveHarmonic_Utility_LegacyCore
