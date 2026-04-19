// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Editor.Settings;

namespace WaveHarmonic.Crest.Editor
{
    static class ShaderSettingsGenerator
    {
        [DidReloadScripts]
        static void OnReloadScripts()
        {
            EditorApplication.update -= GenerateAfterReloadScripts;
            EditorApplication.update += GenerateAfterReloadScripts;
        }

        static async void GenerateAfterReloadScripts()
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            EditorApplication.update -= GenerateAfterReloadScripts;

            // Generate HLSL from C#. Only targets WaveHarmonic.Crest assemblies.
            await ShaderGeneratorUtility.GenerateAll();
            AssetDatabase.Refresh();
        }

        internal static void Generate()
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            // Could not ShaderGeneratorUtility.GenerateAll to work without recompiling…
            CompilationPipeline.RequestScriptCompilation();
        }

        sealed class AssetPostProcessor : AssetPostprocessor
        {
            const string k_SettingsPath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest.hlsl";

            static async void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] movedTo, string[] movedFrom, bool domainReload)
            {
                // Unused.
                _ = deleted; _ = movedTo; _ = movedFrom; _ = domainReload;

                if (EditorApplication.isCompiling)
                {
#if CREST_DEBUG
                    if (imported.Contains(k_SettingsPath))
                    {
                        UnityEngine.Debug.Log($"Crest: Settings.Crest.hlsl changed during compilation!");
                    }
#endif
                    return;
                }

                if (EditorApplication.isUpdating)
                {
#if CREST_DEBUG
                    if (imported.Contains(k_SettingsPath))
                    {
                        UnityEngine.Debug.Log($"Crest: Settings.Crest.hlsl changed during asset database update!");
                    }
#endif
                    return;
                }

                // Regenerate if file changed like re-importing.
                if (imported.Contains(k_SettingsPath))
                {
#if CREST_DEBUG
                    UnityEngine.Debug.Log($"Crest: Settings.Crest.hlsl changed!");
#endif
                    // Generate HLSL from C#. Only targets WaveHarmonic.Crest assemblies.
                    await ShaderGeneratorUtility.GenerateAll();
                    AssetDatabase.Refresh();
                }
            }
        }
    }

    [GenerateHLSL(sourcePath = "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings.Crest")]
    sealed class ShaderSettings
    {
        // These two are here for compute shaders.
        public static int s_CrestPackageHDRP = 0
#if d_UnityHDRP
            + 1
#endif
            ;

        public static int s_CrestPackageURP = 0
#if d_UnityURP
            + 1
#endif
            ;

        public static int s_CrestPortals =
#if d_CrestPortals
            1
#else
            0
#endif
        ;

        public static int s_CrestShiftingOrigin =
#if d_WaveHarmonic_Crest_ShiftingOrigin
            1
#else
            0
#endif
        ;

        public static int s_CrestFullPrecisionDisplacement = ProjectSettings.Instance.FullPrecisionDisplacementOnHalfPrecisionPlatforms ? 1 : 0;

        public static int s_CrestDiscardAtmosphericScattering = ProjectSettings.Instance.RenderAtmosphericScatteringWhenUnderWater ? 0 : 1;

        public static int s_CrestLegacyUnderwater = ProjectSettings.Instance.LegacyUnderwater ? 1 : 0;
    }
}
