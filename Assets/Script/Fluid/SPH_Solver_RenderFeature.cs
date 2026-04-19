﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; 

public class SPH_Solver_RenderFeature : MonoBehaviour
{
    // ============ 服务定位器核心 ============
    private static readonly HashSet<SPH_Solver_RenderFeature> _actives = new HashSet<SPH_Solver_RenderFeature>();
    public static IReadOnlyCollection<SPH_Solver_RenderFeature> actives => _actives;

    [SerializeField]
    private Material _material;
    public Material material{
        get{
            return _material;
        }
    }

    public Terrain terrain;
    private RenderTexture _terrainHeightTexture;

    [Header("Velocity Field")]
    public int velocityFieldResolution = 64; // 建议64或128
    public RTHandle velocityFieldRTHandle; // 暴露给草系统
    public float CellSize { get { return radius * 2; } }
    public int Dimensions { get { return dimensions; } }
    public int Resolution { get { return velocityFieldResolution; } }
    public int SimulationSteps {get { return elapsedSimulationSteps; }} 

    [SerializeField]
    [Header("Particle properties")]
    public float radius = 2f;
    public Mesh particleMesh;
    Transform pointPrefab;
    public float particleRenderSize = 1f;
    public float mass = 4f;
    public float gasConstant = 2000f;
    public float restDensity = 9f;
    //粘性
    public float viscosityCoefficient = 2.5f;
    public float[] gravity = { 0.0f, -9.81f, 0.0f };
    //阻尼
    [SerializeField, Range(-0.99f, 0)]
    public float damping = -0.37f;
    public float dt = 0.01f;

    private float volume;
    private float radius2;
    private float radius3;
    private float radius4;
    private float radius5;
    private float mass2;
    public float stiffness = 1f;
    private int currParticleNum { get { return _particles.Length; } }

    private struct MeshProperties {
        public Matrix4x4 mat;
        public Vector4 color;

        public static int Size() {
            return
                sizeof(float) * 4 * 4 + // matrix;
                sizeof(float) * 4;      // color;
        }
    }

    [Header("Simulation space properties")]
    public int numberOfParticles = 2000;
    public float range;
    public int dimensions = 100;
    public int maximumParticlesPerCell = 500;

    [Header("Debug information")]
    [Tooltip("Tracks how many neighbours each particleIndex has in" + nameof(_neighbourList))]

    public int[] _neighbourTracker;
    [HideInInspector] 
    private Particle[] _particles;
    private MeshProperties[] _properties;

    private int[] _neighbourList;
    private uint[] _hashGrid;
    private uint[] _hashGridTracker;
    private float[] _densities;
    private float[] _pressures;
    private Vector3[] _velocities;
    private Vector3[] _forces;

    public GraphicsBuffer _particlesBuffer;
    public GraphicsBuffer _meshPropertiesBuffer;
    public GraphicsBuffer _argsBuffer;
    public GraphicsBuffer argsBuffer{
        get{
            return _argsBuffer;
        }
    }
    public GraphicsBuffer _neighbourListBuffer;
    public GraphicsBuffer _neighbourTrackerBuffer;
    public GraphicsBuffer _hashGridBuffer;
    public GraphicsBuffer _hashGridTrackerBuffer;
    public GraphicsBuffer _densitiesBuffer;
    public GraphicsBuffer _pressuresBuffer;
    public GraphicsBuffer _velocitiesBuffer;
    public GraphicsBuffer _forcesBuffer;

    // 托管的CommandBuffer
    private ComputeCommandBuffer _simulationCmd;
    public bool IsInitialized => _simulationCmd != null;

    public GameObject Collider;
    [Range(0f, 10f)]public float _colliderRadius;
    private Vector3 _lastColliderPos;
    private float _posMoveThreshold = 0.1f; // 移动0.1米才更新
    private float _lastColliderRadius = -1f;
    private int TerrainWidth, TerrainLength;

