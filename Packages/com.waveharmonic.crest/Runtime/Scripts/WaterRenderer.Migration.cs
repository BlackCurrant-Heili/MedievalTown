// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest
{
    partial class WaterRenderer : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _Version = 1;
#pragma warning restore 414

        /// <inheritdoc/>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (_Version < 1)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                Surface._Layer = _Layer;
                Surface._Material = _Material;
                Surface._VolumeMaterial = _VolumeMaterial;
                Surface._ChunkTemplate = _ChunkTemplate;
                Surface._CastShadows = _CastShadows;
                Surface._WaterBodyCulling = _WaterBodyCulling;
                Surface._TimeSliceBoundsUpdateFrameCount = _TimeSliceBoundsUpdateFrameCount;
                Surface._AllowRenderQueueSorting = _AllowRenderQueueSorting;
                Surface._SurfaceSelfIntersectionFixMode = _SurfaceSelfIntersectionFixMode;
#pragma warning restore CS0618 // Type or member is obsolete

                _DepthLod._IncludeTerrainHeight = false;

                _Version = 1;
            }
        }

        /// <inheritdoc/>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {

        }
    }
}
