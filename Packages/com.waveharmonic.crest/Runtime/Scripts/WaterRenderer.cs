// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;
using WaveHarmonic.Crest.RelativeSpace;
using WaveHarmonic.Crest.Utility;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// The main script for the water system.
    /// </summary>
    /// <remarks>
    /// Attach this to an object to create water. This script initializes the various
    /// data types and systems and moves/scales the water based on the viewpoint. It
    /// also hosts a number of global settings that can be tweaked here.
    /// </remarks>
    public sealed partial class WaterRenderer : ManagerBehaviour<WaterRenderer>
    {
        internal const string k_RunUpdateMarker = "Crest.WaterRenderer.RunUpdate";

        static readonly Unity.Profiling.ProfilerMarker s_RunUpdateMarker = new(k_RunUpdateMarker);

        internal static partial class ShaderIDs
        {
            public static readonly int s_Center = Shader.PropertyToID("g_Crest_WaterCenter");
            public static readonly int s_Scale = Shader.PropertyToID("g_Crest_WaterScale");
            public static readonly int s_Time = Shader.PropertyToID("g_Crest_Time");
            public static readonly int s_CascadeData = Shader.PropertyToID("g_Crest_CascadeData");
            public static readonly int s_CascadeDataSource = Shader.PropertyToID("g_Crest_CascadeDataSource");
            public static readonly int s_LodChange = Shader.PropertyToID("g_Crest_LodChange");
            public static readonly int s_MeshScaleLerp = Shader.PropertyToID("g_Crest_MeshScaleLerp");
            public static readonly int s_LodCount = Shader.PropertyToID("g_Crest_LodCount");

            // Shader Properties
            public static readonly int s_AbsorptionColor = Shader.PropertyToID("_Crest_AbsorptionColor");
            public static readonly int s_Absorption = Shader.PropertyToID("_Crest_Absorption");
            public static readonly int s_Scattering = Shader.PropertyToID("_Crest_Scattering");
            public static readonly int s_Anisotropy = Shader.PropertyToID("_Crest_Anisotropy");
            public static readonly int s_AmbientTerm = Shader.PropertyToID("_Crest_AmbientTerm");
            public static readonly int s_DirectTerm = Shader.PropertyToID("_Crest_DirectTerm");
            public static readonly int s_ShadowsAffectsAmbientFactor = Shader.PropertyToID("_Crest_ShadowsAffectsAmbientFactor");
            public static readonly int s_PlanarReflectionsEnabled = Shader.PropertyToID("_Crest_PlanarReflectionsEnabled");
            public static readonly int s_Occlusion = Shader.PropertyToID("_Crest_Occlusion");
            public static readonly int s_OcclusionUnderwater = Shader.PropertyToID("_Crest_OcclusionUnderwater");

            // Motion Vectors
            public static readonly int s_CenterDelta = Shader.PropertyToID("g_Crest_WaterCenterDelta");
            public static readonly int s_ScaleChange = Shader.PropertyToID("g_Crest_WaterScaleChange");

            // Underwater
            public static readonly int s_VolumeExtinctionLength = Shader.PropertyToID("_Crest_VolumeExtinctionLength");
        }


        //
        // Viewer
        //

        Transform GetViewpoint()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && _FollowSceneCamera && SceneView.lastActiveSceneView != null && IsSceneViewActive)
            {
                return SceneView.lastActiveSceneView.camera.transform;
            }
#endif
            if (_Viewpoint != null)
            {
                return _Viewpoint;
            }

            // Even with performance improvements, it is still good to cache whenever possible.
            var camera = Viewer;

            if (camera != null)
            {
                return camera.transform;
            }

            return null;
        }

        internal Camera GetViewer(bool includeSceneCamera = true)
        {
#if UNITY_EDITOR
            if (includeSceneCamera && !Application.isPlaying && _FollowSceneCamera && SceneView.lastActiveSceneView != null && IsSceneViewActive)
            {
                return SceneView.lastActiveSceneView.camera;
            }
#endif

            if (_Camera != null)
            {
                return _Camera;
            }

            // Unity has greatly improved performance of this operation in 2019.4.9.
            return Camera.main;
        }

        // Cache the ViewCamera property for internal use.
        Camera _ViewCameraCached;

        readonly SampleCollisionHelper _CenterOfDetailDisplacementCorrectionHelper = new();


        //
        // Viewer Height
        //

        /// <summary>
        /// The water changes scale when viewer changes altitude, this gives the interpolation param between scales.
        /// </summary>
        internal float ViewerAltitudeLevelAlpha { get; private set; }

        /// <summary>
        /// Vertical offset of camera vs water surface.
        /// </summary>
        public float ViewerHeightAboveWater { get; private set; }

        /// <summary>
        /// Vertical offset of viewpoint vs water surface.
        /// </summary>
        public float ViewpointHeightAboveWater { get; private set; }

        /// <summary>
        /// Distance of camera to shoreline. Positive if over water and negative if over land.
        /// </summary>
        public float ViewerDistanceToShoreline { get; private set; }

        /// <summary>
        /// Smoothly varying version of viewpoint height to combat sudden changes in water level that are possible
        /// when there are local bodies of water
        /// </summary>
        float _ViewpointHeightAboveWaterSmooth;

        readonly SampleCollisionHelper _SampleHeightHelper = new();
        readonly SampleDepthHelper _SampleDepthHelper = new();

        internal float _ViewerHeightAboveWaterPerCamera;
        readonly SampleCollisionHelper _SampleHeightHelperPerCamera = new();


        //
        // Teleport Threshold
        //

        float _TeleportTimerForHeightQueries;
        bool _IsFirstFrameSinceEnabled = true;
        internal bool _HasTeleportedThisFrame;
        Vector3 _OldViewpointPosition;

