using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;
public class FluidDepthSmoothPass : ScriptableRenderPass
{
    private ComputeShader _compute;
    private int _kernel;
    private RTHandle _ping;
    private RTHandle _pong;
    private FluidDepthPass _depthPass;  // ✅ 持有引用

    [SerializeField] private int _iterations = 5;
    [SerializeField] private float _sigmaDepth = 20f;

    // 输出
    public TextureHandle OutputSmoothDepth { get; private set; }

    public FluidDepthSmoothPass(ComputeShader compute)
    {
        _compute = compute;
        
        if (_compute == null)
        {
            //Debug.LogWarning("[FluidDepthSmoothPass] Compute shader null");
            _kernel = -1;
            return;
        }
        
        if (!_compute.HasKernel("AtrousWaveletFilter"))
        {
            //Debug.LogError("[FluidDepthSmoothPass] Kernel missing");
            _kernel = -1;
            return;
        }
        
        _kernel = _compute.FindKernel("AtrousWaveletFilter");
    }
    public void Setup(FluidDepthPass depthPass)  // ✅ 新方法
    {
        _depthPass = depthPass;
    }
    public bool IsValid() => _compute != null && _kernel >= 0;

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // ✅ 在这里获取，确保 DepthPass 已经执行
        if (_depthPass == null) return;

        TextureHandle inputDepth = _depthPass.OutputDepth;
        //Debug.Log($"[SmoothPass] Got InputDepth: {inputDepth.IsValid()}");

        if (!IsValid() || !inputDepth.IsValid()) return;

        var cameraData = frameData.Get<UniversalCameraData>();
        int width = cameraData.cameraTargetDescriptor.width;
        int height = cameraData.cameraTargetDescriptor.height;

        // 创建输出纹理
        var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.RGFloat, 0)
        {
            enableRandomWrite = true  // ✅ 关键：允许 Compute Shader 写入
        };
        
        OutputSmoothDepth = UniversalRenderer.CreateRenderGraphTexture(
            renderGraph, desc, "_FluidSmoothDepth", false);

        // Ping-Pong
        EnsureBuffers(width, height);
        TextureHandle ping = renderGraph.ImportTexture(_ping);
        TextureHandle pong = renderGraph.ImportTexture(_pong);

        // A-Trous 迭代
        TextureHandle currentInput = inputDepth;

        for (int i = 0; i < _iterations; i++)
        {
            bool isLast = i == _iterations - 1;
            TextureHandle currentOutput = isLast ? OutputSmoothDepth : (i % 2 == 0 ? pong : ping);
            int stepSize = 1 << i;

            using (var builder = renderGraph.AddComputePass<PassData>(
                $"A-Trous {i}", out var passData))
            {
                builder.UseTexture(currentInput, AccessFlags.Read);
                builder.UseTexture(currentOutput, AccessFlags.Write);

                passData.compute = _compute;
                passData.kernel = _kernel;
                passData.stepSize = stepSize;
                passData.sigmaDepth = _sigmaDepth;
                passData.size = new Vector2Int(width, height);

                var input = currentInput;
                var output = currentOutput;
                int step = stepSize;

                builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;
                    
                    cmd.SetComputeTextureParam(data.compute, data.kernel, "_DepthTexture", input);
                    cmd.SetComputeTextureParam(data.compute, data.kernel, "_OutputTexture", output);
                    cmd.SetComputeIntParam(data.compute, "_StepSize", step);
                    cmd.SetComputeFloatParam(data.compute, "_SigmaDepth", data.sigmaDepth);
                    cmd.SetComputeVectorParam(data.compute, "_TextureSize",
                        new Vector4(data.size.x, data.size.y, 0, 0));

                    cmd.DispatchCompute(data.compute, data.kernel,
                        (data.size.x + 7) / 8, (data.size.y + 7) / 8, 1);
                });
            }

            currentInput = currentOutput;
        }
    }

    private void EnsureBuffers(int width, int height)
    {
        if (_ping != null && (_ping.rt.width != width || _ping.rt.height != height))
        {
            RTHandles.Release(_ping);
            RTHandles.Release(_pong);
            _ping = _pong = null;
        }

        if (_ping == null)
        {
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.RGFloat, 0)
            {
                enableRandomWrite = true
            };
            _ping = RTHandles.Alloc(desc, name: "_AtrousPing");
            _pong = RTHandles.Alloc(desc, name: "_AtrousPong");
        }
    }

    public void Dispose()
    {
        RTHandles.Release(_ping);
        RTHandles.Release(_pong);
        _ping = null;
        _pong = null;
    }

    private class PassData
    {
        public ComputeShader compute;
        public int kernel;
        public int stepSize;
        public float sigmaDepth;
        public Vector2Int size;
    }
}