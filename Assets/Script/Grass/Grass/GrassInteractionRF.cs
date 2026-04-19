using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Mathematics;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class GrassInteractionRF : ScriptableRendererFeature
{
    [Header("RT尺寸设置")]
    public int Width = 256;
    public int Height = 256;
    public float FadeOutSpeed = 1;
    
    class GrassInteractionPass : ScriptableRenderPass
    {
        RenderTexture frontRT;
        RenderTexture backRT;
        Material preFrameMat;
        float4 aabb_front;
        float4 aabb_back;

        bool isFirstFrame = true;

        readonly GrassInteractionRF feature; // 用于访问外部设置

        public GrassInteractionPass(GrassInteractionRF feature)
        {
            this.feature = feature;
        }

        class PassData
        {
            public Camera camera;
            public float4 aabbFront;
            public float4 aabbBack;
            public RenderTexture frontRT;
            public RenderTexture backRT;
            public Material preFrameMat;
            public bool isFirstFrame;
        }

        // 修改为接受UnsafeGraphContext而不是RasterGraphContext
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            try
            {
                // 获取原生CommandBuffer，这样可以使用更多功能
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                // 设置渲染目标 - 使用RenderTexture而不是TextureHandle
                cmd.SetRenderTarget(data.backRT);

                // 设置全局纹理和向量 - 使用RenderTexture
                cmd.SetGlobalTexture("_GrassPrevRT", data.frontRT);
                cmd.SetGlobalTexture("_GrassCurrRT", data.backRT);
                cmd.SetGlobalVector("_GrassPrevAABB", data.aabbFront);
                cmd.SetGlobalVector("_GrassCurrAABB", data.aabbBack);

                // 1. 记录当前VP
                Matrix4x4 _V = data.camera.worldToCameraMatrix;
                Matrix4x4 _P = data.camera.projectionMatrix;

                // 2. 构造正交P
                float minX = data.aabbBack.x, minZ = data.aabbBack.y, maxX = data.aabbBack.z, maxZ = data.aabbBack.w;
                float groundY = 0;
                float2 size = new(maxX - minX, maxZ - minZ);
                size *= 0.5f;
                Matrix4x4 P = Matrix4x4.Ortho(-size.x, size.x, -size.y, size.y, -5.0f, 5.0f);
                P = GL.GetGPUProjectionMatrix(P, true);
                // 3. 构造V
                float cx = (minX + maxX) * 0.5f;
                float cz = (minZ + maxZ) * 0.5f;
                float cy = groundY;

                // 相机位置（在世界空间中）
                Vector3 cameraPosition = new Vector3(cx, cy, cz);
                // 相机看向的点（向下看，所以 Y 坐标减小）
                Vector3 targetPosition = new Vector3(cx, cy - 1, cz);
                // 相机的上方向（这里使用 Z 轴作为上方向）
                Vector3 upDirection = new Vector3(0, 0, 1);

                // 构建视图矩阵
                Matrix4x4 V = Matrix4x4.LookAt(cameraPosition, targetPosition, upDirection);
                V.SetColumn(3, new Vector4(-cx, cz, -cy, 1));

                // 3. 设置新VP
                cmd.SetViewProjectionMatrices(V, P);
                
                cmd.SetGlobalFloat("_DeltaTime", Time.deltaTime);

                // 4. 绘制interactionRT
                if (data.isFirstFrame)// 第一次绘制时填充底色，后继绘制接着上一帧的结果继续画
                {
                    cmd.ClearRenderTarget(false, true, new Color(0.5f, 0.5f, 0f, 1f));
                    Debug.Log("firstFrame clear");
                }

                // 5. 降低上一帧记录的方向的强度
                // TODO: 用上一帧aabb左下角与当前aabb左下角的偏移量计算出uv偏移值传给材质
                cmd.DrawMesh(MeshTool.Quad, Matrix4x4.identity, data.preFrameMat);

                // 6. 绘制当前帧
                if (GrassInteractions.instance.grassColliderCount > 0)
                    cmd.DrawMeshInstanced(MeshTool.Quad, 0, GrassInteractions.instance.material, 0, GrassInteractions.instance.grassColliderMatrices, GrassInteractions.instance.grassColliderCount);

                // 7. 恢复旧VP
                cmd.SetViewProjectionMatrices(_V, _P);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ExecutePass执行出错: {e.Message}\n{e.StackTrace}");
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "GrassInteractionRT";

            // 计算 AABB
            // UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            float4 aabb = GrassInteractions.instance.AABB_Range;

            if (preFrameMat == null)
                preFrameMat = new Material(Shader.Find("Universal Render Pipeline/fillFullScreen"));

            preFrameMat.SetFloat("_FadeOut", feature.FadeOutSpeed);

            // 在 RecordRenderGraph 中
            if (frontRT == null || backRT == null)
            {
                // 创建持久的 RenderTexture
                frontRT = new RenderTexture(feature.Width, feature.Height, 0, RenderTextureFormat.ARGB32);
                frontRT.name = "GrassRT_Front";
                frontRT.Create();

                backRT = new RenderTexture(feature.Width, feature.Height, 0, RenderTextureFormat.ARGB32);
                backRT.name = "GrassRT_Back";
                backRT.Create();

                aabb_front = aabb;
                aabb_back = aabb;
            }
            else
            {
                // 交换 RT
                (frontRT, backRT) = (backRT, frontRT);

                // 交换 AABB
                aabb_front = aabb_back;
                aabb_back = aabb;
            }

            // 使用AddUnsafePass代替AddRasterRenderPass
            using var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData);
            passData.camera = cameraData.camera;

            // 赋值Pass中需要用到的参数
            passData.aabbFront = aabb_front;
            passData.aabbBack = aabb_back;
            passData.frontRT = frontRT;
            passData.backRT = backRT;
            passData.preFrameMat = preFrameMat;
            passData.isFirstFrame = isFirstFrame;

            // 更新GrassCollider的矩阵
            GrassInteractions.instance.UpdateGrassColliderMatrices();

            // 没搞懂unity把pass给culling掉是什么标准，这里禁用
            builder.AllowPassCulling(false);

            // 执行Pass
            builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
            {
                ExecutePass(data, context);
            });

            isFirstFrame = false;
        }

        // 添加清理RenderTexture的方法
        public void Cleanup()
        {
            Debug.Log("Cleanup");
            // 释放前一帧的RenderTexture
            if (frontRT != null)
            {
                frontRT.Release();
                DestroyImmediate(frontRT);
                frontRT = null;
            }
            
            // 释放当前帧的RenderTexture
            if (backRT != null)
            {
                backRT.Release();
                DestroyImmediate(backRT);
                backRT = null;
            }
            
            // 释放材质
            if (preFrameMat != null)
            {
                DestroyImmediate(preFrameMat);
                preFrameMat = null;
            }
            
            // 重置标志
            isFirstFrame = true;
        }
    }

    GrassInteractionPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new GrassInteractionPass(this)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (GrassInteractions.instance == null)
            return;
        renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        // 调用基类的Dispose方法
        base.Dispose(disposing);

        Debug.Log("dispose");
        // 清理RenderTexture资源
        if (disposing && m_ScriptablePass != null)
        {
            m_ScriptablePass.Cleanup();
        }
    }
}