#if d_WaveHarmonic_Crest_ShiftingOrigin
        Vector3 TeleportOriginThisFrame => ShiftingOrigin.ShiftThisFrame;
#else
        Vector3 TeleportOriginThisFrame => Vector3.zero;
#endif

        //
        // Wind
        //

        internal float WindSpeedKPH => _WindSpeed;
        bool WindSpeedOverriden => _WindZone == null || _OverrideWindZoneWindSpeed;
        bool WindDirectionOverriden => _WindZone == null || _OverrideWindZoneWindDirection;
        bool WindTurbulenceOverriden => _WindZone == null || _OverrideWindZoneWindTurbulence;

        float GetWindSpeed()
        {
            return _OverrideWindZoneWindSpeed || _WindZone == null ? _WindSpeed : _WindZone.windMain * 3.6f;
        }

        float GetWindDirection()
        {
            var wind = _WindZone != null ? _WindZone.transform : null;
            return _OverrideWindZoneWindDirection || wind == null
                ? _WindDirection
                : Mathf.Atan2(wind.forward.z, wind.forward.x) * Mathf.Rad2Deg;
        }

        float GetWindTurbulence()
        {
            return _OverrideWindZoneWindTurbulence || _WindZone == null ? _WindTurbulence : _WindZone.windTurbulence;
        }


        //
        // Transform
        //

        internal Vector3 Position { get; private set; }
        internal GameObject Container { get; private set; }

        /// <summary>
        /// Sea level is given by y coordinate of GameObject with WaterRenderer script.
        /// </summary>
        public float SeaLevel => Position.y;

        // Anything higher (minus 1 for near plane) will be clipped.
        const float k_RenderAboveSeaLevel = 10000f;
        // Anything lower will be clipped.
        const float k_RenderBelowSeaLevel = 10000f;

        Matrix4x4[] _ProjectionMatrix;
        internal Matrix4x4 GetProjectionMatrix(int slice) => _ProjectionMatrix[slice];

        internal static Matrix4x4 CalculateViewMatrixFromSnappedPositionRHS(Vector3 snapped)
        {
            return Helpers.CalculateWorldToCameraMatrixRHS(snapped + Vector3.up * k_RenderAboveSeaLevel, Quaternion.AngleAxis(90f, Vector3.right));
        }


        //
        // Time Provider
        //

        /// <summary>
        /// Loosely a stack for time providers.
        /// </summary>
        /// <remarks>
        /// The last <see cref="TimeProvider"/> in the list is the active one. When a
        /// <see cref="TimeProvider"/> gets added to the stack, it is bumped to the top of
        /// the list. When a <see cref="TimeProvider"/> is removed, all instances of it are
        /// removed from the stack. This is less rigid than a real stack which would be
        /// harder to use as users have to keep a close eye on the order that things are
        /// pushed/popped.
        /// </remarks>
        public Utility.Internal.Stack<ITimeProvider> TimeProviders { get; private set; } = new();

        /// <summary>
        /// The current time provider.
        /// </summary>
        public ITimeProvider TimeProvider => TimeProviders.Peek();

        internal float CurrentTime => TimeProvider.Time;
        internal float DeltaTime => TimeProvider.Delta;


        //
        // Environment
        //

        /// <summary>
        /// The primary light that affects the water. This should be a directional light.
        /// </summary>
        Light GetPrimaryLight() => _PrimaryLight == null ? RenderSettings.sun : _PrimaryLight;

        /// <summary>
        /// Physics gravity applied to water.
        /// </summary>
        public float Gravity => _GravityMultiplier * Mathf.Abs(_OverrideGravity ? _GravityOverride : Physics.gravity.y);


        //
        // Rendering
        //

        // Used as an extra check to prevent null exceptions, as the events raised when an
        // RP change happen too late for some things.
        RenderPipeline _SetUpFor;

        internal bool RenderBeforeTransparency =>
#if d_Crest_LegacyUnderwater
            false;
#else
            _InjectionPoint == WaterInjectionPoint.BeforeTransparent;
#endif

#if d_CrestPortals
        internal bool Portaled => _Portals.Active;
#else
        internal bool Portaled => false;
#endif

        internal MaskRenderer _Mask;

        // Flags
        bool _DonePerCameraHeight;

        bool GetWriteMotionVectors() =>
#if !UNITY_6000_0_OR_NEWER
            !RenderPipelineHelper.IsUniversal &&
