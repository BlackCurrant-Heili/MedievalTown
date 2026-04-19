// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    sealed partial class SampleShadowsURP : ScriptableRenderPass
    {
        static SampleShadowsURP s_Instance;
        internal static bool Created => s_Instance != null;

        WaterRenderer _Water;

        SampleShadowsURP(RenderPassEvent renderPassEvent)
        {
            this.renderPassEvent = renderPassEvent;
        }

        internal static void Enable(WaterRenderer water)
        {
            s_Instance ??= new(RenderPassEvent.AfterRenderingShadows);
            s_Instance._Water = water;
        }

        internal static void EnqueuePass(ScriptableRenderContext context, Camera camera)
        {
            if (s_Instance == null)
            {
                return;
            }

            var water = s_Instance._Water;

            if (!water._ShadowLod.Enabled)
            {
                return;
            }

#if UNITY_EDITOR
            if (!WaterRenderer.IsWithinEditorUpdate)
            {
                return;
            }
#endif

            // Only sample shadows for the main camera.
            if (!ReferenceEquals(water.Viewer, camera))
            {
                return;
            }

            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData))
            {
                cameraData.scriptableRenderer.EnqueuePass(s_Instance);
            }
        }

#if UNITY_6000_0_OR_NEWER
        void Execute(ScriptableRenderContext context, CommandBuffer buffer, PassData renderingData)
#else
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
#endif
        {
            var water = _Water;

            if (water == null || !water._ShadowLod.Enabled)
            {
                return;
            }

            // TODO: This may not be the same as WaterRenderer._primaryLight. Not certain how to support overriding the
            // main light for shadows yet.
            var mainLightIndex = renderingData.lightData.mainLightIndex;

            if (mainLightIndex == -1)
            {
                return;
            }

            var camera = renderingData.cameraData.camera;

#if !UNITY_6000_0_OR_NEWER
            var buffer = CommandBufferPool.Get(WaterRenderer.k_DrawLodData);
#endif

            // Disable for XR SPI otherwise input will not have correct world position.
            Rendering.URP.DisableXR(buffer, renderingData.cameraData);

            water._ShadowLod.BuildCommandBuffer(water, buffer);

            // Restore matrices otherwise remaining render will have incorrect matrices. Each pass is responsible for
            // restoring matrices if required.
            buffer.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

            // Restore XR SPI as we cannot rely on remaining pipeline to do it for us.
            Rendering.URP.EnableXR(buffer, renderingData.cameraData);

#if !UNITY_6000_0_OR_NEWER
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
#endif
        }
    }
}

#endif // d_UnityURP
