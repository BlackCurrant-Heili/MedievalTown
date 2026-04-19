using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

public class FluidShadowPass : ScriptableRenderPass
{
    private List<SPH_Solver_RenderFeature> _solvers = new();
    private MaterialPropertyBlock _mpb = new();

    // 输出
    public TextureHandle OutputShadowMap { get; private set; }

    // 参数
    [SerializeField] private int _shadowMapSize = 2048;
    [SerializeField] private float _shadowDistance = 60f;
    [SerializeField] private float _shadowBias = 0.005f;

    // 运行时数据
    private Vector3 _lightDir;
    private Matrix4x4 _lightViewMatrix;
    private Matrix4x4 _lightProjMatrix;
    private Matrix4x4 _zFlip ;

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

        // 从 RenderPipeline 获取光源（纯数据驱动）
        if (!TryGetMainLightDirection(frameData, out _lightDir))
        {
            // Debug.LogWarning("[FluidShadowPass] No valid light found, skipping shadow pass");
            return;
        }

        // 计算光源矩阵
        CalculateLightMatrices(readySolvers);

        // 验证矩阵有效性
        if (_lightViewMatrix == Matrix4x4.zero || _lightProjMatrix == Matrix4x4.zero)
        {
            // Debug.LogError("[FluidShadowPass] Invalid light matrices");
            return;
        }

        // 创建阴影图
        RenderTextureDescriptor desc = new(
            _shadowMapSize,
            _shadowMapSize,
            GraphicsFormat.R8G8B8A8_SRGB, //.R32_SFloat,
            0);

