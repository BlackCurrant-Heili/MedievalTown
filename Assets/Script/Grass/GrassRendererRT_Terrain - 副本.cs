using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem; 
using UnityEngine.Rendering; 

public partial class GrassRendererRT_TerrainXXX : MonoBehaviour
{
    public Terrain terrain;
    
    public RTHandle velocityFieldRTHandle { get; set; }
    public float sphCellSize { get; set; }
    public int sphDimensions { get; set; }
    public int sphResolution { get; set; }

    // --- 新增的参数 ---
    [Header("地形草地生成设置")]
    public string targetLayerName = "Dirt";     // 你的Dirt图层名字
    [Range(0.1f, 100f)] 
    public float grassDensityPerMeter = 5f;     // 每平方米/单位的草密度[Range(0f, 1f)] 
    public float layerWeightThreshold = 0.5f;   // 权重大于此值才生成草
    
    public Texture2D _windNoiseTex;
    [HideInInspector] public int Width, Length;
    private int TerrainWidth, TerrainLength;
    private RenderTexture _initGrassTexture, _editGrassTexture;
    private int _kernelInit;
    [SerializeField] private ComputeShader initCS, linesGenCS, grassGenCS;
    [SerializeField] private ComputeShader gravityCS, globalCS, lengthCS, localCS, windCS, toTrianglesCS;
    private ComputeBuffer _trianglesBuffer, _linesBuffer, _linesCopyBuffer, _linesOriBuffer, _drawArgsBuffer, _ParamsBuffer;
    private int _kernelGen, _kernelGravity, _kernelGlobal, _kernelLength, _kernelLocal, _kernelWind, _kernelToTriangles;
    [SerializeField] private int maxBufferCount;
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
        [Range(5, 10)]public int _Segment;
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

    private void Start()
    {
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
        if (terrain != null)
        {
            TerrainData terrainData = terrain.terrainData;
            initCS.SetVector("_TerrainSize", new Vector4(terrainData.size.x, terrainData.size.y, terrainData.size.z, 0));
            initCS.SetVector("_TerrainPos", terrain.transform.position);
        }

        linesGenCS.SetVector(_WorldSpaceCameraPos, Camera.main.transform.position);
        linesGenCS.SetMatrix(_ViewProj, Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix);
        linesGenCS.SetVector(_WorldSpaceCameraPos, Camera.main.transform.position);

        windCS.SetFloat("_Time", Time.time * 5f);
        windCS.SetFloat("_SphCellSize", sphCellSize);
        windCS.SetInt("_SphDimensions", sphDimensions);
        windCS.SetInt("_SphResolution", sphResolution);
    }

    private void CreateTerrainHeightTexture(TerrainData terrainData)
    {
        int resolution = terrainData.heightmapResolution;
        float[,] heights = terrainData.GetHeights(0, 0, resolution, resolution);
        
        // 创建RFloat格式纹理存储高度
        _terrainHeightTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
        { enableRandomWrite = true, filterMode = FilterMode.Bilinear };
        
        // 复制高度数据到纹理
        Texture2D heightTex = new Texture2D(resolution, resolution, TextureFormat.RFloat, false);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heightTex.SetPixel(x, y, new Color(heights[y, x], 0, 0, 0));
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
        }

        _initGrassTexture = new RenderTexture(Width, Length, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        { enableRandomWrite = true };
        _editGrassTexture = new RenderTexture(Width, Length, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        { enableRandomWrite = true };
        initCS.SetTexture(_kernelInit, "_InitGrass", _initGrassTexture);
        initCS.SetTexture(_kernelInit, "_TerrainHeightMap", _terrainHeightTexture);
        linesGenCS.SetTexture(_kernelGen, "_EditGrass", _editGrassTexture);
        windCS.SetTexture(_kernelWind, "_WindNoiseTex", _windNoiseTex);
    }

    void InitComputeBuffers()
    {
        _lastParams = grassParams;

        int VERTEX_DATA_SIZE_Line = (1 + 1 + 3 + 3) * sizeof(float) + sizeof(int);
        int LINES_DATA_SIZE = grassParams._Segment * VERTEX_DATA_SIZE_Line + (1 + 1) * sizeof(int);

        int VERTEX_DATA_SIZE_Triangle = (1 + 3 + 3) * sizeof(float);
        int TRIANGLES_DATA_SIZE = 3 * VERTEX_DATA_SIZE_Triangle;

        uint[] drawArgs = new uint[5] { 0, 1, 0, 0, 0 };
        _trianglesBuffer = new ComputeBuffer(maxBufferCount, TRIANGLES_DATA_SIZE, ComputeBufferType.Append);
        _linesBuffer = new ComputeBuffer(maxBufferCount, LINES_DATA_SIZE, ComputeBufferType.IndirectArguments);
        _linesCopyBuffer = new ComputeBuffer(maxBufferCount, LINES_DATA_SIZE, ComputeBufferType.IndirectArguments);
        _linesOriBuffer = new ComputeBuffer(maxBufferCount, LINES_DATA_SIZE, ComputeBufferType.IndirectArguments);
        // _LinesCount = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer = new ComputeBuffer(1, drawArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer.SetData(drawArgs);

        _ParamsBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(GrassParams)), ComputeBufferType.IndirectArguments);
        _ParamsBuffer.SetData(new GrassParams[] { grassParams });

        _mpb = new MaterialPropertyBlock();

        linesGenCS.SetBuffer(_kernelGen, "_Triangles", _trianglesBuffer);
        linesGenCS.SetBuffer(_kernelGen, "_Lines", _linesBuffer);
        linesGenCS.SetBuffer(_kernelGen, "_LinesCopy", _linesCopyBuffer);
        linesGenCS.SetBuffer(_kernelGen, "_LinesOri", _linesOriBuffer);
        linesGenCS.SetBuffer(_kernelGen, "_GrassParams", _ParamsBuffer);
        