    public ComputeShader computeShaderSPH;
    private int clearHashGridKernel;
    private int recalculateHashGridKernel;
    private int buildNeighbourListKernel;
    private int computeDensityPressureKernel;
    private int computeForcesKernel;
    private int integrateKernel;
    private int DispatchKernel;
    private int createVelocityTexKernel;

    [Tooltip("The absolute accumulated simulation steps")]
    public int elapsedSimulationSteps;
    private object properties;
    public Mesh mesh;
    public Bounds bounds;

    private class ShaderProperties
    {
        public static readonly int SizeProperty = Shader.PropertyToID("_size");
        public static readonly int ParticlesBufferProperty = Shader.PropertyToID("_particlesBuffer");
        public static readonly int MeshBufferProperty = Shader.PropertyToID("_meshProperties");

    }

    [StructLayout(LayoutKind.Sequential, Size = 28)]
    private struct Particle
    {
        //public float mass;
        public Vector3 position;
       
        public Vector4 colorGradient;
      
    }
    private Mesh CreateQuad(float width = 1f, float height = 1f) {
        // Create a quad mesh.
        // See source for implementation.
        Mesh mesh = new Mesh();
        mesh.name = "Quad";
        // 定义四个顶点（中心在原点）
        Vector3[] vertices = new Vector3[4];
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        vertices[0] = new Vector3(-halfWidth, -halfHeight, 0); // 左下
        vertices[1] = new Vector3(halfWidth, -halfHeight, 0);  // 右下
        vertices[2] = new Vector3(-halfWidth, halfHeight, 0);  // 左上
        vertices[3] = new Vector3(halfWidth, halfHeight, 0);   // 右上

        mesh.vertices = vertices;

        Vector2[] uv = new Vector2[4];
        uv[0] = new Vector2(0, 0);
        uv[1] = new Vector2(1, 0);
        uv[2] = new Vector2(0, 1);
        uv[3] = new Vector2(1, 1);

        mesh.uv = uv;

        // 定义法线（全部朝向Z轴正方向）
        Vector3[] normals = new Vector3[4];
        normals[0] = Vector3.forward;
        normals[1] = Vector3.forward;
        normals[2] = Vector3.forward;
        normals[3] = Vector3.forward;
        mesh.normals = normals;

        // 定义两个三角形的顶点索引（顺时针方向）
        int[] triangles = new int[6];
        triangles[0] = 0;
        triangles[1] = 2;
        triangles[2] = 1;
        triangles[3] = 2;
        triangles[4] = 3;
        triangles[5] = 1;
        mesh.triangles = triangles;

         // 可选：计算切线（某些着色器需要）
        Vector4[] tangents = new Vector4[4];
        tangents[0] = new Vector4(1, 0, 0, -1);
        tangents[1] = new Vector4(1, 0, 0, -1);
        tangents[2] = new Vector4(1, 0, 0, -1);
        tangents[3] = new Vector4(1, 0, 0, -1);
        mesh.tangents = tangents;

        return mesh;
    }

    private void Setup() {
        Mesh mesh = CreateQuad();
        this.mesh = mesh;

        // Boundary surrounding the meshes we will be drawing.  Used for occlusion.
        bounds = new Bounds(transform.position, Vector3.one * (range + 1));
    }

    private void OnEnable()
    {
        _actives.Add(this);
        Debug.Log($"[SPH] 注册实例: {name}, 当前总数: {_actives.Count}");

        radius2 = radius * radius;
        radius3 = radius * radius2;
        radius4 = radius2 * radius2;
        radius5 = radius4 * radius;
        mass2 = mass * mass;
        
        Setup();
        RespawnParticles();
        FindKernels();
        InitTexture();
        InitComputeShader();
        InitGraphicsBuffers();
        //FindKernels();
    }

