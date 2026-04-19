using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

public class FluidNormalPass : ScriptableRenderPass
{
    private Material _normalMaterial;
    private FluidDepthSmoothPass _smoothPass;
    
    // 缓存全屏 Quad（避免每帧创建）
    private static Mesh _fullscreenMesh;

    public TextureHandle OutputNormal { get; private set; }

    public FluidNormalPass(Material material)
    {
        _normalMaterial = material;
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public void Setup(FluidDepthSmoothPass smoothPass)
    {
        _smoothPass = smoothPass;
    }

    // 创建标准全屏 Quad（覆盖整个屏幕，UV [0,1]）
    private static Mesh GetFullscreenMesh()
    {
        if (_fullscreenMesh != null) return _fullscreenMesh;
        
        _fullscreenMesh = new Mesh { name = "Fullscreen Quad" };
        _fullscreenMesh.SetVertices(new List<Vector3> {
            new Vector3(-1, -1, 0), // 左下
            new Vector3(-1,  1, 0), // 左上
            new Vector3( 1, -1, 0), // 右下
            new Vector3( 1,  1, 0)  // 右上
        });
        _fullscreenMesh.SetUVs(0, new List<Vector2> {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 0),
            new Vector2(1, 1)
        });
        _fullscreenMesh.SetIndices(new[] {0, 1, 2, 2, 1, 3}, MeshTopology.Triangles, 0);
        _fullscreenMesh.UploadMeshData(true);
        return _fullscreenMesh;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // Debug.Log("[FluidNormalPass] ========================= RecordRenderGraph START");
        // Debug.Log($"[FluidNormalPass] _smoothPass == null: {_smoothPass == null}");
        
        if (_smoothPass == null) 
        {
            Debug.LogError("[FluidNormalPass] _smoothPass is NULL, exit!");
            return;
        }
        
        TextureHandle smoothDepth = _smoothPass.OutputSmoothDepth;

        // Debug.Log($"[FluidNormalPass] smoothDepth.IsValid(): {smoothDepth.IsValid()}");
    
        if (!smoothDepth.IsValid()) 
        {
            // Debug.LogError("[FluidNormalPass] smoothDepth is INVALID, exit!");
            return;
        }
        
        // Debug.Log("[FluidNormalPass] All checks passed, creating resources...");

        var cameraData = frameData.Get<UniversalCameraData>();
        int width = cameraData.cameraTargetDescriptor.width;
        int height = cameraData.cameraTargetDescriptor.height;

        // 法线用 ARGBHalf (RGBA16F) 存储 View Space Normal XYZ + 可选的 W
        var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 0);
        OutputNormal = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_FluidNormalTexture", false);

        // 计算逆投影矩阵
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, false);
        Matrix4x4 invProj = proj.inverse;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("FluidNormalReconstruct", out var passData))
        {
            // 写入法线纹理（作为 color attachment 0）
            builder.SetRenderAttachment(OutputNormal, 0, AccessFlags.Write);
            // 声明读取依赖（让 RenderGraph 知道我们要读 smoothDepth）
            builder.UseTexture(smoothDepth, AccessFlags.Read);
            // ✅ 关键：强制保留这个 Pass
            builder.AllowPassCulling(false);
            
            passData.material = _normalMaterial;
            passData.smoothDepth = smoothDepth;
            passData.invProj = invProj;
            
            // 设置渲染函数
            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                // 设置材质参数
                data.material.SetTexture("_FluidSmoothDepth", data.smoothDepth);
                data.material.SetMatrix("_InvProjectionMatrix", data.invProj);
                
                // ✅ 使用 DrawMesh 绘制全屏 Quad（兼容 RasterCommandBuffer）
                ctx.cmd.DrawMesh(GetFullscreenMesh(), Matrix4x4.identity, data.material, 0, 0);
            });
        }
    }

    private class PassData
    {
        public Material material;
        public TextureHandle smoothDepth;
        public Matrix4x4 invProj;
    }
}