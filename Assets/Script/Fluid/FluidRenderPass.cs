using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;
public class FluidRenderPass : ScriptableRenderPass
{
    public FluidRenderPass(){
    }

    private class PassData
    {
        internal List<SPH_Solver_RenderFeature> solvers;
    }

    public void Setup()
    {
        
    }

    public void Dispose()
    {
        
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var activeSolvers = SPH_Solver_RenderFeature.actives;
        if (activeSolvers.Count == 0) return;

        var readySolvers = new List<SPH_Solver_RenderFeature>();
        foreach (var solver in activeSolvers)
        {
            if(!solver){
                continue;
            }
            if(!solver.material){
                continue;
            }
            readySolvers.Add(solver);
        }
        if (readySolvers.Count == 0) return;

        // ========== 2. Raster Pass: 执行渲染 ==========
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("DrawFluidParticles", out PassData drawPassData))
        {
            drawPassData.solvers = readySolvers;
            var resourceData = frameData.Get<UniversalResourceData>();

            builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, 0);

            builder.SetRenderFunc<PassData>((data, ctx) => ExecutePass(data, ctx));
        }
    }
    private void ExecutePass(PassData data, RasterGraphContext context)
    {
        foreach (var solver in data.solvers)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetBuffer("_particlesBuffer", solver._particlesBuffer);
            mpb.SetBuffer("_meshProperties", solver._meshPropertiesBuffer);
            mpb.SetFloat("_size", solver.particleRenderSize);

            context.cmd.DrawMeshInstancedIndirect(
                solver.mesh,
                0,
                solver.material,
                3,
                solver.argsBuffer,
                0,
                mpb
            );
        }
    }
}