    #region Initilisation
    private void RespawnParticles()
    {
        _particles = new Particle[numberOfParticles];
        _densities = new float[numberOfParticles];
        _pressures = new float[numberOfParticles];
        _velocities = new Vector3[numberOfParticles];
        _forces = new Vector3[numberOfParticles];
        _properties = new MeshProperties[numberOfParticles];

        int particlesPerDimension = Mathf.CeilToInt(Mathf.Pow(numberOfParticles, 1f / 3f));
        volume = Mathf.Pow(2 * radius, 3);
        int counter = 0;
        while (counter < numberOfParticles/2)
        {
            for (int x = 0; x < particlesPerDimension; x++)
                for (int y = 0; y < particlesPerDimension; y++)
                    for (int z = 0; z < particlesPerDimension; z++)
                    {
                        float spacing = radius * 0.8f;
                        Vector3 startPos = new Vector3(dimensions - 1, dimensions - 1, dimensions - 1)
                                - new Vector3(spacing * x, spacing * y, spacing * z) 
                                - new Vector3(Random.Range(0, 0.01f), Random.Range(0f, 0.01f), Random.Range(0f, 0.01f));

                        _particles[counter] = new Particle
                        {
                            position = startPos,
                            colorGradient = Color.white,
                        };
                        _densities[counter] = -1f;
                        _pressures[counter] = 0.0f;
                        _forces[counter] = Vector3.zero;
                        _velocities[counter] = Vector3.zero;

                        MeshProperties props = new MeshProperties();
                        Quaternion rotation = Quaternion.Euler(Random.Range(-180, 180), Random.Range(-180, 180), Random.Range(-180, 180));
                        Vector3 scale = Vector3.one;
                        props.mat = Matrix4x4.TRS(Vector3.zero, rotation, scale);
                        // props.color = Color.Lerp(Color.red, Color.blue, Random.value);
                        // 计算归一化高度（0=底部，1=顶部）
                        float heightRatio = startPos.y / dimensions;

                        // 从底部的天蓝色渐变到顶部的靛青色
                        props.color = Color.Lerp(
                            new Color(0.53f, 0.81f, 1.0f),  // 天蓝色（底部）
                            new Color(0.3f, 0.0f, 0.5f),    // 靛青色（顶部）
                            Random.value
                        );
                        
                        _properties[counter] = props;

                        if (++counter == numberOfParticles)
                        {
                            return;
                        }
                    }
        }
    }

    private void FindKernels()
    {
        clearHashGridKernel = computeShaderSPH.FindKernel("ClearHashGrid");
        recalculateHashGridKernel = computeShaderSPH.FindKernel("RecalculateHashGrid");
        buildNeighbourListKernel = computeShaderSPH.FindKernel("BuildNeighbourList");
        computeDensityPressureKernel = computeShaderSPH.FindKernel("ComputeDensityPressure");
        computeForcesKernel = computeShaderSPH.FindKernel("ComputeForces");
        integrateKernel = computeShaderSPH.FindKernel("Integrate");
        // createVelocityTexKernel = computeShaderSPH.FindKernel("UpdateVelocityFieldWithHash");
    }

    // Start is called before the first frame update
    private void InitComputeShader()
    {
        computeShaderSPH.SetFloat("CellSize", radius * 2);
        computeShaderSPH.SetInt("Dimensions", dimensions);
        // ⭐ 新增：计算真实的网格维度
        int gridDim = Mathf.CeilToInt((float)dimensions / (radius * 2)) + 1;
        computeShaderSPH.SetInt("GridDim", gridDim); 
        computeShaderSPH.SetInt("maximumParticlesPerCell", maximumParticlesPerCell);
        computeShaderSPH.SetInt("_FieldResolution", velocityFieldResolution);
        computeShaderSPH.SetFloat("radius", radius);
        computeShaderSPH.SetFloat("radius2", radius2);
        computeShaderSPH.SetFloat("radius3", radius3);
        computeShaderSPH.SetFloat("radius4", radius4);
        computeShaderSPH.SetFloat("radius5", radius5);
        computeShaderSPH.SetFloat("mass", mass);
        computeShaderSPH.SetFloat("mass2", mass2);
        computeShaderSPH.SetFloat("gasConstant", gasConstant);
        computeShaderSPH.SetFloat("restDensity", restDensity);
        computeShaderSPH.SetFloat("viscosityCoefficient", viscosityCoefficient);
        computeShaderSPH.SetFloat("damping", damping);
        computeShaderSPH.SetFloat("dt", dt);
        computeShaderSPH.SetFloats("gravity", gravity);
        computeShaderSPH.SetFloats("epsilon", Mathf.Epsilon);
        computeShaderSPH.SetFloat("pi", Mathf.PI);
        computeShaderSPH.SetFloat("stiffness", stiffness);
        computeShaderSPH.SetFloat("_ColliderRadius", _colliderRadius);
        computeShaderSPH.SetVector("_ColliderPos", Collider.transform.position);
        if (terrain != null)
        {
            TerrainData terrainData = terrain.terrainData;
            computeShaderSPH.SetVector("_TerrainSize", new Vector4(terrainData.size.x, terrainData.size.y, terrainData.size.z, 0));
            computeShaderSPH.SetVector("_TerrainPos", terrain.transform.position);
        }
    }

