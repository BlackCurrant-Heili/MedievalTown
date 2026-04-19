// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if ENABLE_VR && d_UnityModuleVR
#define _XR_ENABLED
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.XR;

namespace WaveHarmonic.Crest
{
    static partial class Rendering
    {
        public static partial class BIRP
        {
            static partial class ShaderIDs
            {
                public static readonly int s_InverseViewProjection = Shader.PropertyToID("_Crest_InverseViewProjection");
            }

            public static Texture GetWhiteTexture(Camera camera)
            {
#if _XR_ENABLED
                if (camera.stereoEnabled && SinglePassXR)
                {
                    return WhiteTextureXR;
                }
#endif

                return Texture2D.whiteTexture;
            }

            public static void SetMatrices(Camera camera)
            {
                Shader.SetGlobalMatrix(ShaderIDs.s_InverseViewProjection, (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix).inverse);

#if _XR_ENABLED
                SetMatricesXR(camera);
#endif
            }

            public enum FrameBufferFormatOverride
            {
                None,
                LDR,
                HDR,
            }

            public static RenderTextureDescriptor GetCameraTargetDescriptor(Camera camera, FrameBufferFormatOverride hdrOverride = FrameBufferFormatOverride.None)
            {
                RenderTextureDescriptor descriptor;

#if _XR_ENABLED
                if (camera.stereoEnabled)
                {
                    // Will not set the following correctly:
                    // - HDR format
                    descriptor = XRSettings.eyeTextureDesc;
                }
                else
#endif
                {
                    // As recommended by Unity, in 2021.2 using SystemInfo.GetGraphicsFormat with DefaultFormat.LDR is
                    // necessary or gamma color space texture is returned:
                    // https://docs.unity3d.com/ScriptReference/Experimental.Rendering.DefaultFormat.html
                    descriptor = new(camera.pixelWidth, camera.pixelHeight, SystemInfo.GetGraphicsFormat(DefaultFormat.LDR), 0);
                }

                // Set HDR format.
                if (camera.allowHDR && QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    var format = DefaultFormat.HDR;

                    if (hdrOverride is not FrameBufferFormatOverride.None)
                    {
                        format = hdrOverride is FrameBufferFormatOverride.HDR ? DefaultFormat.HDR : DefaultFormat.LDR;
                    }
#if UNITY_ANDROID || UNITY_IOS || UNITY_TVOS
                    else
                    {
                        format = DefaultFormat.LDR;
                    }
#endif

                    descriptor.graphicsFormat = SystemInfo.GetGraphicsFormat(format);
                }

                return descriptor;
            }
        }
    }
}
