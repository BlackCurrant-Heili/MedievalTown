// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityURP

#if !UNITY_6000_3_OR_NEWER
#define URP_COMPATIBILITY_MODE
#endif

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaveHarmonic.Crest
{
    // Universal Render Pipeline
    partial class WaterRenderer
    {
        internal const RenderPassEvent k_WaterRenderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

        internal sealed class CopyTargetsRenderPass : ScriptableRenderPass
        {
            readonly WaterRenderer _Water;
            public static CopyTargetsRenderPass Instance { get; set; }

            readonly UnityEngine.Rendering.Universal.Internal.CopyDepthPass _CopyDepthPass;
            readonly Shader _CopyDepthShader;
            readonly Material _CopyDepthMaterial;

            static readonly System.Reflection.FieldInfo s_OpaqueColor = typeof(UniversalRenderer).GetField("m_OpaqueColor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            static readonly System.Reflection.FieldInfo s_ActiveRenderPassQueue = typeof(ScriptableRenderer).GetField("m_ActiveRenderPassQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            readonly UnityEngine.Rendering.Universal.Internal.CopyColorPass _CopyColorPass;
            readonly Material _CopyColorMaterial;
            readonly Material _SampleColorMaterial;

            public CopyTargetsRenderPass(WaterRenderer water)
            {
                _Water = water;

                _CopyDepthShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
#if !UNITY_6000_0_OR_NEWER
                _CopyDepthMaterial = new Material(_CopyDepthShader);
#endif

                _CopyDepthPass = new
                (
                    renderPassEvent,
#if UNITY_6000_0_OR_NEWER
                    _CopyDepthShader,
#else
                    _CopyDepthMaterial,
#endif
                    // Will not work in U6 without it.
                    copyToDepth: true,
                    copyResolvedDepth: RenderingUtils.MultisampleDepthResolveSupported(),
                    shouldClear: false
#if UNITY_6000_0_OR_NEWER
                    , customPassName: "Crest.DrawWater"
#endif
                );

                _CopyColorMaterial = new(Shader.Find("Hidden/Universal/CoreBlit"));
                _SampleColorMaterial = new(Shader.Find("Hidden/Universal Render Pipeline/Sampling"));

                _CopyColorPass = new
                (
                    renderPassEvent,
                    _SampleColorMaterial,
                    _CopyColorMaterial
#if UNITY_6000_0_OR_NEWER
                    , customPassName: "Crest.DrawWater"
#endif
                );
            }

            public static void Enable(WaterRenderer water)
            {
                Instance = new(water);
            }

            internal void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                var water = _Water;

                if (!ShouldRender(camera, water.Surface.Layer))
                {
                    return;
                }

                // Our reflections do not need them.
                if (camera == WaterReflections.CurrentCamera)
                {
                    return;
                }


                if (!water.WriteToColorTexture && !water.WriteToDepthTexture)
                {
                    return;
                }

                if (water.Surface.Material == null)
                {
                    return;
                }

                // TODO: Could also check RenderType. Which is better?
                if (!SurfaceRenderer.IsTransparent(water.Surface.Material))
                {
                    return;
                }

                renderPassEvent = water.RenderBeforeTransparency ? RenderPassEvent.BeforeRenderingTransparents : RenderPassEvent.AfterRenderingTransparents;
                _CopyColorPass.renderPassEvent = renderPassEvent;
                _CopyDepthPass.renderPassEvent = renderPassEvent;

                var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
                // Needed for OnCameraSetup.
                renderer.EnqueuePass(this);

#if UNITY_6000_0_OR_NEWER
                // Copy depth pass does not support RG directly.
                if (GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode)
#endif
                {
                    if (water.WriteToColorTexture) renderer.EnqueuePass(_CopyColorPass);
                    if (water.WriteToDepthTexture) renderer.EnqueuePass(_CopyDepthPass);
                }
            }

#if UNITY_6000_0_OR_NEWER
            public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                var descriptor = resources.cameraDepthTexture.GetDescriptor(graph);

                if (_Water.WriteToColorTexture)
                {
                    _CopyColorPass.RenderToExistingTexture(graph, frame, resources.cameraOpaqueTexture, resources.cameraColor, UniversalRenderPipeline.asset.opaqueDownsampling);
                }

                if (_Water.WriteToDepthTexture)
                {
                    // Whether we a writing to color or depth format.
                    _CopyDepthPass.CopyToDepth = descriptor.colorFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                    _CopyDepthPass.Render(graph, frame, resources.cameraDepthTexture, resources.cameraDepth);
                }
            }

            [System.Obsolete]
#endif
            public override void OnCameraSetup(CommandBuffer buffer, ref RenderingData data)
            {
                var renderer = (UniversalRenderer)data.cameraData.renderer;
                var opaqueColorHandle = s_OpaqueColor.GetValue(renderer) as RTHandle;

                // Also check internal RT because it can be null on Vulkan for some reason.
                if (renderer.cameraColorTargetHandle?.rt != null && opaqueColorHandle?.rt != null)
                {
                    _CopyColorPass.Setup(renderer.cameraColorTargetHandle, opaqueColorHandle, UniversalRenderPipeline.asset.opaqueDownsampling);
                }
                else
                {
                    var queue = s_ActiveRenderPassQueue.GetValue(renderer) as List<ScriptableRenderPass>;
                    queue.Remove(_CopyColorPass);
                }

#if URP_COMPATIBILITY_MODE
                // Also check internal RT because it can be null on Vulkan for some reason.
                if (renderer.cameraDepthTargetHandle?.rt != null && renderer.m_DepthTexture?.rt != null)
                {
                    // Whether we a writing to color or depth format.
                    _CopyDepthPass.CopyToDepth = renderer.m_DepthTexture.rt.graphicsFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                    _CopyDepthPass.m_CopyResolvedDepth = false;
                    _CopyDepthPass.Setup(renderer.cameraDepthTargetHandle, renderer.m_DepthTexture);
                }
                else
#endif
                {
                    var queue = s_ActiveRenderPassQueue.GetValue(renderer) as List<ScriptableRenderPass>;
                    queue.Remove(_CopyDepthPass);
                }
            }

#if UNITY_6000_0_OR_NEWER
            [System.Obsolete]
#endif
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                // Blank
            }
        }
    }
}

#endif
