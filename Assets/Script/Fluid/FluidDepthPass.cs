using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;
public class FluidDepthPass : ScriptableRenderPass
{
    private List<SPH_Solver_RenderFeature> _solvers = new();
    private MaterialPropertyBlock _mpb = new(); // 字段复用
    
    // 输出给下一个 Pass
    public TextureHandle OutputDepth { get; private set; }

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

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        
        // 创建深度纹理
        RenderTextureDescriptor desc = new(
            cameraData.cameraTargetDescriptor.width,
            cameraData.cameraTargetDescriptor.height,
            GraphicsFormat.R32G32_SFloat, 0); // Unity 6 推荐 R32_SFloat 用于深度
        
        OutputDepth = UniversalRenderer.CreateRenderGraphTexture(
            renderGraph, desc, "_FluidDepth", false);

        // ✅ 2. 创建一个临时的深度纹理（极其关键，否则ZWrite/ZTest无效）
        RenderTextureDescriptor depthDesc = new RenderTextureDescriptor(
            cameraData.cameraTargetDescriptor.width,
            cameraData.cameraTargetDescriptor.height,
            GraphicsFormat.None,            // 颜色格式留空
            GraphicsFormat.D32_SFloat       // 分配32位深度
        );
        TextureHandle tempDepth = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc, "_FluidTempDepth", false);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("CreateDepthtexture", out var passData))
        {
            builder.SetRenderAttachment(OutputDepth, 0, AccessFlags.Write);
            // ✅ 3. 将其绑定为深度附件
            builder.SetRenderAttachmentDepth(tempDepth, AccessFlags.Write);
            builder.AllowPassCulling(false);

            passData.solvers = readySolvers;

            builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
        }
    }

    private void ExecutePass(PassData data, RasterGraphContext context)
    {
        // ✅ 4. 极其关键：在绘制前清空颜色（背景深度）和深度缓冲（否则会有花屏和重影）
        context.cmd.ClearRenderTarget(true, true, Color.black);

        foreach (var solver in data.solvers)
        {
            _mpb.Clear();
            _mpb.SetBuffer("_particlesBuffer", solver._particlesBuffer);
            _mpb.SetBuffer("_meshProperties", solver._meshPropertiesBuffer);
            _mpb.SetFloat("_ParticleSize", solver.particleRenderSize);

            // ✅ 调用Shader的第二个Pass（DepthOnly）
            context.cmd.DrawMeshInstancedIndirect(
                solver.mesh, 
                0, 
                solver.material, 
                0,
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