#endif
            _WriteMotionVectors;

        bool GetWriteToColorTexture()
        {
            return (_WriteToColorTexture && RenderBeforeTransparency) || Meniscus.RequiresOpaqueTexture;
        }

        bool GetWriteToDepthTexture()
        {
            return _WriteToDepthTexture && Surface.Enabled;
        }

        internal static bool ShouldRender(Camera camera)
        {
#if UNITY_EDITOR
            // Preview camera are for preview game view, preview panes, material previews etc.
            if (camera.cameraType == CameraType.Preview)
            {
                return false;
            }
#endif

            return true;
        }

        internal static bool ShouldRender(Camera camera, int layer)
        {
            if (!ShouldRender(camera))
            {
                return false;
            }

            if (!Helpers.MaskIncludesLayer(camera.cullingMask, layer))
            {
                return false;
            }

            return true;
        }


        //
        // Material
        //

        /// <summary>
        /// Calculates the absorption value from the absorption color.
        /// </summary>
        /// <param name="color">The absorption color.</param>
        /// <returns>The absorption value (XYZ value).</returns>
        public static Vector4 CalculateAbsorptionValueFromColor(Color color)
        {
            return UpdateAbsorptionFromColor(color);
        }

        internal static Vector4 UpdateAbsorptionFromColor(Color color)
        {
            var alpha = Vector3.zero;
            alpha.x = Mathf.Log(Mathf.Max(color.r, 0.0001f));
            alpha.y = Mathf.Log(Mathf.Max(color.g, 0.0001f));
            alpha.z = Mathf.Log(Mathf.Max(color.b, 0.0001f));
            // Magic numbers that make fog density easy to control using alpha channel
            return (-color.a * 32f * alpha / 5f).XYZN(1f);
        }

        internal static void UpdateAbsorptionFromColor(Material material)
        {
            var fogColour = material.GetColor(ShaderIDs.s_AbsorptionColor);
            var alpha = Vector3.zero;
            alpha.x = Mathf.Log(Mathf.Max(fogColour.r, 0.0001f));
            alpha.y = Mathf.Log(Mathf.Max(fogColour.g, 0.0001f));
            alpha.z = Mathf.Log(Mathf.Max(fogColour.b, 0.0001f));
            // Magic numbers that make fog density easy to control using alpha channel
            material.SetVector(ShaderIDs.s_Absorption, UpdateAbsorptionFromColor(fogColour));
        }


        //
        // Simulations
        //

        internal List<Lod> Simulations { get; } = new();


        //
        // Instance
        //

        bool _Initialized;
        internal bool Active => enabled && this == Instance;


        //
        // Hash
        //

        // A hash of the settings used to generate the water, used to regenerate when necessary
        int _GeneratedSettingsHash;


        //
        // Runtime Environment
        //

        /// <summary>
        /// Is runtime environment without graphics card
        /// </summary>
        public static bool RunningWithoutGraphics
        {
            get
            {
                var noGPU = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
                var emulateNoGPU = Instance != null && Instance._Debug._ForceNoGraphics;
                return noGPU || emulateNoGPU;
            }
        }

        // No GPU or emulate no GPU.
        internal bool IsRunningWithoutGraphics => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || _Debug._ForceNoGraphics;

        /// <summary>
        /// Is runtime environment non-interactive (not displaying to user).
        /// </summary>
        [System.Obsolete("We no longer care whether Unity is running in non-interactive mode.")]
        public static bool RunningHeadless => false;


        //
        // Frame Timing
        //

        /// <summary>
        /// The frame count for Crest.
        /// </summary>
        public static int FrameCount
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    return s_EditorFrames;
                }
                else
