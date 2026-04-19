// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class SurfaceRenderer
    {
        CommandBuffer _WaterLevelDepthBuffer;

        void OnPreRenderWaterLevelDepthTexture(Camera camera)
        {
            if (camera.cameraType != CameraType.SceneView || camera != _Water.Viewer)
            {
                return;
            }

            _WaterLevelDepthBuffer ??= new() { name = k_WaterLevelDepthTextureName };
            _WaterLevelDepthBuffer.Clear();

            ExecuteWaterLevelDepthTexture(camera, _WaterLevelDepthBuffer);

            // Both forward and deferred.
            camera.AddCommandBuffer(CameraEvent.BeforeDepthTexture, _WaterLevelDepthBuffer);
            camera.AddCommandBuffer(CameraEvent.BeforeGBuffer, _WaterLevelDepthBuffer);
        }

        void OnPostRenderWaterLevelDepthTexture(Camera camera)
        {
            if (_WaterLevelDepthBuffer != null)
            {
                // Both forward and deferred.
                camera.RemoveCommandBuffer(CameraEvent.BeforeDepthTexture, _WaterLevelDepthBuffer);
                camera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer, _WaterLevelDepthBuffer);
            }
        }
    }
}
