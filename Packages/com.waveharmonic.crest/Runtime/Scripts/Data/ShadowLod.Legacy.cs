// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class ShadowLod
    {
        internal void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // TODO: refactor this similar to MaskRenderer.
            if (!RenderPipelineHelper.IsLegacy)
            {
#if d_UnityURP
                if (RenderPipelineHelper.IsUniversal)
                {
                    SampleShadowsURP.EnqueuePass(context, camera);
                }
#endif

                return;
            }

#if UNITY_EDITOR
            // Do not execute when editor is not active to conserve power and prevent possible leaks.
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            {
                CopyShadowMapBuffer?.Clear();
                return;
            }

            if (!WaterRenderer.IsWithinEditorUpdate)
            {
                CopyShadowMapBuffer?.Clear();
                return;
            }
#endif

            var water = _Water;

            if (water == null)
            {
                return;
            }

            if (!WaterRenderer.ShouldRender(camera, water.Surface.Layer))
            {
                return;
            }

            if (camera == water.Viewer && CopyShadowMapBuffer != null)
            {
                if (_Light != null)
                {
                    // Calling this in OnPreRender was too late to be executed in the same frame.
                    _Light.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, CopyShadowMapBuffer);
                    _Light.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, CopyShadowMapBuffer);
                }

                // Disable for XR SPI otherwise input will not have correct world position.
                Rendering.BIRP.DisableXR(CopyShadowMapBuffer, camera);

                BuildCommandBuffer(water, CopyShadowMapBuffer);

                // Restore XR SPI as we cannot rely on remaining pipeline to do it for us.
                Rendering.BIRP.EnableXR(CopyShadowMapBuffer, camera);
            }
        }

        internal void OnEndCameraRendering(Camera camera)
        {
            if (!RenderPipelineHelper.IsLegacy)
            {
                return;
            }

#if UNITY_EDITOR
            // Do not execute when editor is not active to conserve power and prevent possible leaks.
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            {
                CopyShadowMapBuffer?.Clear();
                return;
            }

            if (!WaterRenderer.IsWithinEditorUpdate)
            {
                CopyShadowMapBuffer?.Clear();
                return;
            }
#endif

            var water = _Water;

            if (water == null)
            {
                return;
            }

            if (!WaterRenderer.ShouldRender(camera, water.Surface.Layer))
            {
                return;
            }

            if (camera == water.Viewer)
            {
                // CBs added to a light are executed for every camera, but the LOD data is only
                // supports a single camera. Removing the CB after the camera renders restricts the
                // CB to one camera. Careful of recursive rendering for planar reflections, as it
                // executes a camera within this camera's frame.
                if (_Light != null && CopyShadowMapBuffer != null)
                {
                    _Light.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, CopyShadowMapBuffer);
                }
            }
        }
    }
}