#endif
                {
                    return Time.frameCount;
                }
            }
        }


        //
        // Level of Detail
        //

        internal const string k_DrawLodData = "Crest.LodData";
        internal CommandBuffer SimulationBuffer { get; private set; }

        // Scale, Weight, MaximumWaveLength, Unused
        internal BufferedData<Vector4[]> _CascadeData;

        internal int BufferSize { get; private set; }

        internal float MaximumWavelength(int slice)
        {
            return MaximumWavelength(CalcLodScale(slice));
        }

        internal float MaximumWavelength(float scale)
        {
            var maximumDiameter = 4f * scale;
            // TODO: Do we need to pass in resolution? Could resolution mismatch with animated
            // and dynamic waves be an issue?
            var maximumTexelSize = maximumDiameter / LodResolution;
            var texelsPerWave = 2f;
            return 2f * maximumTexelSize * texelsPerWave;
        }


        //
        // Scale
        //

        /// <summary>
        /// Current water scale (changes with viewer altitude).
        /// </summary>
        public float Scale { get; private set; }
        internal float CalcLodScale(float slice) => Scale * Mathf.Pow(2f, slice);
        internal float CalcGridSize(int slice) => CalcLodScale(slice) / LodResolution;

        /// <summary>
        /// Could the water horizontal scale increase (for e.g. if the viewpoint gains altitude). Will be false if water already at maximum scale.
        /// </summary>
        internal bool ScaleCouldIncrease => _ScaleRange.y == Mathf.Infinity || Scale < _ScaleRange.y * 0.99f;
        /// <summary>
        /// Could the water horizontal scale decrease (for e.g. if the viewpoint drops in altitude). Will be false if water already at minimum scale.
        /// </summary>
        internal bool ScaleCouldDecrease => Scale > _ScaleRange.x * 1.01f;

        internal int ScaleDifferencePower2 { get; private set; }


        //
        // Displacement Reporting
        //

        /// <summary>
        /// User shape inputs can report in how far they might displace the shape horizontally and vertically. The max value is
        /// saved here. Later the bounding boxes for the water tiles will be expanded to account for this potential displacement.
        /// </summary>
        internal void ReportMaximumDisplacement(float horizontal, float vertical, float verticalFromWaves)
        {
            MaximumHorizontalDisplacement += horizontal;
            MaximumVerticalDisplacement += vertical;
            _MaximumVerticalDisplacementFromWaves += verticalFromWaves;
        }

        float _MaximumVerticalDisplacementFromWaves = 0f;
        /// <summary>
        /// The maximum horizontal distance that the shape scripts are displacing the shape.
        /// </summary>
        internal float MaximumHorizontalDisplacement { get; private set; }
        /// <summary>
        /// The maximum height that the shape scripts are displacing the shape.
        /// </summary>
        internal float MaximumVerticalDisplacement { get; private set; }


        //
        // Query Providers
        //

        /// <summary>
        /// Provides water shape to CPU.
        /// </summary>
        public ICollisionProvider CollisionProvider => AnimatedWavesLod?.Provider;

        /// <summary>
        /// Provides flow to the CPU.
        /// </summary>
        public IFlowProvider FlowProvider => FlowLod?.Provider;

        /// <summary>
        /// Provides water depth and distance to water edge to the CPU.
        /// </summary>
        public IDepthProvider DepthProvider => DepthLod?.Provider;


        //
        // Component
        //

        // Drive state from OnEnable and OnDisable? OnEnable on RegisterLodDataInput seems to get called on script reload
        private protected override void Initialize()
        {
            base.Initialize();

            _SetUpFor = RenderPipelineHelper.RenderPipeline;

            _IsFirstFrameSinceEnabled = true;
            _ViewCameraCached = Viewer;

            if (_Initialized)
            {
                Enable();
                return;
            }

            Utility.RTHandles.Initialize();

            _Mask = MaskRenderer.Instantiate(this);

            Meniscus.Initialize(this);

            Surface._Water = this;
            _Reflections._Water = this;
            _Reflections._UnderWater = _Underwater;
            _Underwater._Water = this;
#if d_CrestPortals
            _Underwater._Portals = _Portals;
            _Portals._Water = this;
            _Portals._UnderWater = _Underwater;
#endif

            _DepthLod._Water = this;
            _LevelLod._Water = this;
            _FlowLod._Water = this;
            _DynamicWavesLod._Water = this;
            _AnimatedWavesLod._Water = this;
            _FoamLod._Water = this;
            _ClipLod._Water = this;
            _AbsorptionLod._Water = this;
            _ScatteringLod._Water = this;
            _AlbedoLod._Water = this;
            _ShadowLod._Water = this;

            // Add simulations to a list for common operations. Order is important.
            Simulations.Clear();
            Simulations.Add(_DepthLod);
            Simulations.Add(_LevelLod);
            Simulations.Add(_FlowLod);
            Simulations.Add(_DynamicWavesLod);
            Simulations.Add(_AnimatedWavesLod);
            Simulations.Add(_FoamLod);
            Simulations.Add(_AbsorptionLod);
            Simulations.Add(_ScatteringLod);
            Simulations.Add(_ClipLod);
            Simulations.Add(_AlbedoLod);
            Simulations.Add(_ShadowLod);

            // Setup a default time provider, and add the override one (from the inspector)
            TimeProviders.Clear();

            // Put a base TP that should always be available as a fallback
            TimeProviders.Push(new DefaultTimeProvider());

            // Add the TP from the inspector
            if (_TimeProvider != null)
            {
                TimeProviders.Push(_TimeProvider);
            }

            if (!VerifyRequirements())
            {
                enabled = false;
                return;
            }

            SimulationBuffer ??= new()
            {
                name = k_DrawLodData,
            };

            Container = new()
            {
                name = "Container",
                hideFlags = _Debug._ShowHiddenObjects ? HideFlags.DontSave : HideFlags.HideAndDontSave
            };
            Container.transform.SetParent(transform, worldPositionStays: false);
            this.Manage(Container);

            Scale = Mathf.Clamp(Scale, _ScaleRange.x, _ScaleRange.y);

            foreach (var simulation in Simulations)
            {
                // Bypasses Enabled and has an internal check.
                if (!simulation._Enabled) continue;
                simulation.Initialize();
            }

            // TODO: Have a BufferCount which will be the run-time buffer size or prune data.
            // NOTE: Hardcode minimum (2) to avoid breaking server builds and LodData* toggles.
            // Gather the buffer size for shared data.
            BufferSize = 2;
            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                BufferSize = Mathf.Max(BufferSize, simulation.BufferCount);
            }

            // The extra LOD accounts for reading off the cascade (eg CurrentIndex + LodChange + 1).
            _CascadeData = new(BufferSize, () => new Vector4[Lod.k_MaximumSlices + 1]);

            _ProjectionMatrix = new Matrix4x4[LodLevels];

            if (Application.isPlaying && _Debug._AttachDebugGUI && !TryGetComponent<DebugGUI>(out _))
            {
                gameObject.AddComponent<DebugGUI>().hideFlags = HideFlags.DontSave;
            }

            _GeneratedSettingsHash = CalculateSettingsHash();

            // Prevent MVs from popping on first frame.
            if (!_Debug._DisableFollowViewpoint && _ViewCameraCached != null)
            {
                LateUpdatePosition();
                LateUpdateScale();
            }

            WritePerFrameMaterialParams();

            if (Surface.Enabled)
            {
                Surface.Initialize();
            }

            foreach (var body in WaterBody.WaterBodies)
            {
                if (body._Material != null)
                {
                    Surface.UpdateMaterial(body._Material, ref body._MotionVectorMaterial);
                }
            }

            Enable();
            _Initialized = true;
        }

        void OnDisable()
        {
            Disable();

            // Always clean up in OnDisable during edit mode as OnDestroy is not always called.
            if (_Debug._DestroyResourcesInOnDisable || !Application.isPlaying)
            {
                Destroy();
            }
        }

        void OnDestroy()
        {
            // Only clean up in OnDestroy when not in edit mode.
            if (_Debug._DestroyResourcesInOnDisable || !Application.isPlaying)
            {
                return;
            }

            Destroy();
        }

        private protected override void LateUpdate()
        {
#if CREST_DEBUG
#if UNITY_EDITOR
            if (_SkipForTesting)
            {
                return;
            }
#endif
#endif

#if UNITY_EDITOR
            // Don't run immediately if in edit mode - need to count editor frames so this is run through EditorUpdate()
            if (Application.isPlaying)
#endif
            {
                RunUpdate();
            }
        }


        //
        // Methods
        //

        private protected override void Enable()
        {
            base.Enable();

#if UNITY_EDITOR
            EditorTime = Time.time;
            EditorDeltaTime = 0;

            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
#endif

            // Needs to be first or will get assertions etc. Unity bug likely.
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineTypeChanged;
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnActiveRenderPipelineTypeChanged;

            // Needs to run even without graphics to initialize provider.
            foreach (var simulation in Simulations)
            {
                simulation.SetGlobals(enable: true);
                if (!simulation.Enabled) continue;
                simulation.Enable();
            }

            if (IsRunningWithoutGraphics)
            {
                // We need nothing from here on.
                return;
            }

#if d_WaveHarmonic_Crest_ShiftingOrigin
            ShiftingOrigin.OnShift -= OnOriginShift;
            ShiftingOrigin.OnShift += OnOriginShift;
#endif

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

            // This event should not when not using the built-in renderer, but in some cases it can in the editor like
            // when using scene filtering.
            if (RenderPipelineHelper.IsLegacy)
            {
                Camera.onPreCull -= OnBeginCameraRendering;
                Camera.onPreCull += OnBeginCameraRendering;
                Camera.onPostRender -= OnEndCameraRendering;
                Camera.onPostRender += OnEndCameraRendering;
            }

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                // Always enable as it sets requirements.
                SurfaceRenderer.WaterSurfaceRenderPass.Enable(this);
            }
