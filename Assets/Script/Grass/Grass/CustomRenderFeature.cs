using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CustomRenderFeature : ScriptableRendererFeature
{
    [Header("RT尺寸设置")]
    public int Width = 256;
    public int Height = 256;
    public float FadeOutSpeed = 1;

    private CustomRenderPass renderPass;

    public override void Create()
    {
        renderPass = new CustomRenderPass(this);
        renderPass.Init();
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        renderPass.Setup();
    }

    protected override void Dispose(bool disposing)
    {
        renderPass.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        SetupRenderPasses(renderer, renderingData);
        renderer.EnqueuePass(renderPass);
    }
}