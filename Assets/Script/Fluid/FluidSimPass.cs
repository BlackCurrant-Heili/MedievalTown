using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;
public class FluidSimPass : ScriptableRenderPass
{
    private List<SPH_Solver_RenderFeature> _solvers = new();

    public void Setup(List<SPH_Solver_RenderFeature> solvers)
    {
        _solvers.Clear();
        _solvers.AddRange(solvers); // 拷贝，解耦
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_solvers == null || _solvers.Count == 0) return;

        var readySolvers = GetReadySolvers(_solvers);
        if (readySolvers.Count == 0) return;

        // ========== 1. Compute Pass: 执行仿真 ==========
        using (var computeBuilder = renderGraph.AddComputePass<PassData>("SPH_Simulation", out PassData simPassData))
        {
            simPassData.solvers = readySolvers;

            // 声明所有Buffer的写入依赖（关键）
            foreach (var solver in readySolvers)
            {
                computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._particlesBuffer), AccessFlags.ReadWrite);
                computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._velocitiesBuffer), AccessFlags.ReadWrite);
                computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._forcesBuffer), AccessFlags.ReadWrite);
                computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._densitiesBuffer), AccessFlags.ReadWrite);
                computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._pressuresBuffer), AccessFlags.ReadWrite);

                computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._hashGridBuffer), AccessFlags.Write);
                computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._hashGridTrackerBuffer), AccessFlags.Write);
                // computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._neighbourListBuffer), AccessFlags.Write);
                // computeBuilder.UseBuffer(renderGraph.ImportBuffer(solver._neighbourTrackerBuffer), AccessFlags.Write);
                computeBuilder.UseTexture(renderGraph.ImportTexture(solver.velocityFieldRTHandle)); //RTHandles.Alloc()
            }

            computeBuilder.SetRenderFunc<PassData>((data, ctx) => ExecuteSimulation(data, ctx));
        }
    }

    private void ExecuteSimulation(PassData data, ComputeGraphContext context)
    {
        foreach (var solver in data.solvers)
        {
            // 执行预录制的仿真命令（在CommandBuffer中）
            solver.ExecuteSimulation(context.cmd);
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