        OutputShadowMap = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_FluidShadowMap", false);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Fluid Shadow", out var passData))
        {
            builder.SetRenderAttachment(OutputShadowMap, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            passData.solvers = readySolvers;
            // Debug.LogError($"=== MATRIX DEBUG ===");
            // Debug.LogError($"_lightViewMatrix:\n{_lightViewMatrix}");
            // Debug.LogError($"_zFlip:\n{_zFlip}");
            // Debug.LogError($"_lightProjMatrix:\n{_lightProjMatrix}");

            passData.lightViewProj = _lightProjMatrix * _zFlip * _lightViewMatrix;

            // Debug.LogError($"Final VP (_lightProj * _zFlip * _lightView):\n{passData.lightViewProj}");

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                ExecuteShadowRender(data, ctx);
            });
        }
    }

    /// <summary>
    /// 从 RenderPipeline 获取主光源方向（现代游戏管线标准做法）
    /// </summary>
    private bool TryGetMainLightDirection(ContextContainer frameData, out Vector3 lightDir)
    {
        lightDir = Vector3.down; // 默认

        // 尝试从 UniversalLightData 获取（Unity 6 推荐）
        var lightData = frameData.Get<UniversalLightData>();

        if (lightData != null && lightData.visibleLights.Length > 0)
        {
            // 找主光源索引
            int mainIndex = lightData.mainLightIndex;
            if (mainIndex >= 0 && mainIndex < lightData.visibleLights.Length)
            {
                var mainLight = lightData.visibleLights[mainIndex];
                lightDir = ExtractLightDirection(mainLight);
                //  Debug.Log($"[FluidShadowPass] Using main light index {mainIndex}: {lightDir}");
                return true;
            }

            // 备用：找第一个 Directional
            foreach (var vl in lightData.visibleLights)
            {
                if (vl.lightType == LightType.Directional)
                {
                    lightDir = ExtractLightDirection(vl);
                    Debug.Log($"[FluidShadowPass] Using first Directional: {lightDir}");
                    return true;
                }
            }
        }

        // 终极备用：从 Shader 全局变量
        Vector4 mainLightPos = Shader.GetGlobalVector("_MainLightPosition");
        if (mainLightPos != Vector4.zero)
        {
            lightDir = mainLightPos.w < 0.5f ? (Vector3)mainLightPos : -(Vector3)mainLightPos;
            // Debug.Log($"[FluidShadowPass] Using Shader global: {lightDir}");
            return true;
        }

        // Debug.LogWarning("[FluidShadowPass] Failed to get light from any source");
        return false;
    }

    /// <summary>
    /// 从 VisibleLight 提取光照方向
    /// </summary>
    private Vector3 ExtractLightDirection(VisibleLight visibleLight)
    {
        // Directional: 方向是 -forward
        // Point/Spot: 从物体指向光源（简化处理）
        return visibleLight.light.transform.forward;
    }

    private void CalculateLightMatrices(List<SPH_Solver_RenderFeature> solvers)
    {
        Bounds fluidBounds = CalculateFluidBounds(solvers);
        
        // 现在 fluidBounds.center 应该是 (30,30,30)
        // Debug.Log($"[Bounds] Center: {fluidBounds.center}, Min: {fluidBounds.min}, Max: {fluidBounds.max}");

        
        // 光源位置：在中心上方
        Vector3 lightPos = fluidBounds.center - _lightDir * _shadowDistance;

        // 构建 View 矩阵：看向 (30,30,30)
        Vector3 up = Mathf.Abs(Vector3.Dot(_lightDir, Vector3.up)) > 0.99f 
            ? Vector3.forward 
            : Vector3.up;

        // Matrix4x4.LookAt(from, to, up);
        // 1 计算三个正交基向量（世界空间）
        // 2 构建 View 矩阵
        // LookAt函数返回矩阵包含Right 向量、Up 向量、Forward 向量和Position
        // 直接使用这个矩阵作为 _lightViewMatrix 是错误的！
        _lightViewMatrix = Matrix4x4.LookAt(lightPos, fluidBounds.center, up);
        // _lightViewMatrix = Matrix4x4.LookAt(new Vector3(0, 80, 0), new Vector3(30, 0, 30), new Vector3(0, 0, 1));
        _lightViewMatrix = _lightViewMatrix.inverse;
        //_lightViewMatrix = Matrix4x4.identity;

        // 创建 Z 翻转矩阵（第3列乘以 -1）,为了解决fucking Z方向反转问题
        _zFlip = Matrix4x4.identity;
        _zFlip[2, 2] = -1;  // Z 轴翻转

        float orthoSize = 40f;
        _lightProjMatrix = Matrix4x4.Ortho(-orthoSize, orthoSize, -orthoSize, orthoSize, 0.1f, 90f);
        // GL.GetGPUProjectionMatrix 默认假设 View 空间是右手坐标系，相机看向 -Z（即前方点的 Z_view 为负值）。
        _lightProjMatrix = GL.GetGPUProjectionMatrix(_lightProjMatrix, false);
        // _lightProjMatrix = Matrix4x4.identity;
        
        // Debug.Log($"[Shadow] LightPos: {lightPos}, Target: {fluidBounds.center}, OrthoSize: {orthoSize}");
    }

    private Bounds CalculateFluidBounds(List<SPH_Solver_RenderFeature> solvers)
    {
        Bounds bounds = new Bounds();
        bool first = true;

        foreach (var solver in solvers)
        {
            if (solver == null) continue;

            Vector3 min = new Vector3(0, 0, 0);
            Vector3 max = new Vector3(solver.dimensions - 1, solver.dimensions - 1, solver.dimensions - 1);
            
            Vector3 center = (min + max) * 0.5f;  // (30, 30, 30)
            Vector3 size = max - min;             // (60, 60, 60)

            Bounds b = new Bounds(center, size);

            if (first)
            {
                bounds = b;
                first = false;
            }
            else
            {
                bounds.Encapsulate(b);
            }
        }

        return bounds;
    }

    private void ExecuteShadowRender(PassData data, RasterGraphContext context)
    {
        // 清空为远平面（白色 = 最大深度）
        // context.cmd.ClearRenderTarget(true, true, Color.red);
        // context.cmd.ClearRenderTarget(true, true, Color.white);
        context.cmd.ClearRenderTarget(true, true, Color.black);

        // 设置矩阵
        context.cmd.SetGlobalMatrix("_LightViewProj", data.lightViewProj);

        Matrix4x4 m = data.lightViewProj;
        // Debug.LogError($"[MATRIX] Row0: {m.GetRow(0)}");
        // Debug.LogError($"[MATRIX] Row1: {m.GetRow(1)}");
        // Debug.LogError($"[MATRIX] Row2: {m.GetRow(2)}");
        // Debug.LogError($"[MATRIX] Row3: {m.GetRow(3)}");

        foreach (var solver in data.solvers)
        {
            // 立即读取并打印 argsBuffer
            uint[] args = new uint[5];
            solver.argsBuffer.GetData(args);
            // Debug.LogError($"[CRITICAL] Args: IndexCount={args[0]}, InstanceCount={args[1]}, StartIndex={args[2]}, BaseVertex={args[3]}");


            _mpb.Clear();
            _mpb.SetBuffer("_particlesBuffer", solver._particlesBuffer);
            _mpb.SetBuffer("_meshProperties", solver._meshPropertiesBuffer);
            _mpb.SetFloat("_ParticleSize", solver.particleRenderSize);

            // solver.material.EnableKeyword("DEBUG_SHADOW");
            solver.material.DisableKeyword("DEBUG_SHADOW");

            context.cmd.DrawMeshInstancedIndirect(
                solver.mesh,
                0,
                solver.material,
                2, // Shadow Pass
                solver.argsBuffer,
                0,
                _mpb);
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

    public Matrix4x4 GetLightViewProjMatrix() => _lightProjMatrix  * _zFlip * _lightViewMatrix;

    private class PassData
    {
        public List<SPH_Solver_RenderFeature> solvers;
        public Matrix4x4 lightViewProj;
    }
}