        gravityCS.SetBuffer(_kernelGravity, "_Lines", _linesBuffer);
        gravityCS.SetBuffer(_kernelGravity, "_LinesCopy", _linesCopyBuffer);
        gravityCS.SetBuffer(_kernelGravity, "_LinesOri", _linesOriBuffer);
        gravityCS.SetBuffer(_kernelGravity, "_GrassParams", _ParamsBuffer);
        
        globalCS.SetBuffer(_kernelGlobal, "_Lines", _linesBuffer);
        globalCS.SetBuffer(_kernelGlobal, "_LinesCopy", _linesCopyBuffer);
        globalCS.SetBuffer(_kernelGlobal, "_LinesOri", _linesOriBuffer);
        globalCS.SetBuffer(_kernelGlobal, "_GrassParams", _ParamsBuffer);
       
        lengthCS.SetBuffer(_kernelLength, "_Lines", _linesBuffer);
        lengthCS.SetBuffer(_kernelLength, "_GrassParams", _ParamsBuffer);
       
        localCS.SetBuffer(_kernelLocal, "_Lines", _linesBuffer);
        localCS.SetBuffer(_kernelLocal, "_LinesCopy", _linesCopyBuffer);
        localCS.SetBuffer(_kernelLocal, "_LinesOri", _linesOriBuffer);
        localCS.SetBuffer(_kernelLocal, "_GrassParams", _ParamsBuffer);
        
        windCS.SetBuffer(_kernelWind, "_Lines", _linesBuffer);
        windCS.SetBuffer(_kernelWind, "_GrassParams", _ParamsBuffer);
        
        toTrianglesCS.SetBuffer(_kernelToTriangles, "_Lines", _linesBuffer);
        toTrianglesCS.SetBuffer(_kernelToTriangles, "_Triangles", _trianglesBuffer);
        toTrianglesCS.SetBuffer(_kernelToTriangles, "_IndirectArgsBuffer", _drawArgsBuffer);
        toTrianglesCS.SetBuffer(_kernelToTriangles, "_GrassParams", _ParamsBuffer);
    }

    private void Awake()
    {
        FindKernels();
        InitComputeShader();
        InitTexture();
        InitComputeBuffers();
    }

    private void OnEnable()
    {
        initCS.Dispatch(_kernelInit, Width / 16, Length / 16, 1);
        Graphics.CopyTexture(_initGrassTexture, _editGrassTexture);
        linesGenCS.Dispatch(_kernelGen, Width / 16, Length / 16, 1);
        toTrianglesCS.Dispatch(_kernelToTriangles, Width / 16, Length / 16, 1);
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
        if (FrameDebugPauseHelper.IsPaused) return; // ✅ 冻结粒子模拟
        UpdateParamsIfDirty();

        // ✅ 防御性检查：如果数据未就绪，跳过整个模拟
        // if (velocityFieldRTHandle == null || sphCellSize <= 0 || sphDimensions <= 0 || sphResolution <= 0)
        // {
        //     // 只在第一帧警告
        //     if (Time.frameCount == 1)
        //         return; // 不执行任何Dispatch
        // }

        // ✅ 首次绑定（只执行一次）
        if (!_isBound)
        {
            // windCS.SetTexture(_kernelWind, "_VelocityField", velocityFieldRTHandle.rt);
            windCS.SetFloat("_SphCellSize", sphCellSize);
            windCS.SetInt("_SphDimensions", sphDimensions);
            windCS.SetInt("_SphResolution", sphResolution);
            _isBound = true;
        }

        _trianglesBuffer.SetCounterValue(0);

        linesGenCS.Dispatch(_kernelGen, Width / 16, Length / 16, 1);
        gravityCS.Dispatch(_kernelGravity, Width / 16, Length / 16, 1); 
        globalCS.Dispatch(_kernelGlobal, Width / 16, Length / 16, 1);
        localCS.Dispatch(_kernelLocal, Width / 16, Length / 16, 1);
        lengthCS.Dispatch(_kernelLength, Width / 16, Length / 16, 1);
        lengthCS.Dispatch(_kernelLength, Width / 16, Length / 16, 1);
        lengthCS.Dispatch(_kernelLength, Width / 16, Length / 16, 1);
        if (Time.frameCount % 3 == 0)
        {
            windCS.Dispatch(_kernelWind, Width / 16, Length / 16, 1);
        }
        toTrianglesCS.Dispatch(_kernelToTriangles, Width / 16, Length / 16, 1);
        _mpb.Clear();
        _mpb.SetBuffer("_Triangles", _trianglesBuffer);
        Graphics.DrawProceduralIndirect(grassMat, new Bounds(Vector3.zero, Vector3.one * 1000f), MeshTopology.Triangles,
                     _drawArgsBuffer, 0, null, _mpb, UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
    }

    private void OnDisable()
    {
        // 销毁渲染纹理
        Destroy(_initGrassTexture);
        Destroy(_editGrassTexture);
        Destroy(_terrainHeightTexture); 

        // 释放缓冲区：
        _linesBuffer?.Release(); _linesBuffer = null;
        _linesCopyBuffer?.Release(); _linesCopyBuffer = null;
        _linesOriBuffer?.Release(); _linesOriBuffer = null;
        _trianglesBuffer?.Release(); _trianglesBuffer = null;
        _drawArgsBuffer?.Release(); _drawArgsBuffer = null;
        _ParamsBuffer?.Release(); _ParamsBuffer = null;
    }
}
