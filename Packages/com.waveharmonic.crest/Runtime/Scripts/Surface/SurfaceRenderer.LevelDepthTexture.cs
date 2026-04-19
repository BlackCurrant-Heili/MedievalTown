// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// FIXME: Broken for BIRP on MacOS. Either platform specific problem or bug in Unity.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        RenderTexture _WaterLevelDepthTexture;
        internal RenderTexture WaterLevelDepthTexture => _WaterLevelDepthTexture;
        RenderTargetIdentifier _WaterLevelDepthTarget;
        Material _WaterLevelDepthMaterial;

        const string k_WaterLevelDepthTextureName = "Crest Water Level Depth Texture";

        void ExecuteWaterLevelDepthTexture(Camera camera, CommandBuffer buffer)
        {
            Helpers.CreateRenderTargetTextureReference(ref _WaterLevelDepthTexture, ref _WaterLevelDepthTarget);
            _WaterLevelDepthTexture.name = k_WaterLevelDepthTextureName;

            if (_WaterLevelDepthMaterial == null)
            {
                _WaterLevelDepthMaterial = new(Shader.Find("Hidden/Crest/Editor/Water Level (Depth)"));
            }

            var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight)
            {
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                depthBufferBits = 32,
            };

            // Depth buffer.
            buffer.GetTemporaryRT(Helpers.ShaderIDs.s_MainTexture, descriptor);
            CoreUtils.SetRenderTarget(buffer, Helpers.ShaderIDs.s_MainTexture, ClearFlag.Depth);

            Render(camera, buffer, _WaterLevelDepthMaterial);

            Render(camera, buffer, _WaterLevelDepthMaterial);

            // Depth texture.
            // Always release to handle screen size changes.
            _WaterLevelDepthTexture.Release();
            descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat;
            descriptor.depthBufferBits = 0;
            Helpers.SafeCreateRenderTexture(ref _WaterLevelDepthTexture, descriptor);
            _WaterLevelDepthTexture.Create();

            // Convert.
            Helpers.Blit(buffer, _WaterLevelDepthTarget, Helpers.UtilityMaterial, (int)Helpers.UtilityPass.Copy);

            buffer.ReleaseTemporaryRT(Helpers.ShaderIDs.s_MainTexture);
        }

        void EnableWaterLevelDepthTexture()
        {
            if (Application.isPlaying) return;

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                WaterLevelDepthTextureURP.Enable(_Water, this);
            }
#endif

#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                WaterLevelDepthTextureHDRP.Enable(_Water, this);
            }
#endif
        }

        void DisableWaterLevelDepthTexture()
        {
            if (Application.isPlaying) return;

#if d_UnityHDRP
            WaterLevelDepthTextureHDRP.Disable();
#endif
        }
    }
}
