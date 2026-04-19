// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Utility_ShaderGraphDefines
#define d_WaveHarmonic_Utility_ShaderGraphDefines

//
// Defines
//

#ifdef _BUILTIN_SPECULAR_SETUP
#define _SPECULAR_SETUP _BUILTIN_SPECULAR_SETUP
#endif

#ifdef _BUILTIN_TRANSPARENT_RECEIVES_SHADOWS
#define _TRANSPARENT_RECEIVES_SHADOWS _BUILTIN_TRANSPARENT_RECEIVES_SHADOWS
#endif


//
// Passes
//

#define SHADERPASS_FORWARD_ADD (20)
#define SHADERPASS_DEFERRED (21)
#define SHADERPASS_MOTION_VECTORS (22)


//
// Deferred Fix
//

#if (defined(SHADER_API_GLES3) && !defined(SHADER_API_DESKTOP)) || defined(SHADER_API_GLES) || defined(SHADER_API_N3DS)
    #define UNITY_ALLOWED_MRT_COUNT 4
#else
    #define UNITY_ALLOWED_MRT_COUNT 8
#endif

// Required on Windows (and possibly others) to prevent tiling.
#undef UNITY_SAMPLE_FULL_SH_PER_PIXEL
#define UNITY_SAMPLE_FULL_SH_PER_PIXEL 1


//
// Stereo Instancing Fix
//

#if defined(STEREO_INSTANCING_ON) && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_PSSL) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && !defined(UNITY_COMPILER_DXC)))
#define UNITY_STEREO_INSTANCING_ENABLED
#endif

#if defined(STEREO_MULTIVIEW_ON) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_VULKAN)) && !(defined(SHADER_API_SWITCH))
#define UNITY_STEREO_MULTIVIEW_ENABLED
#endif

// Redeclared their includes to insert shadow declarations at the right spot.
// Adapted from:
// Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/Shim/Shims.hlsl

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// Duplicate define in Macros.hlsl
#if defined (TRANSFORM_TEX)
#undef TRANSFORM_TEX
#endif

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
#undef GLOBAL_CBUFFER_START
#if defined(UNITY_STEREO_MULTIVIEW_ENABLED) || ((defined(UNITY_SINGLE_PASS_STEREO) || defined(UNITY_STEREO_INSTANCING_ENABLED)) && (defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL)))
    #define GLOBAL_CBUFFER_START(name)    cbuffer name {
    #define GLOBAL_CBUFFER_END            }
#else
    #define GLOBAL_CBUFFER_START(name)    CBUFFER_START(name)
    #define GLOBAL_CBUFFER_END            CBUFFER_END
#endif
#endif

#include "Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/Shim/HLSLSupportShim.hlsl"

// Fix wrong definitions.
#undef UNITY_SAMPLE_TEX2DARRAY
#define UNITY_SAMPLE_TEX2DARRAY(tex,coord) SAMPLE_TEXTURE2D_ARRAY(tex, sampler##tex, coord.xy, coord.z)


//
// Transparent Objects Receives Shadows
//

#if _SURFACE_TYPE_TRANSPARENT
#if _TRANSPARENT_RECEIVES_SHADOWS
#if SHADERPASS == SHADERPASS_FORWARD || SHADERPASS == SHADERPASS_FORWARD_ADD
#if DIRECTIONAL || DIRECTIONAL_COOKIE
#if !SHADOWS_SCREEN

StructuredBuffer<float4x4> _Crest_WorldToShadow;

// Declarations for shadow collector.
UNITY_DECLARE_SHADOWMAP(_ShadowMapTexture);
float4 _ShadowMapTexture_TexelSize;
#define SHADOWMAPSAMPLER_DEFINED 1
#define SHADOWMAPSAMPLER_AND_TEXELSIZE_DEFINED 1

#define d_Crest_ShadowsOverriden 1

#endif
#endif
#endif
#endif
#endif

#endif // d_WaveHarmonic_Utility_ShaderGraphDefines
