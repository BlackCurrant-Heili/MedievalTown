using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

public class FluidCompositePass : ScriptableRenderPass
{
    private Material _compositeMaterial;
    
    // ✅ 改为持有 Pass 引用，而不是直接持有 TextureHandle
    private FluidDepthSmoothPass _smoothPass;
    private FluidNormalPass _normalPass;
    private FluidThicknessPass _thicknessPass;
    private FluidShadowPass _shadowPass;
    private CopyOpaquePass _copyPass;

    public FluidCompositePass(Material material)
    {
        _compositeMaterial = material;
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    // ✅ Setup 接收 Pass 引用（参考 FluidNormalPass 模式）
    public void Setup(FluidDepthSmoothPass smoothPass, FluidNormalPass normalPass, 
                     FluidThicknessPass thicknessPass, CopyOpaquePass copyPass, FluidShadowPass shadowPass = null)
    {
        _smoothPass = smoothPass;
        _normalPass = normalPass;
        _thicknessPass = thicknessPass;
        _shadowPass = shadowPass;
        _copyPass = copyPass;
    }

    private static Mesh GetFullscreenMesh()
    {
        if (_fullscreenMesh != null) return _fullscreenMesh;
        
        _fullscreenMesh = new Mesh { name = "Fullscreen Quad" };
        _fullscreenMesh.SetVertices(new List<Vector3> {
            new Vector3(-1, -1, 0), new Vector3(-1, 1, 0),
            new Vector3(1, -1, 0), new Vector3(1, 1, 0)
        });
        _fullscreenMesh.SetUVs(0, new List<Vector2> {
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 0), new Vector2(1, 1)
        });
        _fullscreenMesh.SetIndices(new[] {0, 1, 2, 2, 1, 3}, MeshTopology.Triangles, 0);
        _fullscreenMesh.UploadMeshData(true);
        return _fullscreenMesh;
    }
    private static Mesh _fullscreenMesh;

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // ✅ 关键：在这里实时获取 TextureHandle（Render Graph 已确保前置 Pass 执行完毕）
        if (_smoothPass == null || _normalPass == null || _thicknessPass == null)
        {
            Debug.LogError("[FluidCompositePass] Some pass reference is null!");
            return;
        }

        TextureHandle savedBackground = _copyPass.SavedTexture;
        TextureHandle smoothDepth = _smoothPass.OutputSmoothDepth;
        TextureHandle normal = _normalPass.OutputNormal;
        TextureHandle thickness = _thicknessPass.OutputThickness;
        TextureHandle shadow = _shadowPass?.OutputShadowMap ?? default;
        
        if (!smoothDepth.IsValid() || !normal.IsValid() || !thickness.IsValid())
        {
            Debug.LogError($"[FluidCompositePass] Invalid texture: smooth={smoothDepth.IsValid()}, normal={normal.IsValid()}, thick={thickness.IsValid()}");
            return;
        }
        
        if (!_compositeMaterial) return;

        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();
        
        TextureHandle cameraColor = resourceData.activeColorTexture;
        TextureHandle cameraDepth = resourceData.activeDepthTexture;
        
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("FluidComposite", out var passData))
        {
            // 声明读取依赖
            builder.UseTexture(smoothDepth, AccessFlags.Read);
            builder.UseTexture(normal, AccessFlags.Read);
            builder.UseTexture(thickness, AccessFlags.Read);

            // 使用 depthToUse 而不是 cameraDepth
            builder.UseTexture(cameraDepth, AccessFlags.Read); 
            builder.UseTexture(savedBackground, AccessFlags.Read);  
            
            if (shadow.IsValid())
                builder.UseTexture(shadow, AccessFlags.Read);
            
            // ✅ 直接设置为渲染目标（隐含 ReadWrite 访问）
            builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);
            
            // 强制保留此 Pass
            builder.AllowPassCulling(false);

            passData.material = _compositeMaterial;
            passData.invProj = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, false).inverse;
            passData.viewToWorld = cameraData.camera.cameraToWorldMatrix;

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                // 在 RenderFunc 中设置材质参数
                data.material.SetTexture("_FluidSmoothDepth", smoothDepth);
                data.material.SetTexture("_FluidNormal", normal);
                data.material.SetTexture("_FluidThickness", thickness);
                data.material.SetTexture("_CameraOpaqueTexture", savedBackground);
                data.material.SetTexture("_CameraDepthTexture", cameraDepth);
                data.material.SetMatrix("_InvProjectionMatrix", data.invProj);
                data.material.SetMatrix("_ViewToWorldMatrix", data.viewToWorld);
                
                ctx.cmd.DrawMesh(GetFullscreenMesh(), Matrix4x4.identity, data.material, 0, 0);
            });
        }
    }

    private class PassData
    {
        public Material material;
        public Matrix4x4 invProj;
        public Matrix4x4 viewToWorld;
    }
}