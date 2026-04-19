// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEngine;
using WaveHarmonic.Crest.Attributes;
using WaveHarmonic.Crest.Editor;

namespace WaveHarmonic.Crest
{
    sealed class Embedded : DecoratedProperty
    {
        internal EmbeddedAssetEditor _Editor;
        public int BottomMargin { get; private set; }
        public string DefaultPropertyName { get; private set; }

        public Embedded(int margin = 0, string defaultPropertyName = null)
        {
            _Editor = new();
            BottomMargin = margin;
            DefaultPropertyName = defaultPropertyName;
        }

        internal override void OnGUI(Rect position, SerializedProperty property, GUIContent label, DecoratedDrawer drawer)
        {
            _Editor.DrawEditorCombo(this, label, drawer, property, "asset");
        }

        internal override bool NeedsControlRectangle(SerializedProperty property) => false;
    }
}