#endif

#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                if (RenderBeforeTransparency)
                {
                    SurfaceRenderer.WaterSurfaceCustomPass.Enable(this);
                }

                CrestInternalCopyToTextureCustomPass.Enable(this);
            }

#if UNITY_EDITOR
            if (RenderPipelineHelper.IsHighDefinition)
            {
                RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            }
#endif
#endif

            Container.SetActive(true);

            _Mask.Enable();

            if (_Underwater._Enabled)
            {
                _Underwater.OnEnable();
            }

            if (Meniscus.Enabled)
            {
                Meniscus.Enable();
            }

#if d_UnityURP
            if (RenderPipelineHelper.IsUniversal)
            {
                if (WriteToColorTexture || WriteToDepthTexture)
                {
                    CopyTargetsRenderPass.Enable(this);
                }
            }
#endif

#if d_CrestPortals
            if (_Portals._Enabled)
            {
                _Portals.OnEnable();
            }
#endif

            if (_Reflections._Enabled)
            {
                _Reflections.OnEnable();
            }
        }

        // Because we cannot pass null when using built-in render pipeline.
        // Being a struct there should not be any side effects.
        internal ScriptableRenderContext _Context = new();

        void OnBeginCameraRendering(Camera camera)
        {
            if (_SetUpFor != RenderPipelineHelper.RenderPipeline)
            {
                return;
            }

            Utility.RTHandles.OnBeginCameraRendering(camera);

            OnBeginCameraRendering(_Context, camera);
        }

        // OnBeginCameraRendering or OnPreCull
        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // Guard against being called before the RP change events are raised.
            if (_SetUpFor != RenderPipelineHelper.RenderPipeline)
            {
                return;
            }

#if UNITY_EDITOR
            UpdateLastActiveSceneCamera(camera);
#endif

            if (!ShouldRender(camera))
            {
                return;
            }

            var noSurface = !Surface.Enabled || !Helpers.MaskIncludesLayer(camera.cullingMask, Surface.Layer);
            var noVolume = !Underwater.Enabled || !Helpers.MaskIncludesLayer(camera.cullingMask, Underwater.Layer);

            // Nothing to render to this camera.
            if (noSurface && noVolume)
            {
                return;
            }

            // Must render first so that we do not overwrite work below for game camera.
            // Reflections only make sense with an active surface.
            if (_Reflections._Enabled && Surface.Enabled)
            {
                _Reflections.OnBeginCameraRendering(context, camera);
            }

            if (_Mask.Enabled)
            {
                _Mask.OnBeginCameraRendering(camera);
            }

            // Water lighting etc.
#if d_UnityHDRP
            if (RenderPipelineHelper.IsHighDefinition)
            {
                UpdateHighDefinitionLighting(camera);
            }
            else
#endif

            if (RenderPipelineHelper.IsLegacy)
            {
                OnBeginCameraRenderingLegacy(camera);
            }

            // Always execute before surface, as order is only important when rendering volume
            // before surface.
            if (Underwater._Enabled)
            {
                Underwater.OnBeginCameraRendering(context, camera);
            }

#if d_CrestPortals
            // Call between volume and surface. Sets water line uniforms.
            if (Portals.Enabled)
            {
                Portals.OnBeginCameraRendering(camera);
            }
#endif

            if (Surface.Enabled)
            {
                Surface.OnBeginCameraRendering(context, camera);
            }

#if d_UnityURP
            // Always execute after surface.
            if (RenderPipelineHelper.IsUniversal)
            {
                CopyTargetsRenderPass.Instance.OnBeginCameraRendering(context, camera);
            }
            else
#endif

            if (RenderPipelineHelper.IsLegacy)
            {
                OnLegacyCopyPass(camera);
            }

            // Execute after copy pass in case refraction.
            if (Meniscus.Enabled)
            {
                Meniscus.Renderer.OnBeginCameraRendering(camera);
            }

            if (_ShadowLod.Enabled)
            {
                _ShadowLod.OnBeginCameraRendering(context, camera);
            }
        }

        void OnEndCameraRendering(Camera camera)
        {
            OnEndCameraRendering(_Context, camera);
        }

        void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            _DonePerCameraHeight = false;

#if d_UnityHDRP
            _DoneHighDefinitionLighting = false;
#endif

            if (RenderPipelineHelper.IsLegacy)
            {
                OnEndCameraRenderingLegacy(camera);
            }

            if (_Mask.Enabled)
            {
                _Mask.OnEndCameraRendering(camera);
            }

            if (Meniscus.Enabled)
            {
                Meniscus.Renderer.OnEndCameraRendering(camera);
            }

            if (Underwater._Enabled)
            {
                Underwater.OnEndCameraRendering(camera);
            }

            if (Surface.Enabled)
            {
                Surface.OnEndCameraRendering(camera);
            }

            if (_Reflections._Enabled)
            {
                _Reflections.OnEndCameraRendering(camera);
            }

            if (_ShadowLod.Enabled)
            {
                _ShadowLod.OnEndCameraRendering(camera);
            }

#if d_CrestPortals
            if (_Portals.Enabled)
            {
                _Portals.OnEndCameraRendering(camera);
            }
