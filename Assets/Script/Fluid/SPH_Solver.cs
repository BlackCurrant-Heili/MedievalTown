﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; 

public class SPH_Solver : MonoBehaviour
{
    public Terrain terrain;
    private RenderTexture _terrainHeightTexture;

    [Header("Velocity Field")]
    public int velocityFieldResolution = 64; // 建议64或128
    public RenderTexture velocityFieldTexture { get; private set; } // 暴露给草系统
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
    public Material material;
    public float mass = 4f;
    public float gasConstant = 2000f;
    public float restDensity = 9f;
    //粘性
    public float viscosityCoefficient = 2.5f;
    public float[] gravity = { 0.0f, -9.81f* 2000f, 0.0f };
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
    public int maximumParticlesPerCell = 100;

    [Header("Debug information")]
    [Tooltip("Tracks how many neighbours each particleIndex has in" + nameof(_neighbourList))]

    public int[] _neighbourTracker;
    private Particle[] _particles;
    private MeshProperties[] _properties;

    private int[] _neighbourList;
    private uint[] _hashGrid;
    private uint[] _hashGridTracker;
    private float[] _densities;
    private float[] _pressures;
    private Vector3[] _velocities;
    private Vector3[] _forces;

    private ComputeBuffer _particlesBuffer;
    private ComputeBuffer _meshPropertiesBuffer;
    private ComputeBuffer _argsBuffer;
    private ComputeBuffer _neighbourListBuffer;
    private ComputeBuffer _neighbourTrackerBuffer;
    private ComputeBuffer _hashGridBuffer;
    private ComputeBuffer _hashGridTrackerBuffer;
    private ComputeBuffer _densitiesBuffer;
    private ComputeBuffer _pressuresBuffer;
    private ComputeBuffer _velocitiesBuffer;
    private ComputeBuffer _forcesBuffer;
    private static readonly int SizeProperty = Shader.PropertyToID("_size");
    private static readonly int ParticlesBufferProperty = Shader.PropertyToID("_particlesBuffer");
    private static readonly int MeshBufferProperty = Shader.PropertyToID("_meshProperties");
    public GameObject Collider;
    [Range(0f, 10f)]public float _colliderRadius;
    private Vector3 _lastColliderPos;
    private float _posMoveThreshold = 0.1f; // 移动0.1米才更新
    private float _lastColliderRadius = -1f;
    private int TerrainWidth, TerrainLength;

    //private static readonly int VelocityField = Shader.PropertyToID("_velocitiesBuffer");

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
    private Mesh mesh;
    private Bounds bounds;


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

