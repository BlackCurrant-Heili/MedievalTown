using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

public class FluidThicknessPass : ScriptableRenderPass
{
    private List<SPH_Solver_RenderFeature> _solvers = new();
    private MaterialPropertyBlock _mpb = new();
    
    // 输出给下一个 Pass
    public TextureHandle OutputThickness { get; private set; }

    public void Setup(List<SPH_Solver_RenderFeature> solvers)
    {
        _solvers.Clear();
        _solvers.AddRange(solvers);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_solvers == null || _solvers.Count == 0) return;

        var readySolvers = GetReadySolvers(_solvers);
        if (readySolvers.Count == 0) return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        
        // 创建厚度纹理（R16_SFloat 足够）
        RenderTextureDescriptor desc = new(
            cameraData.cameraTargetDescriptor.width,
            cameraData.cameraTargetDescriptor.height,
            GraphicsFormat.R16_SFloat, 0);
        
        OutputThickness = UniversalRenderer.CreateRenderGraphTexture(
            renderGraph, desc, "_FluidThickness", false);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(
            "CreateThicknessTexture", out var passData))
        {
            builder.SetRenderAttachment(OutputThickness, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);

            // 清空为黑色（Additive混合需要黑色背景）
            builder.AllowGlobalStateModification(true);

            passData.solvers = readySolvers;

            builder.SetRenderFunc((PassData data, RasterGraphContext context) => 
                ExecutePass(data, context));
        }
    }

    private void ExecutePass(PassData data, RasterGraphContext context)
    {
        // 清空为黑色
        context.cmd.ClearRenderTarget(false, true, Color.black);

        foreach (var solver in data.solvers)
        {
            _mpb.Clear();
            _mpb.SetBuffer("_particlesBuffer", solver._particlesBuffer);
            _mpb.SetBuffer("_meshProperties", solver._meshPropertiesBuffer);
            _mpb.SetFloat("_ParticleSize", solver.particleRenderSize);

            // ✅ Pass 1 = Thickness（假设你的 Shader 中厚度是 Pass 1）
            context.cmd.DrawMeshInstancedIndirect(
                solver.mesh, 
                0, 
                solver.material, 
                1,  // Thickness Pass
                solver.argsBuffer, 
                0, 
                _mpb
            );
        }
    }

    private List<SPH_Solver_RenderFeature> GetReadySolvers(List<SPH_Solver_RenderFeature> all)
    {
        var ready = new List<SPH_Solver_RenderFeature>();
        foreach (var s in all)
        {
            if (s != null && s.material != null && s._particlesBuffer != null)
                ready.Add(s);
        }
        return ready;
    }

    private class PassData
    {
        public List<SPH_Solver_RenderFeature> solvers;
    }
}