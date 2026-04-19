using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem; 
using UnityEngine.Rendering; 

public partial class GrassRendererRT_Terrain : MonoBehaviour
{
    public Terrain terrain;
    
    public RTHandle velocityFieldRTHandle { get; set; }
    public float sphCellSize { get; set; }
    public int sphDimensions { get; set; }
    public int sphResolution { get; set; }

    public Texture2D _windNoiseTex;
    public int _GridWidth, _GridLength;
    public float _GridSpacing;
    private int TerrainWidth, TerrainLength;
    private RenderTexture _initGrassTexture, _editGrassTexture;
    private int _kernelInit;
    [SerializeField] private ComputeShader initCS, linesGenCS, grassGenCS;
    [SerializeField] private ComputeShader gravityCS, globalCS, lengthCS, localCS, windCS, toTrianglesCS;
    private ComputeBuffer _linesBuffer, _drawArgsBuffer, _ParamsBuffer;
    private int _kernelGen, _kernelGravity, _kernelGlobal, _kernelLength, _kernelLocal, _kernelWind, _kernelToTriangles;
    [SerializeField] private Material grassMat;
    [SerializeField] private Camera mainCam;   
    private Vector3 lastCamPos;
    private Quaternion lastCamRot;
    private MaterialPropertyBlock _mpb;

    private static readonly int _WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    private static readonly int _ViewProj = Shader.PropertyToID("_ViewProj");
    private static readonly int _ColliderPos = Shader.PropertyToID("_ColliderPos");
    private static readonly int _ColliderRadius = Shader.PropertyToID("_ColliderRadius");
    private static readonly int _CullRadius = Shader.PropertyToID("_CullRadius");

    [System.Serializable]
    public struct GrassParams
    {
        [Header("草参数")]
        [Range(0.10f, 4f)] public float _HEIGHT;
        [Range(0.01f, 0.2f)]public float _WIDTH;
        [Range(0.1f, 10f)]public float _WindSize;
        [Range(0.0f, 10f)]public float _WindSpeed;
        [Range(0f, 0.02f)]public float _Stiffness;
        [Range(0f, 1f)]public float _LocalConstraintStiffness;
        [Range(1, 10)]public int _Segment;
        [Range(0f, 10f)] public float _WindBase;
        [Range(-1f, 1f)] public float _WindDirectionX;
        [Range(-1f, 1f)] public float _WindDirectionZ;
    }
    [SerializeField] private GrassParams grassParams;
    private GrassParams _lastParams; 
    public GameObject Collider;
    private bool _isBound = false; 
    private Matrix4x4 _lastViewProj;
    private Vector3 _lastCamPos;
    private Vector3 _lastColliderPos;
    private float _posMoveThreshold = 0.1f; 
    [Range(0f, 10f)]public float _colliderRadius;
    [Range(0f, 2f)]public float _cullRadius;
    private float _lastCullRadius = -1f; // 初始为不可能值
    private float _lastColliderRadius = -1f;
    private RenderTexture _terrainHeightTexture;

    // 在类成员中添加
    private Texture2D _splatMapTexture;
    private Vector3 _terrainPos;
    private static readonly int _SplatMap = Shader.PropertyToID("_SplatMap");
    private static readonly int _TerrainPos = Shader.PropertyToID("_TerrainPos");
    private static readonly int _TerrainSize = Shader.PropertyToID("_TerrainSize");

    private void Start()
    {
    }

    // 创建Splatmap纹理
    private void CreateSplatMapTexture(TerrainData terrainData)
    {
        // 获取splatmap数据 [height, width, layers]
        float[,,] splatmapData = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
        
        // 创建单通道纹理存储草地层的权重
        _splatMapTexture = new Texture2D(terrainData.alphamapWidth, terrainData.alphamapHeight, 
            TextureFormat.RGBAFloat, false, true);
        
        for (int y = 0; y < terrainData.alphamapHeight; y++)
        {
            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {
                // 获取指定层的权重（0-1）
                float grassWeight0 = splatmapData[y, x, 0];
                float grassWeight1 = splatmapData[y, x, 1];
                float grassWeight2 = splatmapData[y, x, 2];
                _splatMapTexture.SetPixel(x, y, new Color(grassWeight0, grassWeight1, grassWeight2, 0)); 
            }
        }
        _splatMapTexture.Apply();
        
        // 设置线性过滤，避免硬边
        _splatMapTexture.filterMode = FilterMode.Bilinear;
        _splatMapTexture.wrapMode = TextureWrapMode.Clamp;
    }