    private void Awake()
    {
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
        InitComputeBuffers();
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
                    {//ToDo 开放参数初始化
                        Vector3 startPos = new Vector3(dimensions - 1, dimensions - 1, dimensions - 1)
                            - new Vector3(x / 2f, y / 2f, z / 2f) - new Vector3(Random.Range(0, 0.01f), Random.Range(0f, 0.01f), Random.Range(0f, 0.01f));

                        
                        _particles[counter] = new Particle
                        {
                            position = startPos,
                            colorGradient = Color.white,
                        };
                        _densities[counter] = -1f;
                        _pressures[counter] = 0.0f;
                        _forces[counter] = Vector3.zero;
                        _velocities[counter] = Vector3.down * 50;

                        MeshProperties props = new MeshProperties();
                        Quaternion rotation = Quaternion.Euler(Random.Range(-180, 180), Random.Range(-180, 180), Random.Range(-180, 180));
                        Vector3 scale = Vector3.one;
                        props.mat = Matrix4x4.TRS(Vector3.zero, rotation, scale);
                        // props.color = Color.Lerp(Color.red, Color.blue, Random.value);
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
        createVelocityTexKernel = computeShaderSPH.FindKernel("UpdateVelocityFieldWithHash");
       // DispatchKernel = computeShaderSPH.FindKernel("DrawMesh");

    }



    // Start is called before the first frame update
    private void InitComputeShader()
    {
        computeShaderSPH.SetFloat("CellSize", radius * 2);
        computeShaderSPH.SetInt("Dimensions", dimensions);
        computeShaderSPH.SetInt("maximumParticlesPerCell", maximumParticlesPerCell);
        computeShaderSPH.SetInt("_FieldResolution", velocityFieldResolution);
        computeShaderSPH.SetFloat("radius", radius);
        computeShaderSPH.SetFloat("radius2", radius2);
        computeShaderSPH.SetFloat("radius3", radius3);
        computeShaderSPH.SetFloat("radius4", radius4);
        computeShaderSPH.SetFloat("radius5", radius4);
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

    void InitComputeBuffers()
    {

        Debug.Log("look1 "+mesh.GetIndexCount(0)+" "+mesh.GetIndexStart(0)+" "+mesh.GetBaseVertex(0));

        uint[] args = {
        mesh.GetIndexCount(0),
        (uint)numberOfParticles,
        mesh.GetIndexStart(0),
        mesh.GetBaseVertex(0),
        0
        };

        Debug.Log("args" + particleMesh.GetIndexCount(0) + " " + (uint)numberOfParticles + " " + particleMesh.GetIndexStart(0) + " " + particleMesh.GetBaseVertex(0));

        _meshPropertiesBuffer = new ComputeBuffer(numberOfParticles, MeshProperties.Size());
        _meshPropertiesBuffer.SetData(_properties);

        _argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(args);
        _particlesBuffer = new ComputeBuffer(numberOfParticles, sizeof(float)*(3+4));
        _particlesBuffer.SetData(_particles);


        _neighbourList = new int[numberOfParticles * maximumParticlesPerCell * 8];
        _neighbourTracker = new int[numberOfParticles];

        _hashGrid = new uint[dimensions * dimensions * dimensions * maximumParticlesPerCell];
        _hashGridTracker = new uint[dimensions* dimensions* dimensions];

        _neighbourListBuffer = new ComputeBuffer(numberOfParticles * maximumParticlesPerCell * 8, sizeof(int));
        _neighbourListBuffer.SetData(_neighbourList);
        _neighbourTrackerBuffer = new ComputeBuffer(numberOfParticles, sizeof(int));
        _neighbourTrackerBuffer.SetData(_neighbourTracker);

        _hashGridBuffer = new ComputeBuffer(dimensions * dimensions * dimensions * maximumParticlesPerCell, sizeof(uint));
        _hashGridBuffer.SetData(_hashGrid);
        _hashGridTrackerBuffer = new ComputeBuffer(dimensions * dimensions * dimensions, sizeof(uint));
        //TODO CS.stride直接设置
        
        //_particlesBuffer = new ComputeBuffer(numberOfParticles, Particle.stride);
        _densitiesBuffer = new ComputeBuffer(numberOfParticles, sizeof(float));
        _densitiesBuffer.SetData(_densities);
        _pressuresBuffer = new ComputeBuffer(numberOfParticles, sizeof(float));
        _pressuresBuffer.SetData(_pressures);

        _velocitiesBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 3);
        _velocitiesBuffer.SetData(_velocities);
        _forcesBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 3);
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
        
        computeShaderSPH.SetBuffer(createVelocityTexKernel, "_particles", _particlesBuffer);
        computeShaderSPH.SetBuffer(createVelocityTexKernel, "_velocities", _velocitiesBuffer);
        computeShaderSPH.SetBuffer(createVelocityTexKernel, "_hashGrid", _hashGridBuffer);
        computeShaderSPH.SetBuffer(createVelocityTexKernel, "_hashGridTracker", _hashGridTrackerBuffer);

        computeShaderSPH.SetTexture(createVelocityTexKernel, "_VelocityField", velocityFieldTexture);
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
        velocityFieldTexture = new RenderTexture(
            velocityFieldResolution, 
            velocityFieldResolution, 
            0, 
            RenderTextureFormat.ARGBFloat, // 高精度浮点
            RenderTextureReadWrite.Linear
        );
        velocityFieldTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        velocityFieldTexture.volumeDepth = velocityFieldResolution;
        velocityFieldTexture.wrapMode = TextureWrapMode.Clamp;
        velocityFieldTexture.enableRandomWrite = true;
        velocityFieldTexture.Create();
        Debug.Log($"[SPH] 速度场3D纹理创建: {velocityFieldResolution}³");
    }

    void UpdateParamsIfDirty()
    {
        Vector3 colliderPos = Collider.transform.position;  

        if (Vector3.Distance(colliderPos, _lastColliderPos) > _posMoveThreshold)
        {
            computeShaderSPH.SetVector("_ColliderPos", colliderPos);
            _lastColliderPos = colliderPos;
        }

        if (Mathf.Abs(_colliderRadius - _lastColliderRadius) > 0.001f)
        {
            computeShaderSPH.SetFloat("_ColliderRadius", _colliderRadius);
            _lastColliderRadius = _colliderRadius;
        }
    }

    #endregion
    // Update is called once per frame
    void Update()
    {
        UpdateParamsIfDirty();
        computeShaderSPH.Dispatch(clearHashGridKernel, dimensions * dimensions * dimensions / 100, 1, 1);
        computeShaderSPH.Dispatch(recalculateHashGridKernel, numberOfParticles / 100, 1, 1);
        computeShaderSPH.Dispatch(buildNeighbourListKernel, numberOfParticles / 100, 1, 1);
        computeShaderSPH.Dispatch(computeDensityPressureKernel, numberOfParticles / 100, 1, 1);
        computeShaderSPH.Dispatch(computeForcesKernel, numberOfParticles / 100, 1, 1);
        computeShaderSPH.Dispatch(integrateKernel, numberOfParticles / 100, 1, 1);
        // 新增：将粒子速度写入3D纹理
        if (elapsedSimulationSteps % 2 == 0) // 每2帧更新一次，性能优化
        {
            computeShaderSPH.Dispatch(createVelocityTexKernel, velocityFieldResolution/8, velocityFieldResolution/8, velocityFieldResolution/8);
        }

        material.SetFloat(SizeProperty, particleRenderSize);
        material.SetBuffer(ParticlesBufferProperty, _particlesBuffer);
        material.SetBuffer("_meshProperties", _meshPropertiesBuffer);

        Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, _argsBuffer);

        elapsedSimulationSteps++;
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        
        Destroy(_terrainHeightTexture); 

        _particlesBuffer.Dispose();
        _argsBuffer.Dispose();
        _meshPropertiesBuffer.Dispose();
        _neighbourListBuffer.Dispose();
        _neighbourTrackerBuffer.Dispose();
        _hashGridBuffer.Dispose();
        _hashGridTrackerBuffer.Dispose();
        _densitiesBuffer.Dispose();
        _pressuresBuffer.Dispose();
        _velocitiesBuffer.Dispose();
        _forcesBuffer.Dispose();
    }
}