#endif
        }

        internal void UpdatePerCameraHeight(Camera camera)
        {
            if (_DonePerCameraHeight)
            {
                return;
            }

            // This will be 1-frame behind.
            var viewpoint = camera.transform.position;
            _SampleHeightHelperPerCamera.SampleHeight(System.HashCode.Combine(GetHashCode(), camera.GetHashCode()), viewpoint, out var height, allowMultipleCallsPerFrame: true);
            _ViewerHeightAboveWaterPerCamera = viewpoint.y - height;

            _DonePerCameraHeight = true;
        }

        void OnActiveRenderPipelineTypeChanged()
        {
            _Mask?.Destroy();
            _Mask = MaskRenderer.Instantiate(this);

            Meniscus.OnActiveRenderPipelineTypeChanged();

            if (isActiveAndEnabled)
            {
                // Must destroy as there is still some state left like buffer count.
                Disable();
                Destroy();
                Initialize();
            }
        }

        internal void Rebuild()
        {
            Disable();
            Destroy();
            OnEnable();
        }

        bool VerifyRequirements()
        {
            if (!RunningWithoutGraphics)
            {
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    Debug.LogError("Crest: Crest does not support WebGL backends.", this);
                    return false;
                }
#if UNITY_EDITOR
                if (SystemInfo.graphicsDeviceType is GraphicsDeviceType.OpenGLES3 or GraphicsDeviceType.OpenGLCore)
                {
                    Debug.LogError("Crest: Crest does not support OpenGL backends.", this);
                    return false;
                }
#endif
                if (SystemInfo.graphicsShaderLevel < 45)
                {
                    Debug.LogError("Crest: Crest requires graphics devices that support shader level 4.5 or above.", this);
                    return false;
                }
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogError("Crest: Crest requires graphics devices that support compute shaders.", this);
                    return false;
                }
                if (!SystemInfo.supports2DArrayTextures)
                {
                    Debug.LogError("Crest: Crest requires graphics devices that support 2D array textures.", this);
                    return false;
                }
            }

            return true;
        }

        int CalculateSettingsHash()
        {
            var settingsHash = Hash.CreateHash();

            // Add all the settings that require rebuilding..
            Hash.AddInt(_Resolution, ref settingsHash);
            Hash.AddInt(_Slices, ref settingsHash);
            Hash.AddBool(WriteMotionVectors, ref settingsHash);
            Hash.AddBool(_Debug._ForceNoGraphics, ref settingsHash);
            Hash.AddBool(_Debug._ShowHiddenObjects, ref settingsHash);

            return settingsHash;
        }

        void RunUpdate()
        {
            s_RunUpdateMarker.Begin(this);

            // Rebuild if needed. Needs to run in builds (for MVs at the very least).
            if (CalculateSettingsHash() != _GeneratedSettingsHash)
            {
                Rebuild();
            }

            if (RunningWithoutGraphics)
            {
                // All we need for servers.
                BroadcastUpdate();
                Position = new(0f, transform.position.y, 0f);
                base.LateUpdate();
            }
            else
            {
                _ViewCameraCached = Viewer;

                // Reset displacement reporting values.
                // This is written to in Update, and read in LateUpdate (chunk) and LateUpdateScale.
                MaximumHorizontalDisplacement = MaximumVerticalDisplacement = _MaximumVerticalDisplacementFromWaves = 0f;

                BroadcastUpdate();

                if (!_Debug._DisableFollowViewpoint && _ViewCameraCached != null)
                {
                    LateUpdatePosition();
                    LateUpdateViewerHeight();
                    LateUpdateScale();
                }
                else
                {
                    Position = new(0f, transform.position.y, 0f);
                }

                // Set global shader params
                Shader.SetGlobalFloat(ShaderIDs.s_Time, CurrentTime);
                Shader.SetGlobalFloat(ShaderIDs.s_LodCount, LodLevels);

                // Needs updated transform values like scale.
                WritePerFrameMaterialParams();

                // Construct the command buffer and attach it to the camera so that it will be executed in the render.
                {
                    SimulationBuffer.Clear();

                    foreach (var simulation in Simulations)
                    {
                        if (!simulation.Enabled) continue;
                        simulation.BuildCommandBuffer(this, SimulationBuffer);
                    }

                    // This will execute at the beginning of the frame before the graphics queue.
                    Graphics.ExecuteCommandBuffer(SimulationBuffer);

                    foreach (var simulation in Simulations)
                    {
                        if (!simulation.Enabled) continue;
                        simulation.AfterExecute();
                    }
                }

                base.LateUpdate();

                // Call after LateUpdate so chunk bounds are updated.
                if (Surface.Enabled)
                {
                    Surface.LateUpdate();
                }

                if (_Reflections._Enabled && !_Reflections.SupportsRecursiveRendering)
                {
                    _Reflections.LateUpdate(_Context);
                }
            }

            // Run queries at end of update. For CollProviderBakedFFT calling this kicks off
            // collision processing job, and the next call to Query() will force a complete, and
            // we don't want that to happen until they've had a chance to run, so schedule them
            // late.
            if (AnimatedWavesLod.CollisionSource == CollisionSource.CPU)
            {
                AnimatedWavesLod.Provider?.UpdateQueries(this);
            }

            _IsFirstFrameSinceEnabled = false;

            s_RunUpdateMarker.End();
        }

        void WritePerFrameMaterialParams()
        {
            _CascadeData.Flip();

            var current = _CascadeData.Current;

            // Update rendering parameters.
            {
                var levels = LodLevels;

                for (var slice = 0; slice < levels; slice++)
                {
                    var scale = CalcLodScale(slice);
                    current[slice] = new Vector4(scale, 1f, MaximumWavelength(scale), 0f);

                    _ProjectionMatrix[slice] = Matrix4x4.Ortho(-2f * scale, 2f * scale, -2f * scale, 2f * scale, 1f, k_RenderAboveSeaLevel + k_RenderBelowSeaLevel);
                    if (slice == 0) Shader.SetGlobalFloat(ShaderIDs.s_Scale, scale);
                }

                // Duplicate last element so that things can safely read off the end of the cascades
                current[levels] = current[levels - 1].XNZW(0f);
            }

            Shader.SetGlobalVectorArray(ShaderIDs.s_CascadeData, current);
            Shader.SetGlobalVectorArray(ShaderIDs.s_CascadeDataSource, _CascadeData.Previous(1));
        }

        void LateUpdatePosition()
        {
            var position = Viewpoint.position;

            // This will cause artifacts in motion vectors debug view, but are likely negligible.
            if (_CenterOfDetailDisplacementCorrection && _CenterOfDetailDisplacementCorrectionHelper.SampleDisplacement(position, out var displacement))
            {
                position = new(position.x - displacement.x, position.y, position.z - displacement.z);
            }

            // maintain y coordinate - sea level
            position.y = transform.position.y;

            // Don't land very close to regular positions where things are likely to snap to, because different tiles might
            // land on either side of a snap boundary due to numerical error and snap to the wrong positions. Nudge away from
            // common by using increments of 1/60 which have lots of factors.
            // :WaterGridPrecisionErrors
            if (Mathf.Abs(position.x * 60f - Mathf.Round(position.x * 60f)) < 0.001f)
            {
                position.x += 0.002f;
            }
            if (Mathf.Abs(position.z * 60f - Mathf.Round(position.z * 60f)) < 0.001f)
            {
                position.z += 0.002f;
            }

            Shader.SetGlobalVector(ShaderIDs.s_CenterDelta, (position - Position).XZ());

            Position = position;
            Shader.SetGlobalVector(ShaderIDs.s_Center, Position);
        }

        void LateUpdateScale()
        {
            var viewerHeight = _ViewpointHeightAboveWaterSmooth;

            // Reach maximum detail at slightly below sea level. this should combat cases where visual range can be lost
            // when water height is low and camera is suspended in air. i tried a scheme where it was based on difference
            // to water height but this does help with the problem of horizontal range getting limited at bad times.
            viewerHeight += _MaximumVerticalDisplacementFromWaves * _DropDetailHeightBasedOnWaves;

            var camDistance = Mathf.Abs(viewerHeight);

            // offset level of detail to keep max detail in a band near the surface
            camDistance = Mathf.Max(camDistance - 4f, 0f);

            // scale water mesh based on camera distance to sea level, to keep uniform detail.
            var level = camDistance;
            level = Mathf.Max(level, _ScaleRange.x);
            if (_ScaleRange.y < Mathf.Infinity) level = Mathf.Min(level, 1.99f * _ScaleRange.y);

            var l2 = Mathf.Log(level) / Mathf.Log(2f);
            var l2f = Mathf.Floor(l2);

            ViewerAltitudeLevelAlpha = l2 - l2f;

            var newScale = Mathf.Pow(2f, l2f);

            if (Scale > 0f)
            {
                var ratio = newScale / Scale;
                var ratioL2 = Mathf.Log(ratio) / Mathf.Log(2f);
                ScaleDifferencePower2 = Mathf.RoundToInt(ratioL2);
                Shader.SetGlobalFloat(ShaderIDs.s_LodChange, ScaleDifferencePower2);
                Shader.SetGlobalFloat(ShaderIDs.s_ScaleChange, ratio);

#if UNITY_EDITOR
#if CREST_DEBUG
                if (ratio != 1f)
                {
                    EditorApplication.isPaused = EditorApplication.isPaused || _Debug._PauseOnScaleChange;
                    if (_Debug._LogScaleChange) Debug.Log($"Scale Change: {newScale} / {Scale} = {ratio}. LOD Change: {ScaleDifferencePower2}");
                }
#endif
#endif
            }

            Scale = newScale;

            // LOD 0 is blended in/out when scale changes, to eliminate pops. Here we set it as
            // a global, whereas in WaterChunkRenderer it is applied to LOD0 tiles only through
            // instance data. This global can be used in compute, where we only apply this
            // factor for slice 0.
            Shader.SetGlobalFloat(ShaderIDs.s_MeshScaleLerp, ScaleCouldIncrease ? ViewerAltitudeLevelAlpha : 0f);
        }

        void LateUpdateViewerHeight()
        {
            var viewpoint = Viewpoint;

            _SampleHeightHelper.SampleHeight(viewpoint.position, out var waterHeight);
            ViewerHeightAboveWater = ViewpointHeightAboveWater = viewpoint.position.y - waterHeight;

            var viewerHeightAboveWaterOrTerrain = ViewpointHeightAboveWater;

            if (viewpoint != _ViewCameraCached.transform)
            {
                var viewer = _ViewCameraCached.transform;
                // Reuse sampler. Combine hash codes to avoid pontential conflict.
                _SampleHeightHelper.SampleHeight(System.HashCode.Combine(GetHashCode(), viewer.GetHashCode()), viewpoint.position, out waterHeight, allowMultipleCallsPerFrame: true);
                ViewerHeightAboveWater = viewer.position.y - waterHeight;
            }

#if d_Unity_Terrain
            // Also use terrain height for scale. Viewpoint is absolute if set.
            if (_SampleTerrainHeightForScale && LevelLod.Enabled && _Viewpoint == null)
            {
                var viewerPosition = viewpoint.position;
                var viewerHeight = viewerPosition.y;

                var viewerHeightAboveTerrain = Mathf.Infinity;
                var terrain = Helpers.GetTerrainAtPosition(viewerPosition.XZ());
                if (terrain != null)
                {
                    var terrainHeight = terrain.GetPosition().y + terrain.SampleHeight(viewerPosition);
                    var heightAbove = viewerHeight - terrainHeight;

                    // Ignore if viewer is under terrain.
                    if (heightAbove >= 0f)
                    {
                        viewerHeightAboveTerrain = heightAbove;
                    }
                }

                if (viewerHeightAboveTerrain < Mathf.Abs(viewerHeightAboveWaterOrTerrain))
                {
                    viewerHeightAboveWaterOrTerrain = viewerHeightAboveTerrain;
                }
            }
#endif // d_Unity_Terrain

            // Calculate teleport distance and create window for height queries to return a height change.
            {
                if (_TeleportTimerForHeightQueries > 0f)
                {
                    _TeleportTimerForHeightQueries -= Time.deltaTime;
                }

                var hasTeleported = _IsFirstFrameSinceEnabled;
                if (!_IsFirstFrameSinceEnabled)
                {
                    // Find the distance. Adding the FO offset will exclude FO shifts so we can determine a normal teleport.
                    // FO shifts are visually the same position and it is incorrect to treat it as a normal teleport.
                    var teleportDistanceSqr = (_OldViewpointPosition - viewpoint.position - TeleportOriginThisFrame).sqrMagnitude;
                    // Threshold as sqrMagnitude.
                    var thresholdSqr = _TeleportThreshold * _TeleportThreshold;
                    hasTeleported = teleportDistanceSqr > thresholdSqr;
                }

                if (hasTeleported)
                {
                    // Height queries can take a few frames so a one second window should be plenty.
                    _TeleportTimerForHeightQueries = 1f;
                }

                _HasTeleportedThisFrame = hasTeleported;

                _OldViewpointPosition = viewpoint.position;
            }

            // Smoothly varying version of viewer height to combat sudden changes in water level that are possible
            // when there are local bodies of water
            _ViewpointHeightAboveWaterSmooth = Mathf.Lerp
            (
                _ViewpointHeightAboveWaterSmooth,
                viewerHeightAboveWaterOrTerrain,
                _TeleportTimerForHeightQueries > 0f || !(_ForceScaleChangeSmoothing || (LevelLod.Enabled && !_SampleTerrainHeightForScale)) ? 1f : 0.05f
            );

#if CREST_DEBUG
            if (_Debug._IgnoreWavesForScaleChange)
            {
                _ViewpointHeightAboveWaterSmooth = Viewpoint.transform.position.y - SeaLevel;
            }
#endif

            _SampleDepthHelper.SampleDistanceToWaterEdge(_ViewCameraCached.transform.position, out var distance);
            ViewerDistanceToShoreline = distance;
        }

        void Destroy()
        {
            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                simulation.Destroy();
            }
            Simulations.Clear();

            _Mask?.Destroy();

            Meniscus.Destroy();

            // Clean up modules.
