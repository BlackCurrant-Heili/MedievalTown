// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Utility_RenderPipeline_Compute
#define d_WaveHarmonic_Utility_RenderPipeline_Compute

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl"

// Compute does not have an equivalent of PackageRequirements.
// We must handle it ourselves.

// Fallback to BIRP if HDRP package missing.
#if _HRP
#if (CREST_PACKAGE_HDRP != 1)
#undef _HRP
#define _BRP 1
#endif
#endif

// Fallback to BIRP if URP package missing.
#if _URP
#if (CREST_PACKAGE_URP != 1)
#undef _URP
#define _BRP 1
#endif
#endif

#if _BRP
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Utility/Legacy/Core.hlsl"
#endif

#if _HRP
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#endif

#if _URP
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif


//
// Stereo Rendering
//

// Unity 6 only, but had compilation errors for non HDRP anyway:
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"

#ifndef RW_TEXTURE2D_X
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    #define COORD_TEXTURE2D_X(pixelCoord)                                   uint3(pixelCoord, SLICE_ARRAY_INDEX)
    #define RW_TEXTURE2D_X(type, textureName)                               RW_TEXTURE2D_ARRAY(type, textureName)
#else // UNITY_STEREO
    #define COORD_TEXTURE2D_X(pixelCoord)                                   pixelCoord
    #define RW_TEXTURE2D_X                                                  RW_TEXTURE2D
#endif // UNITY_STEREO
#endif // RW_TEXTURE2D_X

#ifndef UNITY_XR_ASSIGN_VIEW_INDEX
// Helper macro to assign view index during compute/ray pass (usually from SV_DispatchThreadID or DispatchRaysIndex())
#if defined(SHADER_STAGE_COMPUTE) || defined(SHADER_STAGE_RAY_TRACING)
    #if defined(UNITY_STEREO_INSTANCING_ENABLED)
        #define UNITY_XR_ASSIGN_VIEW_INDEX(viewIndex) unity_StereoEyeIndex = viewIndex;
    #else
        #define UNITY_XR_ASSIGN_VIEW_INDEX(viewIndex)
    #endif
#endif
#endif // UNITY_XR_ASSIGN_VIEW_INDEX

#endif // d_WaveHarmonic_Utility_RenderPipeline_Compute
