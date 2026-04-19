// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    interface IReportsHeight
    {
        bool ReportHeight(ref Rect bounds, ref float minimum, ref float maximum);
    }

    interface IReportsDisplacement
    {
        bool ReportDisplacement(ref Rect bounds, ref float horizontal, ref float vertical);
    }

    /// <summary>
    /// Sets shader parameters for each geometry tile/chunk.
    /// </summary>
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    [@ExecuteDuringEditMode]
    sealed class WaterChunkRenderer : ManagedBehaviour<WaterRenderer>
    {
        [SerializeField]
        internal bool _DrawRenderBounds = false;

        internal const string k_UpdateMeshBoundsMarker = "Crest.WaterChunkRenderer.UpdateMeshBounds";

        static readonly Unity.Profiling.ProfilerMarker s_UpdateMeshBoundsMarker = new(k_UpdateMeshBoundsMarker);

        internal Transform _Transform;
        internal Mesh _Mesh;
        public Renderer Rend { get; private set; }
        internal MaterialPropertyBlock _MaterialPropertyBlock;
        Matrix4x4 _CurrentObjectToWorld;
        Matrix4x4 _PreviousObjectToWorld;
        internal Material _MotionVectorMaterial;
        internal int _SortingOrder;
        internal int _SiblingIndex;

        internal Rect _UnexpandedBoundsXZ = new();
        public Rect UnexpandedBoundsXZ => _UnexpandedBoundsXZ;

        internal bool _Culled;
        internal bool _Visible;

        internal WaterRenderer _Water;

        public bool MaterialOverridden { get; set; }

        // We need to ensure that all water data has been bound for the mask to
        // render properly - this is something that needs to happen irrespective
        // of occlusion culling because we need the mask to render as a
        // contiguous surface.
        internal bool _WaterDataHasBeenBound = true;

        int _LodIndex = -1;

        public static List<IReportsHeight> HeightReporters { get; } = new();
        public static List<IReportsDisplacement> DisplacementReporters { get; } = new();

        // There is a 1-frame delay with Initialized in edit mode due to setting
        // enableInEditMode in EditorApplication.update. This only really affect this
        // component as it is instantiate via script, and is partial driven externally.
        // So instead, call this after instantiation.
        internal void Initialize(int index, Renderer renderer, Mesh mesh)
        {
            _LodIndex = index;
            Rend = renderer;
            _Mesh = mesh;
            _PreviousObjectToWorld = _CurrentObjectToWorld = transform.localToWorldMatrix;
            _Transform = transform;
        }

        private protected override void OnStart()
        {
            base.OnStart();

            UpdateMeshBounds();
        }

        internal void UpdateMeshBounds(WaterRenderer water, SurfaceRenderer surface)
        {
            _WaterDataHasBeenBound = false;

            var count = surface.TimeSliceBoundsUpdateFrameCount;

            // Time slice update to distribute the load.
            if (count <= 1 || !(_SiblingIndex % count != Time.frameCount % surface.Chunks.Count % count))
            {
                // This needs to be called on Update because the bounds depend on transform scale which can change. Also OnWillRenderObject depends on
                // the bounds being correct. This could however be called on scale change events, but would add slightly more complexity.
                UpdateMeshBounds();
            }
        }

        bool ShouldRender(bool culled)
        {
            // Is visible to camera.
            if (!_Visible)
            {
                return false;
            }

            // If including culling, is it culled.
            if (culled && _Culled)
            {
                return false;
            }

            return true;
        }

        internal void OnLateUpdate()
        {
            _PreviousObjectToWorld = _CurrentObjectToWorld;
            _CurrentObjectToWorld = _Transform.localToWorldMatrix;
        }

        internal void RenderMotionVectors(SurfaceRenderer surface, Camera camera)
        {
            if (!ShouldRender(culled: true))
            {
                return;
            }

            // RenderMesh will copy properties immediately, thus we need them bound.
            if (!_WaterDataHasBeenBound)
            {
                Bind();
            }

            var material = MaterialOverridden ? _MotionVectorMaterial : surface._MotionVectorMaterial;

            var parameters = new RenderParams(material)
            {
                motionVectorMode = MotionVectorGenerationMode.Object,
                material = material,
                matProps = _MaterialPropertyBlock,
                worldBounds = Rend.bounds,
                layer = surface.Layer,
                receiveShadows = false,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off,
                reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off,
                camera = camera,
            };

            Graphics.RenderMesh(parameters, _Mesh, 0, _CurrentObjectToWorld, _PreviousObjectToWorld);
        }

        void UpdateMeshBounds()
        {
            s_UpdateMeshBoundsMarker.Begin(this);

            var bounds = _Mesh.bounds;

            if (WaterBody.WaterBodies.Count > 0)
            {
                _UnexpandedBoundsXZ = ComputeBoundsXZ(_Transform, bounds);
            }

            bounds = ExpandBoundsForDisplacements(_Transform, bounds);

            Rend.localBounds = bounds;

            s_UpdateMeshBoundsMarker.End();
        }

        public static Rect ComputeBoundsXZ(Transform transform, Bounds bounds)
        {
            // Since chunks are axis-aligned it is safe to rotate the bounds.
            var center = transform.rotation * bounds.center * transform.lossyScale.x + transform.position;
            var size = transform.rotation * bounds.size * transform.lossyScale.x;
            // Rotation can make size negative.
            return new(0, 0, Mathf.Abs(size.x), Mathf.Abs(size.z))
            {
                center = center.XZ(),
            };
        }

        // Used by the water mask system if we need to render the water mask in situations
        // where the water itself doesn't need to be rendered or has otherwise been disabled
        internal void Bind()
        {
            _MaterialPropertyBlock = _Water.Surface._PerCascadeMPB.Current[_LodIndex];
            Rend.SetPropertyBlock(_MaterialPropertyBlock);

            _WaterDataHasBeenBound = true;
        }

        void OnDestroy()
        {
            Helpers.Destroy(_Mesh);
        }

        // Called when visible to a camera
        void OnWillRenderObject()
        {
            if (Rend == null)
            {
                return;
            }

            if (!MaterialOverridden && Rend.sharedMaterial != _Water.Surface.Material)
            {
                Rend.sharedMaterial = _Water.Surface.Material;
                _MotionVectorMaterial = _Water.Surface._MotionVectorMaterial;
            }

            if (!_WaterDataHasBeenBound)
            {
                Bind();
            }

            if (_DrawRenderBounds)
            {
                Rend.bounds.DebugDraw();
            }
        }

        // this is called every frame because the bounds are given in world space and depend on the transform scale, which
        // can change depending on view altitude
        public Bounds ExpandBoundsForDisplacements(Transform transform, Bounds bounds)
        {
            var extents = bounds.extents;
            var center = bounds.center;

            var scale = transform.lossyScale;
            var rotation = transform.rotation;

            var boundsPadding = _Water.MaximumHorizontalDisplacement;
            var expandXZ = boundsPadding / scale.x;
            var boundsY = _Water.MaximumVerticalDisplacement;

            // Extend the kinematic bounds slightly to give room for dynamic waves.
            if (_Water._DynamicWavesLod.Enabled)
            {
                boundsY += 5f;
            }

            // Extend bounds by global waves.
            extents.x += expandXZ;
            extents.y += boundsY;
            extents.z += expandXZ;

            // Get XZ bounds. Doing this manually bypasses updating render bounds call.
            Rect rect;
            {
                var p1 = transform.position;
                var p2 = rotation * new Vector3(center.x, 0f, center.z);
                var s1 = scale;
                var s2 = rotation * (extents.XNZ(0f) * 2f);

                rect = new(0, 0, Mathf.Abs(s1.x * s2.x), Mathf.Abs(s1.z * s2.z))
                {
                    center = new(p1.x + p2.x, p1.z + p2.z)
                };
            }

            // Extend bounds by local waves.
            {
                var totalHorizontal = 0f;
                var totalVertical = 0f;

                foreach (var reporter in DisplacementReporters)
                {
                    var horizontal = 0f;
                    var vertical = 0f;
                    if (reporter.ReportDisplacement(ref rect, ref horizontal, ref vertical))
                    {
                        totalHorizontal += horizontal;
                        totalVertical += vertical;
                    }
                }

                boundsPadding = totalHorizontal;
                expandXZ = boundsPadding / scale.x;
                boundsY = totalVertical;

                extents.x += expandXZ;
                extents.y += boundsY;
                extents.z += expandXZ;
            }

            // Expand and offset bounds by height.
            {
                var minimumWaterLevelBounds = 0f;
                var maximumWaterLevelBounds = 0f;

                foreach (var reporter in HeightReporters)
                {
                    var minimum = 0f;
                    var maximum = 0f;
                    if (reporter.ReportHeight(ref rect, ref minimum, ref maximum))
                    {
                        minimumWaterLevelBounds = Mathf.Max(minimumWaterLevelBounds, Mathf.Abs(Mathf.Min(minimum, _Water.SeaLevel) - _Water.SeaLevel));
                        maximumWaterLevelBounds = Mathf.Max(maximumWaterLevelBounds, Mathf.Abs(Mathf.Max(maximum, _Water.SeaLevel) - _Water.SeaLevel));
                    }
                }

                minimumWaterLevelBounds *= 0.5f;
                maximumWaterLevelBounds *= 0.5f;

                boundsY = minimumWaterLevelBounds + maximumWaterLevelBounds;
                extents.y += boundsY;
                bounds.extents = extents;

                var offset = maximumWaterLevelBounds - minimumWaterLevelBounds;
                center.y += offset;
                bounds.center = center;
            }

            return bounds;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitStatics()
        {
            HeightReporters.Clear();
            DisplacementReporters.Clear();
        }
    }

    static class BoundsHelper
    {
        internal static void DebugDraw(this Bounds b)
        {
            var xmin = b.min.x;
            var ymin = b.min.y;
            var zmin = b.min.z;
            var xmax = b.max.x;
            var ymax = b.max.y;
            var zmax = b.max.z;

            Debug.DrawLine(new(xmin, ymin, zmin), new(xmin, ymin, zmax));
            Debug.DrawLine(new(xmin, ymin, zmin), new(xmax, ymin, zmin));
            Debug.DrawLine(new(xmax, ymin, zmax), new(xmin, ymin, zmax));
            Debug.DrawLine(new(xmax, ymin, zmax), new(xmax, ymin, zmin));

            Debug.DrawLine(new(xmin, ymax, zmin), new(xmin, ymax, zmax));
            Debug.DrawLine(new(xmin, ymax, zmin), new(xmax, ymax, zmin));
            Debug.DrawLine(new(xmax, ymax, zmax), new(xmin, ymax, zmax));
            Debug.DrawLine(new(xmax, ymax, zmax), new(xmax, ymax, zmin));

            Debug.DrawLine(new(xmax, ymax, zmax), new(xmax, ymin, zmax));
            Debug.DrawLine(new(xmin, ymin, zmin), new(xmin, ymax, zmin));
            Debug.DrawLine(new(xmax, ymin, zmin), new(xmax, ymax, zmin));
            Debug.DrawLine(new(xmin, ymax, zmax), new(xmin, ymin, zmax));
        }
    }
}
