// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// This time provider fixes the water time at a custom value which is usable for testing/debugging.
    /// </summary>
    [AddComponentMenu(Constants.k_MenuPrefixTime + "Custom Time Provider")]
    [@HelpURL("Manual/TimeProviders.html#supporting-pause")]
    public sealed partial class CustomTimeProvider : TimeProvider
    {
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _Version = 0;
#pragma warning restore 414

        [Tooltip("Freeze progression of time. Only works properly in Play mode.")]
        [@GenerateAPI]
        [SerializeField]
        bool _Paused = false;

        [Tooltip("Whether to override the water simulation time.")]
        [@GenerateAPI]
        [SerializeField]
        bool _OverrideTime = false;

        [Tooltip("The time override value.")]
        [@Predicated(nameof(_OverrideTime))]
        [@GenerateAPI(name: "TimeOverride")]
        [@DecoratedField, SerializeField]
        float _Time = 0f;

        [Tooltip("Whether to override the water simulation time.\n\nThis in particular affects dynamic elements of the simulation like the foam simulation and the ripple simulation.")]
        [@GenerateAPI]
        [SerializeField]
        bool _OverrideDeltaTime = false;

        [Tooltip("The delta time override value.")]
        [@Predicated(nameof(_OverrideDeltaTime))]
        [@GenerateAPI]
        [@DecoratedField, SerializeField]
        float _DeltaTime = 0f;


        readonly DefaultTimeProvider _DefaultTimeProvider = new();
        float _TimeInternal = 0f;
        bool _FirstUpdate = true;

        private protected override void Initialize()
        {
            base.Initialize();
            _FirstUpdate = true;
        }

        private protected override System.Action<WaterRenderer> OnUpdateMethod => OnUpdate;

        void OnUpdate(WaterRenderer water)
        {
            // Use default TP delta time to update our time, because this dt works
            // well in edit mode
            if (_FirstUpdate)
            {
                _TimeInternal = _DefaultTimeProvider.Time;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    _TimeInternal += _DefaultTimeProvider.Delta;
                }
#endif

                _FirstUpdate = false;
            }
            else if (!_Paused)
            {
                _TimeInternal += _DefaultTimeProvider.Delta;
            }
        }

        /// <inheritdoc/>
        public override float Time
        {
            get
            {
                if (!isActiveAndEnabled)
                {
                    return _DefaultTimeProvider.Time;
                }

                // Override means override
                if (_OverrideTime)
                {
                    return _Time;
                }

                // Otherwise use our accumulated time
                return _TimeInternal;
            }
        }

        // Either use override, or the default TP which works in edit mode
        /// <inheritdoc/>
        public override float Delta
        {
            get
            {
                if (!isActiveAndEnabled)
                {
                    return _DefaultTimeProvider.Delta;
                }

                if (_Paused)
                {
                    return 0f;
                }

                if (_OverrideDeltaTime)
                {
                    return _DeltaTime;
                }

                return _DefaultTimeProvider.Delta;
            }
        }
    }
}
