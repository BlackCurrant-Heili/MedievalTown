// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest
{
#if !d_CrestPortals
    namespace Portals
    {
        // Dummy script to keep serializer from complaining.
        [System.Serializable]
        public sealed class PortalRenderer { }
    }
#endif

    /// <summary>
    /// The render pass injection point.
    /// </summary>
    [@GenerateDoc]
    public enum WaterInjectionPoint
    {
        /// <inheritdoc cref="Generated.WaterInjectionPoint.Default" />
        [Tooltip("Renders in the default pass.\n\nFor the water surface, this will be determined by the material (opaque or transparent). This pass is controlled by Unity, and is not compatible with certain features like soft particles.\n\nFor the water volume, this will be after transparency.")]
        Default,

        /// <inheritdoc cref="Generated.WaterInjectionPoint.BeforeTransparent" />
        [Tooltip("Renders before the transparent pass.\n\nThis has advantages like being compatible with soft particles, refractive shaders, and possibly third-party fog.")]
        BeforeTransparent,
    }

    partial class WaterRenderer
    {
        internal const float k_MaximumWindSpeedKPH = 150f;

        [@Space(1, isAlwaysVisible: true)]

        [@Group("General", Group.Style.Accordian)]

        [Tooltip("The camera which drives the water data.\n\nSetting this is optional. Defaults to the main camera.")]
        [@GenerateAPI(Getter.Custom, name: "Viewer")]
        [@DecoratedField, SerializeField]
        internal Camera _Camera;

        [Tooltip("Optional provider for time.\n\nCan be used to hard-code time for automation, or provide server time. Defaults to local Unity time.")]
        [@DecoratedField, SerializeField]
        internal TimeProvider _TimeProvider;


        [@Group("Environment", Group.Style.Accordian)]

        [Tooltip("Uses a provided WindZone as the source of global wind.\n\nIt must be directional. Wind speed units are presumed to be in m/s.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        WindZone _WindZone;

        [Tooltip("Whether to override the given wind zone's wind speed.")]
        [@Predicated(nameof(_WindZone), hide: true)]
        [@InlineToggle, SerializeField]
        bool _OverrideWindZoneWindSpeed;

        [Tooltip("Base wind speed in km/h.\n\nControls wave conditions. Can be overridden on Shape* components.")]
        [@Predicated(typeof(WaterRenderer), nameof(WindSpeedOverriden), inverted: true, hide: true)]
        [@ShowComputedProperty(nameof(WindSpeed))]
        [@Range(0, k_MaximumWindSpeedKPH, scale: 2f)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        internal float _WindSpeed = 10f;

        [Tooltip("Whether to override the given wind zone's wind direction.")]
        [@Predicated(nameof(_WindZone), hide: true)]
        [@InlineToggle, SerializeField]
        bool _OverrideWindZoneWindDirection;

        [Tooltip("Base wind direction in degrees.\n\nControls wave conditions. Can be overridden on Shape* components.")]
        [@Predicated(typeof(WaterRenderer), nameof(WindDirectionOverriden), inverted: true, hide: true)]
        [@ShowComputedProperty(nameof(WindDirection))]
        [@Range(-180, 180)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        internal float _WindDirection;

        [Tooltip("Whether to override the given wind zone's wind turbulence.")]
        [@Predicated(nameof(_WindZone), hide: true)]
        [@InlineToggle, SerializeField]
        bool _OverrideWindZoneWindTurbulence;

        [Tooltip("Base wind turbulence.\n\nControls wave conditions. Can be overridden on ShapeFFT components.")]
        [@Predicated(typeof(WaterRenderer), nameof(WindTurbulenceOverriden), inverted: true, hide: true)]
        [@ShowComputedProperty(nameof(WindTurbulence))]
        [@Range(0, 1)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        internal float _WindTurbulence = 0.145f;

        [@Space(10)]

        [Tooltip("Provide your own gravity value instead of Physics.gravity.")]
        [@GenerateAPI]
        [@InlineToggle, SerializeField]
        bool _OverrideGravity;

        [@Label("Gravity")]
        [Tooltip("Gravity for all wave calculations.")]
        [@Predicated(nameof(_OverrideGravity))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _GravityOverride = -9.8f;

        [Tooltip("Multiplier for physics gravity.")]
        [@Range(0f, 10f)]
        [@GenerateAPI]
        [SerializeField]
        float _GravityMultiplier = 1f;

        [@Space(10)]

        [Tooltip("The primary light that affects the water.\n\nSetting this is optional. This should be a directional light. Defaults to RenderSettings.sun.")]
        [@GenerateAPI(Getter.Custom)]
        [@DecoratedField, SerializeField]
        Light _PrimaryLight;


#if !d_Crest_LegacyUnderwater
        [@Group("Rendering", Group.Style.Accordian)]
#else
        [HideInInspector]
#endif

        [Tooltip("When in the render pipeline the water is rendered.\n\nDefault is the old behaviour which is controlled by Unity.\n\nBefore Transparency has advantages like being compatible with soft particles, refractive shaders, and possibly third-party atmospheric fog.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        WaterInjectionPoint _InjectionPoint;

#if !d_Crest_LegacyUnderwater
        [@Space(10)]
#else
        [@Group("Rendering", Group.Style.Accordian)]
#endif

        [@Label("Color Texture")]
        [Tooltip("Whether to write the water surface color to the color/opaque texture.\n\nThis is likely only beneficial if the water injection point is before transparency, and there are shaders which need it (like refraction).")]
        [@Predicated(nameof(_InjectionPoint), inverted: false, nameof(WaterInjectionPoint.Default))]
        [@GenerateAPI(Getter.Custom)]
        [@DecoratedField, SerializeField]
        internal bool _WriteToColorTexture = true;

        [@Label("Depth Texture")]
        [Tooltip("Whether to write the water surface depth to the depth texture.\n\nThe water surface writes to the depth buffer, but Unity does not copy it to the depth texture for post-processing effects like Depth of Field (or refraction). This will copy the depth buffer to the depth texture.\n\nIf the water injection point is in the transparent pass, be wary that it will include all transparent objects that write to depth. Furthermore, other third parties may already be doing this, and we do not check whether it is necessary to copy or not.\n\nThis feature has a considerable overhead if using the built-in render pipeline, as it requires rendering the surface depth another time.")]
        [@Predicated(nameof(_Surface) + "." + nameof(SurfaceRenderer._Enabled))]
        [@GenerateAPI(Getter.Custom)]
        [@DecoratedField, SerializeField]
        internal bool _WriteToDepthTexture = true;

        [@Label("Motion Vectors")]
        [Tooltip("Whether to enable motion vector support.")]
#if !UNITY_6000_0_OR_NEWER
        [@Predicated(RenderPipeline.Universal, inverted: true, hide: true)]
#endif
        [@GenerateAPI(Getter.Custom)]
        [@DecoratedField, SerializeField]
        internal bool _WriteMotionVectors = true;

        [@Space(10)]

        [Tooltip("Whether to override the automatic detection of framebuffer HDR rendering (BIRP only).\n\nRendering using HDR formats is optional, but there is no way for us to determine if HDR rendering is enabled in the Graphics Settings. We make an educated based on which platform is the target. If you see rendering issues, try disabling this.\n\n This has nothing to do with having an HDR monitor.")]
        [@Predicated(RenderPipeline.Legacy, hide: true)]
        [@GenerateAPI]
        [@InlineToggle, SerializeField]
        bool _OverrideRenderHDR;

        [Tooltip("Force HDR format usage (BIRP only).\n\nIf enabled, we assume the framebuffer is an HDR format, otherwise an LDR format.")]
        [@Predicated(RenderPipeline.Legacy, hide: true)]
        [@Predicated(nameof(_OverrideRenderHDR))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _RenderHDR = true;


        [@Group(isCustomFoldout: true)]

        [Tooltip("The water surface renderer.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField(isCustomFoldout: true), SerializeReference]
        SurfaceRenderer _Surface = new();


        [@Group("Level of Detail", Group.Style.Accordian)]

        [@Label("Scale")]
        [Tooltip("The scale the water can be (infinity for no maximum).\n\nWater is scaled horizontally with viewer height, to keep the meshing suitable for elevated viewpoints. This sets the minimum and maximum the water will be scaled. Low minimum values give lots of detail, but will limit the horizontal extents of the water detail. Increasing the minimum value can be a great performance saving for mobile as it will reduce draw calls.")]
        [@Range(0.25f, 256f, Range.Clamp.Minimum, delayed: false)]
        [@GenerateAPI]
        [SerializeField]
        Vector2 _ScaleRange = new(4f, 256f);

        [Tooltip("Drops the height for maximum water detail based on waves.\n\nThis means if there are big waves, max detail level is reached at a lower height, which can help visual range when there are very large waves and camera is at sea level.")]
        [@Range(0f, 1f)]
        [@GenerateAPI]
        [SerializeField]
        float _DropDetailHeightBasedOnWaves = 0.2f;

        [@Label("Levels")]
        [Tooltip("Number of levels of details (chunks, scales etc) to generate.\n\nThe horizontal range of the water surface doubles for each added LOD, while GPU processing time increases linearly. The higher the number, the further out detail will be. Furthermore, the higher the count, the more larger wavelengths can be filtering in queries.")]
        [@Range(2, Lod.k_MaximumSlices)]
        [@GenerateAPI(name: "LodLevels")]
        [SerializeField]
        int _Slices = 9;

        [@Label("Resolution")]
        [Tooltip("The resolution of the various water LOD data.\n\nThis includes mesh density, displacement textures, foam data, dynamic wave simulation, etc. Sets the 'detail' present in the water - larger values give more detail at increased run-time expense. This value can be overriden per LOD in their respective settings except for Animated Waves which is tied to this value.")]
        [@Range(80, 1024, Range.Clamp.Minimum, step: 16, delayed: true)]
        [@Maximum(Constants.k_MaximumTextureResolution)]
        [@WarnIfAbove(1024)]
        [@GenerateAPI(name: "LodResolution")]
        [SerializeField]
        int _Resolution = 384;

        [Tooltip("How much of the water shape gets tessellated by geometry.\n\nFor example, if set to four, every geometry quad will span 4x4 LOD data texels. a value of 2 will generate one vert per 2x2 LOD data texels. A value of 1 means a vert is generated for every LOD data texel. Larger values give lower fidelity surface shape with higher performance.")]
        [@Delayed]
        [@GenerateAPI]
        [SerializeField]
        internal int _GeometryDownSampleFactor = 2;

        [Tooltip("Applied to the extents' far vertices to make them larger.\n\nIncrease if the extents do not reach the horizon or you see the underwater effect at the horizon.")]
        [@Delayed]
        [@GenerateAPI]
        [SerializeField]
        internal float _ExtentsSizeMultiplier = 100f;

        [@Heading("Center of Detail")]

        [Tooltip("The viewpoint which drives the water detail - the center of the LOD system.\n\nSetting this is optional. Defaults to the camera.")]
        [@GenerateAPI(Getter.Custom)]
        [@DecoratedField, SerializeField]
        Transform _Viewpoint;

        [@Label("Displacement Correction")]
        [Tooltip("Keep the center of detail from drifting from the viewpoint.\n\nLarge horizontal displacement can displace the center of detail. This uses queries to keep the center of detail aligned.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _CenterOfDetailDisplacementCorrection = true;

        [Tooltip("Also checks terrain height when determining the scale.\n\nThe scale is changed based on the viewer's height above the water surface. This can be a problem with varied water level, as the viewer may not be directly over the higher water level leading to a height difference, and thus incorrect scale.")]
        [Predicated(nameof(_Viewpoint), inverted: true)]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _SampleTerrainHeightForScale = true;

        [Tooltip("Forces smoothing for scale changes.\n\nWhen water level varies, smoothing scale change can prevent pops when the viewer's height above water sharply changes. Smoothing is disabled when terrain sampling is enabled or the water level simulation is disabled.")]
        [Predicated(nameof(_Viewpoint), inverted: true)]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        bool _ForceScaleChangeSmoothing;

        [Tooltip("The distance threshold for when the viewer has considered to have teleported.\n\nThis is used to prevent popping, and for prewarming simulations. Threshold is in Unity units.")]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _TeleportThreshold = 100f;


        [@Group("Simulations", Group.Style.Accordian)]

        [@Label("Animated Waves")]
        [Tooltip("All waves (including Dynamic Waves) are written to this simulation.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal AnimatedWavesLod _AnimatedWavesLod = new();

        [@Label("Water Depth")]
        [Tooltip("Water depth information used for shallow water, shoreline foam, wave attenuation, among others.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal DepthLod _DepthLod = new();

        [@Label("Water Level")]
        [Tooltip("Varying water level to support water bodies at different heights and rivers to run down slopes.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal LevelLod _LevelLod = new();

        [@Label("Foam")]
        [Tooltip("Simulation of foam created in choppy water and dissipating over time.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal FoamLod _FoamLod = new();

        [@Label("Dynamic Waves")]
        [Tooltip("Dynamic waves generated from interactions with objects such as boats.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal DynamicWavesLod _DynamicWavesLod = new();

        [@Label("Flow")]
        [Tooltip("Horizontal motion of water body, akin to water currents.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal FlowLod _FlowLod = new();

        [@Label("Shadows")]
        [Tooltip("Shadow information used for lighting water.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal ShadowLod _ShadowLod = new();

        [@Label("Absorption")]
        [Tooltip("Absorption information - gives color to water.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal AbsorptionLod _AbsorptionLod = new();

        [@Label("Scattering")]
        [Tooltip("Scattering information - gives color to water.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal ScatteringLod _ScatteringLod = new();

        [@Label("Surface Clipping")]
        [Tooltip("Clip surface information for clipping the water surface.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal ClipLod _ClipLod = new();

        [@Label("Albedo / Decals")]
        [Tooltip("Albedo - a colour layer composited onto the water surface.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField, SerializeReference]
        internal AlbedoLod _AlbedoLod = new();


        [@Group(isCustomFoldout: true)]

        [Tooltip("The reflection renderer.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField(isCustomFoldout: true), SerializeReference]
        internal WaterReflections _Reflections = new();


        [@Group(isCustomFoldout: true)]

        [Tooltip("The underwater renderer.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField(isCustomFoldout: true), SerializeReference]
        internal UnderwaterRenderer _Underwater = new();


        [@Group(isCustomFoldout: true)]

        [Tooltip("The meniscus module.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField(isCustomFoldout: true), SerializeReference]
        internal Meniscus _Meniscus = new();


#if !d_CrestPortals
        // Hide if package is not present. Fallback to dummy script.
        [HideInInspector]
#endif

        [@Group(isCustomFoldout: true)]

        [Tooltip("The portal renderer.")]
        [@GenerateAPI(Setter.None)]
        [@DecoratedField(isCustomFoldout: true), SerializeReference]
        internal Portals.PortalRenderer _Portals = new();


        [@Group("Edit Mode", Group.Style.Accordian)]

#pragma warning disable 414
        [@DecoratedField, SerializeField]
        internal bool _ShowWaterProxyPlane;

        [Tooltip("Sets the update rate of the water system when in edit mode.\n\nCan be reduced to save power.")]
        [@Range(0f, 120f, Range.Clamp.Minimum)]
        [SerializeField]
        float _EditModeFrameRate = 30f;

        [Tooltip("Move water with Scene view camera if Scene window is focused.")]
        [@Predicated(nameof(_ShowWaterProxyPlane), true)]
        [@DecoratedField, SerializeField]
        internal bool _FollowSceneCamera = true;

        [Tooltip("Whether height queries are enabled in edit mode.")]
        [@DecoratedField, SerializeField]
        internal bool _HeightQueries = true;
#pragma warning restore 414


        [@Group("Debug", isCustomFoldout: true)]

        [@DecoratedField(isCustomFoldout: true), SerializeField]
        internal DebugFields _Debug = new();

        [System.Serializable]
        internal sealed class DebugFields
        {
            [@Space(10)]

            [Tooltip("Attach debug GUI that adds some controls and allows to visualize the water data.")]
            [@DecoratedField, SerializeField]
            public bool _AttachDebugGUI;

            [Tooltip("Show hidden objects like water chunks in the hierarchy.")]
            [@DecoratedField, SerializeField]
            public bool _ShowHiddenObjects;

#if !CREST_DEBUG
            [HideInInspector]
#endif
            [Tooltip("Water will not move with viewpoint.")]
            [@DecoratedField, SerializeField]
            public bool _DisableFollowViewpoint;

            [Tooltip("Resources are normally released in OnDestroy (except in edit mode) which avoids expensive rebuilds when toggling this component. This option moves it to OnDisable. If you need this active then please report to us.")]
            [@DecoratedField, SerializeField]
            public bool _DestroyResourcesInOnDisable;

#if CREST_DEBUG
            [@DecoratedField, SerializeField]
            public bool _DrawLodOutline;

            [@DecoratedField, SerializeField]
            public bool _ShowDebugInformation;
#endif

            [@Heading("Scale")]

#if !CREST_DEBUG
            [HideInInspector]
#endif
            [Tooltip("Water will not move with viewpoint.")]
            [@DecoratedField, SerializeField]
            public bool _LogScaleChange;

#if !CREST_DEBUG
            [HideInInspector]
#endif
            [Tooltip("Water will not move with viewpoint.")]
            [@DecoratedField, SerializeField]
            public bool _PauseOnScaleChange;

#if !CREST_DEBUG
            [HideInInspector]
#endif
            [Tooltip("Water will not move with viewpoint.")]
            [@DecoratedField, SerializeField]
            public bool _IgnoreWavesForScaleChange;

            [@Heading("Server")]

            [Tooltip("Emulate running on a client without a GPU. Equivalent to running standalone with -nographics argument.")]
            [@DecoratedField, SerializeField]
            public bool _ForceNoGraphics;
        }

        [SerializeField, HideInInspector]
        internal WaterResources _Resources;
    }
}