    void InitGraphicsBuffers()
    {
        uint[] args = {
        mesh.GetIndexCount(0),
        (uint)numberOfParticles,
        mesh.GetIndexStart(0),
        mesh.GetBaseVertex(0),
        0
        };

        _meshPropertiesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles, MeshProperties.Size());
        _meshPropertiesBuffer.SetData(_properties);

        _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, args.Length * sizeof(uint));
        _argsBuffer.SetData(args);

        _particlesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles, sizeof(float)*(3+4));
        _particlesBuffer.SetData(_particles);


        _neighbourList = new int[numberOfParticles * maximumParticlesPerCell * 8];
        _neighbourTracker = new int[numberOfParticles];

        // ⭐⭐⭐ 核心修复：绝不能用 dimensions (100)，必须用 gridDim (26)！
        // 内存占用直接从 800MB 暴降到 14MB！
        int gridDim = Mathf.CeilToInt((float)dimensions / (radius * 2)) + 1;
        int totalCells = gridDim * gridDim * gridDim;

        _hashGrid = new uint[totalCells * maximumParticlesPerCell];
        _hashGridTracker = new uint[totalCells];

        _neighbourListBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles * maximumParticlesPerCell * 8, sizeof(int));
        _neighbourListBuffer.SetData(_neighbourList);
        _neighbourTrackerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles, sizeof(int));
        _neighbourTrackerBuffer.SetData(_neighbourTracker);

        _hashGridBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalCells * maximumParticlesPerCell, sizeof(uint));
        _hashGridBuffer.SetData(_hashGrid);
        _hashGridTrackerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalCells, sizeof(uint));
        //TODO CS.stride直接设置

        _densitiesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles, sizeof(float));
        _densitiesBuffer.SetData(_densities);
        _pressuresBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles, sizeof(float));
        _pressuresBuffer.SetData(_pressures);

        _velocitiesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles, sizeof(float) * 3);
        _velocitiesBuffer.SetData(_velocities);
        _forcesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numberOfParticles, sizeof(float) * 3);
        _forcesBuffer.SetData(_forces);
        //

        computeShaderSPH.SetBuffer(clearHashGridKernel, "_hashGridTracker", _hashGridTrackerBuffer);


        computeShaderSPH.SetBuffer(recalculateHashGridKernel, "_particles", _particlesBuffer);
        computeShaderSPH.SetBuffer(recalculateHashGridKernel, "_hashGrid", _hashGridBuffer);
        computeShaderSPH.SetBuffer(recalculateHashGridKernel, "_hashGridTracker", _hashGridTrackerBuffer);

        computeShaderSPH.SetBuffer(buildNeighbourListKernel, "_particles", _particlesBuffer);
        computeShaderSPH.SetBuffer(buildNeighbourListKernel, "_hashGrid", _hashGridBuffer);
        computeShaderSPH.SetBuffer(buildNeighbourListKernel, "_hashGridTracker",_hashGridTrackerBuffer);
        computeShaderSPH.SetBuffer(buildNeighbourListKernel, "_neighbourList", _neighbourListBuffer);
        computeShaderSPH.SetBuffer(buildNeighbourListKernel, "_neighbourTracker", _neighbourTrackerBuffer);


        computeShaderSPH.SetBuffer(computeDensityPressureKernel, "_neighbourTracker", _neighbourTrackerBuffer);
        computeShaderSPH.SetBuffer(computeDensityPressureKernel, "_neighbourList", _neighbourListBuffer);
        computeShaderSPH.SetBuffer(computeDensityPressureKernel, "_particles", _particlesBuffer);
        computeShaderSPH.SetBuffer(computeDensityPressureKernel, "_densities", _densitiesBuffer);
        computeShaderSPH.SetBuffer(computeDensityPressureKernel, "_pressures", _pressuresBuffer);


        computeShaderSPH.SetBuffer(computeForcesKernel,"_neighbourTracker", _neighbourTrackerBuffer);
        computeShaderSPH.SetBuffer(computeForcesKernel, "_neighbourList", _neighbourListBuffer);
        computeShaderSPH.SetBuffer(computeForcesKernel, "_particles", _particlesBuffer);
        computeShaderSPH.SetBuffer(computeForcesKernel, "_densities", _densitiesBuffer);
        computeShaderSPH.SetBuffer(computeForcesKernel, "_pressures", _pressuresBuffer);
        computeShaderSPH.SetBuffer(computeForcesKernel, "_velocities", _velocitiesBuffer);
        computeShaderSPH.SetBuffer(computeForcesKernel, "_forces", _forcesBuffer);

        computeShaderSPH.SetBuffer(integrateKernel, "_particles", _particlesBuffer);
        computeShaderSPH.SetBuffer(integrateKernel, "_forces", _forcesBuffer);
        computeShaderSPH.SetBuffer(integrateKernel, "_velocities", _velocitiesBuffer);
        computeShaderSPH.SetTexture(integrateKernel, "_TerrainHeightMap", _terrainHeightTexture);
        
        // computeShaderSPH.SetBuffer(createVelocityTexKernel, "_particles", _particlesBuffer);
        // computeShaderSPH.SetBuffer(createVelocityTexKernel, "_velocities", _velocitiesBuffer);
        // computeShaderSPH.SetBuffer(createVelocityTexKernel, "_hashGrid", _hashGridBuffer);
        // computeShaderSPH.SetBuffer(createVelocityTexKernel, "_hashGridTracker", _hashGridTrackerBuffer);

        // computeShaderSPH.SetTexture(createVelocityTexKernel, "_VelocityField", velocityFieldRTHandle.rt);
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

        // 创建3D纹理
        var desc = new RenderTextureDescriptor(
            velocityFieldResolution, 
            velocityFieldResolution, 
            RenderTextureFormat.ARGBFloat, 
            0) // depth buffer bits
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = velocityFieldResolution,
            enableRandomWrite = true
        };

        velocityFieldRTHandle = RTHandles.Alloc(desc, name: "_VelocityField");
    }

    void UpdateParamsIfDirty(ComputeCommandBuffer cmd = null)
    {
        Vector3 colliderPos = Collider.transform.position;  

        if (Vector3.Distance(colliderPos, _lastColliderPos) > _posMoveThreshold)
        {
            if (cmd != null)
                cmd.SetComputeVectorParam(computeShaderSPH, "_ColliderPos", colliderPos);
            else
                computeShaderSPH.SetVector("_ColliderPos", colliderPos);
            _lastColliderPos = colliderPos;
        }

        if (Mathf.Abs(_colliderRadius - _lastColliderRadius) > 0.001f)
        {
            if (cmd != null)
                cmd.SetComputeFloatParam(computeShaderSPH, "_ColliderRadius", _colliderRadius);
            else
                computeShaderSPH.SetFloat("_ColliderRadius", _colliderRadius);
            _lastColliderRadius = _colliderRadius;
        }
    }

    #endregion
    // Update is called once per frame
    void Update()
    {
        if (FrameDebugPauseHelper.IsPaused) return; // ✅ 冻结粒子模拟
        // Debug.Log("更update新！");
        UpdateParamsIfDirty(_simulationCmd);
    }

    int lastSimulatedFrame = -1;
    // 由RenderPass调用执行预录制的命令
    public void ExecuteSimulation(ComputeCommandBuffer cmd)
    {
        if (FrameDebugPauseHelper.IsPaused) return; // ✅ 冻结粒子模拟
        if (Time.frameCount == lastSimulatedFrame) return; 
        lastSimulatedFrame = Time.frameCount;

        UpdateParamsIfDirty(cmd);
        // 直接录制命令，不依赖预录制的Buffer
        // ⭐ 修复1：使用真实的网格大小代替 dimensions，防止显存越界！
        int gridDim = Mathf.CeilToInt((float)dimensions / (radius * 2)) + 1;
        int totalCells = gridDim * gridDim * gridDim;
        int clearDispatch = Mathf.CeilToInt((float)totalCells / 100f);
        cmd.DispatchCompute(computeShaderSPH, clearHashGridKernel, clearDispatch, 1, 1);

        // ⭐ 修复2：为了安全，其余的也改成动态计算 Group 数量
        int particleDispatch = Mathf.CeilToInt((float)numberOfParticles / 100f);
        cmd.DispatchCompute(computeShaderSPH, recalculateHashGridKernel, particleDispatch, 1, 1);
        cmd.DispatchCompute(computeShaderSPH, buildNeighbourListKernel, particleDispatch, 1, 1);
        cmd.DispatchCompute(computeShaderSPH, computeDensityPressureKernel, particleDispatch, 1, 1);
        cmd.DispatchCompute(computeShaderSPH, computeForcesKernel, particleDispatch, 1, 1);
        cmd.DispatchCompute(computeShaderSPH, integrateKernel, particleDispatch, 1, 1);
        
        elapsedSimulationSteps++; 
    }

    private void OnDisable()
    {
        _actives.Remove(this);
        Debug.Log($"[SPH] 注销实例: {name}, 剩余总数: {_actives.Count}");
        // 延迟1帧释放Buffer（确保RenderPass已完成最后一次绘制）
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    { 
        // 1. 安全释放 RenderTexture 和 Mesh
        if (_terrainHeightTexture != null) {
            _terrainHeightTexture.Release();
            DestroyImmediate(_terrainHeightTexture);
            _terrainHeightTexture = null;
        }
        if (mesh != null) {
            DestroyImmediate(mesh);
            mesh = null;
        }
        if (velocityFieldRTHandle != null) {
            velocityFieldRTHandle.Release();
            velocityFieldRTHandle = null;
        }

        // 2. 绝对安全的 Buffer 释放法
        SafeDispose(ref _particlesBuffer);
        SafeDispose(ref _argsBuffer);
        SafeDispose(ref _meshPropertiesBuffer);
        SafeDispose(ref _neighbourListBuffer);
        SafeDispose(ref _neighbourTrackerBuffer);
        SafeDispose(ref _hashGridBuffer);
        SafeDispose(ref _hashGridTrackerBuffer);
        SafeDispose(ref _densitiesBuffer);
        SafeDispose(ref _pressuresBuffer);
        SafeDispose(ref _velocitiesBuffer);
        SafeDispose(ref _forcesBuffer);

        // ⭐ 3. 极其重要：把巨型 C# 数组置空，强制 C# 垃圾回收器 (GC) 清理内存！
        _particles = null;
        _neighbourList = null;
        _neighbourTracker = null;
        _hashGrid = null;
        _hashGridTracker = null;
        _densities = null;
        _pressures = null;
        _velocities = null;
        _forces = null;
        _properties = null;
    }

    private void SafeDispose(ref GraphicsBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Dispose();
            buffer = null;  // 置空防止重复释放
        }
    }
    
}