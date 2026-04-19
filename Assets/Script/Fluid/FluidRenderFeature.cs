using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

public class FluidRenderFeature : ScriptableRendererFeature
{
    [SerializeField] 
    private ComputeShader atrousComputeShader; // Inspector 拖拽赋值

    [Header("Materials")]
    [SerializeField]
    private Shader normalReconstructShader; // 拖拽 FluidSSRNormal.shader 到这里
    [SerializeField]
    private Shader fluidCompositeShader;    // ✅ 新增：流体合成 Shader
    [SerializeField]
    private Shader blitCameraColorShader;    // ✅ 新增：流体合成 Shader

    // 内部 Material 缓存
    private Material _normalMaterial;
    private Material _compositeMaterial;    // ✅ 新增
    private Material _blitMaterial;    // ✅ 新增
    
    private Light mainDirectionalLight; // 拖拽赋值
    
    private FluidSimPass _simPass;
    private CopyOpaquePass _copyOpaquePass;
    private FluidDepthPass _depthPass;
    private FluidDepthSmoothPass _smoothPass;
    private FluidNormalPass _normalPass;
    private FluidThicknessPass _thickPass;
    private FluidShadowPass _shadowPass;  // 新增
    private FluidCompositePass _compositePass; // ✅ 新增：合成 Pass

    // FluidThicknessPass fluidThicknessPass;
    private FluidRenderPass fluidRenderPass;

    private List<SPH_Solver_RenderFeature> _activeSolvers = new();
    

    public override void Create()
    {
        Dispose();

        // 创建 Material（如果 Shader 不为空）
        if (normalReconstructShader != null)
        {
            _normalMaterial = new Material(normalReconstructShader);
            _normalMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        // ✅ 创建合成 Material
        if (fluidCompositeShader != null)
        {
            _compositeMaterial = new Material(fluidCompositeShader);
            _compositeMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogError("[FluidRenderFeature] Fluid Composite Shader is not assigned!");
        }

        if (blitCameraColorShader != null)
        {
            _blitMaterial = new Material(blitCameraColorShader);
            _blitMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        _simPass = new FluidSimPass();
        _copyOpaquePass = new CopyOpaquePass(_blitMaterial);
        _depthPass = new FluidDepthPass();
        _smoothPass = new FluidDepthSmoothPass(atrousComputeShader);
        _normalPass = new FluidNormalPass(_normalMaterial);
        _thickPass = new FluidThicknessPass();
        _shadowPass = new FluidShadowPass();  // 新增
        _compositePass = new FluidCompositePass(_compositeMaterial); // ✅ 新增

        // fluidThicknessPass = new FluidThicknessPass();
        fluidRenderPass = new FluidRenderPass();

        var evt = RenderPassEvent.AfterRenderingSkybox;
        _copyOpaquePass.renderPassEvent = evt;
        _simPass.renderPassEvent = evt;
        _depthPass.renderPassEvent = evt;
        _smoothPass.renderPassEvent = evt;
        _normalPass.renderPassEvent = evt;
        _thickPass.renderPassEvent = evt;
        _shadowPass.renderPassEvent = evt;  // 新增

        // ✅ 合成 Pass 必须在所有不透明物体渲染完成后执行（用于采样背景）
        _compositePass.renderPassEvent = evt;
        
        // fluidDepthSmoothPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        // fluidThicknessPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        // fluidRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        _activeSolvers.Clear();
        foreach (var solver in SPH_Solver_RenderFeature.actives)
        {
            if (solver != null && solver.enabled) 
                _activeSolvers.Add(solver);
        }

        // ⭐【修复关键】：必须在 return 之前，调用 Setup！
        // 这样即使 activeSolvers 是空的，也能把各个 Pass 内部的僵尸引用给强制清空！
        _simPass.Setup(_activeSolvers);
        _depthPass.Setup(_activeSolvers);
        _thickPass.Setup(_activeSolvers);
        _shadowPass.Setup(_activeSolvers);

        // 清空完各个 Pass 的历史残留后，现在可以安全 return 了
        if (_activeSolvers.Count == 0) return;
        if (_normalMaterial == null || _compositeMaterial == null || _blitMaterial == null) return; 

        renderer.EnqueuePass(_copyOpaquePass);
        renderer.EnqueuePass(_simPass);
        renderer.EnqueuePass(_depthPass);

        _smoothPass.Setup(_depthPass);
        renderer.EnqueuePass(_smoothPass);

        _normalPass.Setup(_smoothPass);
        renderer.EnqueuePass(_normalPass);

        renderer.EnqueuePass(_thickPass);
        renderer.EnqueuePass(_shadowPass);

        _compositePass.Setup(_smoothPass, _normalPass, _thickPass, _copyOpaquePass, _shadowPass);
        renderer.EnqueuePass(_compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        // ⭐ 使用 CoreUtils.Destroy 能够完美兼容编辑器和运行时的资源清理
        if (_normalMaterial != null) CoreUtils.Destroy(_normalMaterial);
        if (_compositeMaterial != null) CoreUtils.Destroy(_compositeMaterial);
        if (_blitMaterial != null) CoreUtils.Destroy(_blitMaterial);

        _normalMaterial = null;
        _compositeMaterial = null;
        _blitMaterial = null;

        _smoothPass?.Dispose();
        _smoothPass = null;
        _copyOpaquePass = null;
    }
}