#if d_CrestPortals
            _Portals.OnDestroy();
#endif
            _Underwater.OnDestroy();
            _Reflections.OnDestroy();
            Surface.OnDestroy();

            if (Container)
            {
                Helpers.Destroy(Container);
                Container = null;
            }

            _Initialized = false;
        }

        private protected override void Disable()
        {
            foreach (var simulation in Simulations)
            {
                simulation.SetGlobals(enable: false);
                if (!simulation.Enabled) continue;
                simulation.Disable();
            }

            if (RenderPipelineHelper.IsLegacy && Viewer != null)
            {
                // Need to call to prevent crash.
                OnEndCameraRenderingLegacy(Viewer);
            }

#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif

            Camera.onPreCull -= OnBeginCameraRendering;
            Camera.onPostRender -= OnEndCameraRendering;

#if d_UnityHDRP
            SurfaceRenderer.WaterSurfaceCustomPass.Disable();
            CrestInternalCopyToTextureCustomPass.Disable();
#if UNITY_EDITOR
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
#endif
#endif

#if d_WaveHarmonic_Crest_ShiftingOrigin
            ShiftingOrigin.OnShift -= OnOriginShift;
#endif

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineTypeChanged;

            _Mask?.Disable();

#if d_CrestPortals
            if (_Portals._Enabled) _Portals.OnDisable();
#endif
            if (_Underwater._Enabled) _Underwater.OnDisable();
            if (_Reflections._Enabled) _Reflections.OnDisable();

            if (Meniscus.Enabled)
            {
                Meniscus.Disable();
            }

            if (Container != null)
            {
                Container.SetActive(false);
            }

            base.Disable();
        }

#if d_WaveHarmonic_Crest_ShiftingOrigin
        /// <summary>
        /// Notify water of origin shift
        /// </summary>
        void OnOriginShift(Vector3 newOrigin)
        {
            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                simulation.SetOrigin(newOrigin);
            }
        }
#endif

        /// <summary>
        /// Clears persistent LOD data. Some simulations have persistent data which can linger for a little while after
        /// being disabled. This will manually clear that data.
        /// </summary>
        void ClearLodData()
        {
            foreach (var simulation in Simulations)
            {
                if (!simulation.Enabled) continue;
                simulation.ClearLodData();
            }
        }
    }

#if CREST_DEBUG
#if UNITY_EDITOR
    // Tests.
    partial class WaterRenderer
    {
        internal bool _SkipForTesting;

        private protected override void FixedUpdate()
        {
            if (_SkipForTesting)
            {
                return;
            }

            base.FixedUpdate();
        }

        internal void TestFixedUpdate()
        {
            _SkipForTesting = false;
            FixedUpdate();
            _SkipForTesting = true;
        }

        internal void TestLateUpdate()
        {
            _SkipForTesting = false;
            LateUpdate();
            _SkipForTesting = true;
        }
    }
#endif
#endif
}