    private void FindKernels()
    {
        _kernelInit = initCS.FindKernel("CSInit");
        _kernelGen = linesGenCS.FindKernel("CSLinesGen");
        _kernelGravity = gravityCS.FindKernel("CSGravity");
        _kernelGlobal = globalCS.FindKernel("CSGlobal");
        _kernelLength = lengthCS.FindKernel("CSLength");
        _kernelLocal = localCS.FindKernel("CSLocal");
        _kernelWind = windCS.FindKernel("CSWind");
        _kernelToTriangles = toTrianglesCS.FindKernel("CSToTriangles");
    }

    private void InitComputeShader()
    {
        if (terrain == null) return;

        TerrainData terrainData = terrain.terrainData;
        _terrainPos = terrain.GetPosition(); // 地形世界坐标
        // 设置Terrain相关参数
        initCS.SetVector(_TerrainPos, _terrainPos);
        initCS.SetVector(_TerrainSize, new Vector4(terrainData.size.x, terrainData.size.y, terrainData.size.z, 0));
        
        // 传入Splatmap
        if (_splatMapTexture != null)
            initCS.SetTexture(_kernelInit, _SplatMap, _splatMapTexture);

        initCS.SetFloat("_GridSpacing", _GridSpacing);
        initCS.SetInt("_GridWidth", _GridWidth);
        initCS.SetInt("_GridLength", _GridLength);
        initCS.SetVector(_WorldSpaceCameraPos, Camera.main.transform.position);

        linesGenCS.SetVector(_WorldSpaceCameraPos, Camera.main.transform.position);
        linesGenCS.SetMatrix(_ViewProj, Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix);
        linesGenCS.SetVector(_WorldSpaceCameraPos, Camera.main.transform.position);

        // 补上这极其关键的几句！把网格宽度告诉所有的物理内核！
        gravityCS.SetInt("_GridWidth", _GridWidth);
        globalCS.SetInt("_GridWidth", _GridWidth);
        lengthCS.SetInt("_GridWidth", _GridWidth);
        localCS.SetInt("_GridWidth", _GridWidth);
        
        windCS.SetInt("_GridWidth", _GridWidth);
        windCS.SetFloat("_Time", Time.time * 5f);
        windCS.SetFloat("_SphCellSize", sphCellSize);
        windCS.SetInt("_SphDimensions", sphDimensions);
        windCS.SetInt("_SphResolution", sphResolution);
    }

