using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

public class CopyOpaquePass : ScriptableRenderPass
{
    private Material _biltMaterial;
    public TextureHandle SavedTexture { get; private set; }

    public CopyOpaquePass(Material material)
    {
        _biltMaterial = material;
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
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
        if (!_biltMaterial) return;
        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();
        TextureHandle cameraColor = resourceData.activeColorTexture;

        // 创建深度纹理
        RenderTextureDescriptor desc = new(
            cameraData.cameraTargetDescriptor.width,
            cameraData.cameraTargetDescriptor.height,
            GraphicsFormat.R32G32B32A32_SFloat, 0);
        
        SavedTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_BlitColor", false);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("BlitCameraColor", out var passData))
        {
            builder.UseTexture(cameraColor, AccessFlags.Read);
            builder.SetRenderAttachment(SavedTexture, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            passData.material = _biltMaterial;
            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                data.material.SetTexture("_CameraOpaqueTexture", cameraColor);
                ctx.cmd.DrawMesh(GetFullscreenMesh(), Matrix4x4.identity, data.material, 0, 0);
            });
        }
    }
    

    // ✅ 修改：添加 sourceTexture 字段
    private class PassData 
    { 
        public Material material; 
        public TextureHandle sourceTexture; // 新增
    }
}