    private void CreateTerrainHeightTexture(TerrainData terrainData)
    {
        int resolution = terrainData.heightmapResolution;
        float[,] heights = terrainData.GetHeights(0, 0, resolution, resolution);

        // 【关键】获取实际高度缩放
        float heightScale = terrainData.size.y;  // = 256 (你截图中的 Terrain Height)
        
        // 创建RFloat格式纹理存储高度
        _terrainHeightTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
        { enableRandomWrite = true, filterMode = FilterMode.Bilinear };
        
        // 复制高度数据到纹理
        Texture2D heightTex = new Texture2D(resolution, resolution, TextureFormat.RFloat, false);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // 【关键】乘以 heightScale 转换为实际高度
                float realHeight = heights[y, x] * heightScale;
                heightTex.SetPixel(x, y, new Color(realHeight, 0, 0, 0));
            }
        }
        heightTex.Apply();
        
        Graphics.Blit(heightTex, _terrainHeightTexture);
        Destroy(heightTex);
    }

    void InitTexture()
    {
        if (terrain != null)
        {
            TerrainData terrainData = terrain.terrainData;
            float terrainWidth = terrainData.size.x;
            float terrainLength = terrainData.size.z;
            // 128 * 128 
            TerrainWidth = Mathf.CeilToInt(terrainWidth);
            TerrainLength = Mathf.CeilToInt(terrainLength);
            
            CreateTerrainHeightTexture(terrainData);
            CreateSplatMapTexture(terrainData);
        }

        _initGrassTexture = new RenderTexture(_GridWidth, _GridLength, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        { enableRandomWrite = true };
        _editGrassTexture = new RenderTexture(_GridWidth, _GridLength, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        { enableRandomWrite = true };
        initCS.SetTexture(_kernelInit, "_InitGrass", _initGrassTexture);
        initCS.SetTexture(_kernelInit, "_TerrainHeightMap", _terrainHeightTexture);
        linesGenCS.SetTexture(_kernelGen, "_EditGrass", _editGrassTexture);
        windCS.SetTexture(_kernelWind, "_WindNoiseTex", _windNoiseTex);
    }

    void InitComputeBuffers()
    {
        _lastParams = grassParams;

        int VERTEX_DATA_SIZE_Line = (3 + 3 + 3 + 3 + 1 + 1 + 2 + 4) * sizeof(float) + sizeof(int);
        int LINES_DATA_SIZE = grassParams._Segment * VERTEX_DATA_SIZE_Line + (1 + 1) * sizeof(int);

        int maxCount = _GridWidth * _GridLength;
        uint vertexCount = (uint)((grassParams._Segment - 1) * 6);
        uint instanceCount = (uint)(_GridWidth * _GridLength);
        uint firstVertex = 0;
	    uint baseInstance = 0;
        uint[] drawArgs = new uint[5] { vertexCount, instanceCount, firstVertex, baseInstance, 0};
        _linesBuffer = new ComputeBuffer(maxCount, LINES_DATA_SIZE, ComputeBufferType.IndirectArguments);
        _drawArgsBuffer = new ComputeBuffer(1, drawArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer.SetData(drawArgs);

        _ParamsBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(GrassParams)), ComputeBufferType.IndirectArguments);
        _ParamsBuffer.SetData(new GrassParams[] { grassParams });

        _mpb = new MaterialPropertyBlock();

        linesGenCS.SetBuffer(_kernelGen, "_Lines", _linesBuffer);
        linesGenCS.SetBuffer(_kernelGen, "_GrassParams", _ParamsBuffer);
        
        gravityCS.SetBuffer(_kernelGravity, "_Lines", _linesBuffer);
        gravityCS.SetBuffer(_kernelGravity, "_GrassParams", _ParamsBuffer);
        
        globalCS.SetBuffer(_kernelGlobal, "_Lines", _linesBuffer);
        globalCS.SetBuffer(_kernelGlobal, "_GrassParams", _ParamsBuffer);
       
        lengthCS.SetBuffer(_kernelLength, "_Lines", _linesBuffer);
        lengthCS.SetBuffer(_kernelLength, "_GrassParams", _ParamsBuffer);
       
        localCS.SetBuffer(_kernelLocal, "_Lines", _linesBuffer);
        localCS.SetBuffer(_kernelLocal, "_GrassParams", _ParamsBuffer);
        
        windCS.SetBuffer(_kernelWind, "_Lines", _linesBuffer);
        windCS.SetBuffer(_kernelWind, "_GrassParams", _ParamsBuffer);
        
        toTrianglesCS.SetBuffer(_kernelToTriangles, "_Lines", _linesBuffer);
        toTrianglesCS.SetBuffer(_kernelToTriangles, "_IndirectArgsBuffer", _drawArgsBuffer);
        toTrianglesCS.SetBuffer(_kernelToTriangles, "_GrassParams", _ParamsBuffer);
    }

    private void Awake()
    {
        FindKernels();
        InitTexture();
        InitComputeShader();
        InitComputeBuffers();
    }

    private void OnEnable()
    {
        int threadGroupsX = Mathf.CeilToInt(_GridWidth / 16.0f);
        int threadGroupsY = Mathf.CeilToInt(_GridLength / 16.0f);

        initCS.Dispatch(_kernelInit, threadGroupsX, threadGroupsY, 1);
        Graphics.CopyTexture(_initGrassTexture, _editGrassTexture);
        linesGenCS.Dispatch(_kernelGen, threadGroupsX, threadGroupsY, 1);
        // toTrianglesCS.Dispatch(_kernelToTriangles, threadGroupsX, threadGroupsY, 1);
    }
        
    private void OnValidate()
    {
        // 仅在编辑器模式且Application已运行时更新
        if (!Application.isPlaying || _ParamsBuffer == null) return;
        
        // 只检查参数变化，不调用完整的UpdateParamsIfDirty()
        if (!grassParams.Equals(_lastParams))
        {
            _ParamsBuffer.SetData(new GrassParams[] { grassParams});
            _lastParams = grassParams;
            Debug.Log("[Grass] Inspector参数已同步到GPU");
        }
    }

    void UpdateParamsIfDirty()
    {
        // 检查参数是否变化（使用脏标记）
        if (!grassParams.Equals(_lastParams))
        {
            _ParamsBuffer.SetData(new GrassParams[] { grassParams});
            _lastParams = grassParams;
            Debug.Log("[Grass] ✅ 参数已同步到GPU");
        }

        Matrix4x4 viewProj = Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix;
        Vector3 camPos = Camera.main.transform.position;

        if (viewProj != _lastViewProj)
        {
            linesGenCS.SetMatrix(_ViewProj, viewProj);
            _lastViewProj = viewProj;
        }

        if (camPos != _lastCamPos)
        {
            initCS.SetVector(_WorldSpaceCameraPos, camPos);
            linesGenCS.SetVector(_WorldSpaceCameraPos, camPos);
            _lastCamPos = camPos;
        }

        Vector3 colliderPos = Collider.transform.position;  

        if (Vector3.Distance(colliderPos, _lastColliderPos) > _posMoveThreshold)
        {
            gravityCS.SetVector(_ColliderPos, colliderPos);
            linesGenCS.SetVector(_ColliderPos, colliderPos);
            _lastColliderPos = colliderPos;
        }

        if (Mathf.Abs(_cullRadius - _lastCullRadius) > 0.001f)
        {
            linesGenCS.SetFloat(_CullRadius, _cullRadius);
            _lastCullRadius = _cullRadius;
        }

        if (Mathf.Abs(_colliderRadius - _lastColliderRadius) > 0.001f)
        {
            gravityCS.SetFloat(_ColliderRadius, _colliderRadius);
            _lastColliderRadius = _colliderRadius;
        }
    }

    private void Update()
    {
        int threadGroupsX = Mathf.CeilToInt(_GridWidth / 16.0f);
        int threadGroupsY = Mathf.CeilToInt(_GridLength / 16.0f);

        if (FrameDebugPauseHelper.IsPaused) return; // ✅ 冻结粒子模拟
        UpdateParamsIfDirty();

        // ✅ 首次绑定（只执行一次）
        if (!_isBound)
        {
            // windCS.SetTexture(_kernelWind, "_VelocityField", velocityFieldRTHandle.rt);
            windCS.SetFloat("_SphCellSize", sphCellSize);
            windCS.SetInt("_SphDimensions", sphDimensions);
            windCS.SetInt("_SphResolution", sphResolution);
            _isBound = true;
        }

        // 2. 重新执行 Init，让网格跟着摄像机走
        initCS.Dispatch(_kernelInit, threadGroupsX, threadGroupsY, 1);

        // 3. 既然重新生成了底图，记得把数据 Copy 给后续的内核用
        Graphics.CopyTexture(_initGrassTexture, _editGrassTexture);

        linesGenCS.Dispatch(_kernelGen, threadGroupsX, threadGroupsY, 1);
        gravityCS.Dispatch(_kernelGravity, threadGroupsX, threadGroupsY, 1); 
        globalCS.Dispatch(_kernelGlobal, threadGroupsX, threadGroupsY, 1);
        localCS.Dispatch(_kernelLocal, threadGroupsX, threadGroupsY, 1);
        lengthCS.Dispatch(_kernelLength, threadGroupsX, threadGroupsY, 1);
        lengthCS.Dispatch(_kernelLength, threadGroupsX, threadGroupsY, 1);
        lengthCS.Dispatch(_kernelLength, threadGroupsX, threadGroupsY, 1);
        if (Time.frameCount % 3 == 0)
        {
            // windCS.Dispatch(_kernelWind, threadGroupsX, threadGroupsY, 1);
        }
        // toTrianglesCS.Dispatch(_kernelToTriangles, threadGroupsX, threadGroupsY, 1);
        _mpb.Clear();
        // Graphics.DrawProceduralIndirect(grassMat, new Bounds(Vector3.zero, Vector3.one * 1000f), MeshTopology.Triangles,
        //              _drawArgsBuffer, 0, null, _mpb, UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
        _mpb.SetBuffer("_Lines", _linesBuffer);
        _mpb.SetInt("_MaxSegment", grassParams._Segment);
        _mpb.SetFloat("_Width", grassParams._WIDTH); 
    
        // 如果图集行列也是动态调整的，也一并设置
        _mpb.SetFloat("_AtlasColumns", 8);
        _mpb.SetFloat("_AtlasRows", 1);
        Graphics.DrawProceduralIndirect(grassMat, new Bounds(Vector3.zero, Vector3.one * 1000f), MeshTopology.Triangles,
                _drawArgsBuffer, 0, null, _mpb, UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
    }

    private void OnDisable()
    {
        // 销毁渲染纹理
        Destroy(_initGrassTexture);
        Destroy(_editGrassTexture);
        Destroy(_terrainHeightTexture); 
        Destroy(_splatMapTexture);

        // 释放缓冲区：
        _linesBuffer?.Release(); _linesBuffer = null;
        _drawArgsBuffer?.Release(); _drawArgsBuffer = null;
        _ParamsBuffer?.Release(); _ParamsBuffer = null;
    }
}
