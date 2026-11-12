#if DAO_HAS_META
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace DAO.EditorTools
{
    /// <summary>
    /// Menu "DAO > Configurar projeto para Quest": deixa o projeto pronto para gerar o .apk do Meta Quest 2/3/3S.
    /// Android + IL2CPP + ARM64, Vulkan, Linear, XR Plug-in Management com OpenXR + Meta XR Feature
    /// (Android e PC para o Link), URP com ajustes de desempenho do Quest, dispositivos-alvo no OVRProjectConfig
    /// e, no fim, o "Fix All" do Project Setup Tool da Meta.
    /// </summary>
    public static class DaoMetaSetup
    {
        const string SettingsDir = "Assets/DAO/Settings";
        const string OculusLoader = "Unity.XR.Oculus.OculusLoader";
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("DAO/Configurar projeto para Quest (Meta XR)", priority = 0)]
        public static void ConfigureMenu()
        {
            Configure();
            RunMetaProjectSetupFixAll(() =>
                EditorUtility.DisplayDialog("DAO", "Projeto configurado para Meta Quest (OpenXR).\nAgora rode DAO > Montar cena das 3 salas.", "OK"));
        }

        /// <summary>Linha de comando (sem -quit): configura, espera o Fix All assíncrono da Meta e sai.</summary>
        public static void ConfigureCLI()
        {
            try
            {
                Configure();
                RunMetaProjectSetupFixAll(() => EditorApplication.Exit(0));
            }
            catch (Exception e) { Debug.LogError("[DAO-SETUP] FALHOU: " + e); EditorApplication.Exit(1); }
        }

        /// <summary>
        /// Roda o "Fix All" do Meta Project Setup Tool (Android e PC) e chama onDone quando terminar.
        /// O Fix All é assíncrono, então espera pelo EditorApplication.update.
        /// </summary>
        static void RunMetaProjectSetupFixAll(Action onDone)
        {
            var pending = new System.Collections.Generic.Queue<BuildTargetGroup>(new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone });
            System.Threading.Tasks.Task current = null;
            double started = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (current != null && !current.IsCompleted && EditorApplication.timeSinceStartup - started < 180) return;
                if (current != null)
                    Debug.Log($"[DAO-SETUP] Meta Project Setup Fix All: {(current.IsFaulted ? "falhou: " + current.Exception?.GetBaseException().Message : current.IsCompleted ? "ok" : "tempo esgotado")}");
                if (pending.Count == 0)
                {
                    EditorApplication.update -= tick;
                    AssetDatabase.SaveAssets();
                    onDone?.Invoke();
                    return;
                }
                var group = pending.Dequeue();
                Debug.Log($"[DAO-SETUP] Meta Project Setup Fix All ({group})...");
                started = EditorApplication.timeSinceStartup;
                current = OVRProjectSetup.FixAllAsync(group);
            };
            EditorApplication.update += tick;
        }

        /// <summary>Gera Builds/CasaDaDAO.apk (instale com o Meta Quest Developer Hub ou "adb install -r").</summary>
        [MenuItem("DAO/Gerar APK para o Quest", priority = 2)]
        public static void BuildApk()
        {
            Directory.CreateDirectory("Builds");
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/CasaDaDAO.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            });
            var s = report.summary;
            Debug.Log($"[DAO-APK] {s.result}: {s.outputPath} ({s.totalSize / (1024f * 1024f):F1} MB) em {s.totalTime.TotalMinutes:F1} min, {s.totalErrors} erro(s).");
            if (s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Build falhou: " + s.result);
        }

        /// <summary>Abre a cena principal (usado também ao abrir o projeto por linha de comando).</summary>
        [MenuItem("DAO/Abrir cena principal", priority = 1)]
        public static void OpenMainScene()
        {
            const string path = "Assets/DAO/Scenes/DAO_3Salas.unity";
            if (File.Exists(path)) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
        }

        public static void BuildApkCLI()
        {
            try { BuildApk(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogError("[DAO-APK] FALHOU: " + e.Message); EditorApplication.Exit(1); }
        }

        public static void Configure()
        {
            ConfigureAndroid();
            ConfigureXR();
            ConfigureURP();
            ConfigureMetaProject();
            ReimportModels();
            AssetDatabase.SaveAssets();
            Debug.Log("[DAO-SETUP] Projeto configurado para Meta Quest.");
        }

        static void ConfigureAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            PlayerSettings.companyName = "Equipe DAO";
            PlayerSettings.productName = "Casa da DAO";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.equipedao.casadadao");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            Debug.Log("[DAO-SETUP] Android: IL2CPP, ARM64, API 32+, Vulkan, Linear, ASTC.");
        }

        static void ConfigureXR()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
                if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
                var settings = perTarget.SettingsForBuildTarget(group);
                settings.InitManagerOnStart = true;
                var manager = settings.Manager;
#if DAO_HAS_OPENXR
                // A Meta recomenda OpenXR na Unity 6; o plugin Oculus XR está obsoleto.
                XRPackageMetadataStore.RemoveLoader(manager, OculusLoader, group);
                bool ok = XRPackageMetadataStore.AssignLoader(manager, OpenXRLoader, group);
                string loaderName = "OpenXR";
#else
                bool ok = XRPackageMetadataStore.AssignLoader(manager, OculusLoader, group);
                string loaderName = "Oculus";
#endif
                // Remove referências a loaders de pacotes que não existem mais.
                var so = new SerializedObject(manager);
                var loaders = so.FindProperty("m_Loaders");
                if (loaders != null)
                    for (int i = loaders.arraySize - 1; i >= 0; i--)
                        if (loaders.GetArrayElementAtIndex(i).objectReferenceValue == null) loaders.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                EditorUtility.SetDirty(manager);
                Debug.Log($"[DAO-SETUP] XR {group}: loader {loaderName} {(ok ? "ativo" : "JÁ ATIVO/FALHOU")}; loaders = {string.Join(", ", manager.activeLoaders.Where(l => l != null).Select(l => l.GetType().Name))}");
#if DAO_HAS_OPENXR
                ConfigureOpenXRFeatures(group);
#endif
            }
            EditorUtility.SetDirty(perTarget);
        }

#if DAO_HAS_OPENXR
        /// <summary>
        /// Liga a Meta XR Feature (ponte oficial da Meta para OpenXR: mãos, controles, passthrough etc.) e os
        /// perfis de interação dos controles do Quest; usa Multiview/Single Pass Instanced.
        /// </summary>
        static void ConfigureOpenXRFeatures(BuildTargetGroup group)
        {
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group); // registra as features dos pacotes
            var ox = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (ox == null) { Debug.LogWarning($"[DAO-SETUP] OpenXR settings de {group} não encontrados."); return; }
            string[] enable =
            {
                "MetaXRFeature",                       // Meta XR Feature (com.meta.xr.sdk.core)
                "OculusTouchControllerProfile",        // Quest 2 / Quest 3S
                "MetaQuestTouchPlusControllerProfile", // Quest 3
                "MetaQuestTouchProControllerProfile",  // Touch Pro
            };
            foreach (var f in ox.GetFeatures())
            {
                if (f == null) continue;
                if (enable.Contains(f.GetType().Name) && !f.enabled) f.enabled = true;
                EditorUtility.SetDirty(f);
            }
            ox.renderMode = UnityEngine.XR.OpenXR.OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(ox);
            Debug.Log($"[DAO-SETUP] OpenXR {group}: ativas = {string.Join(", ", ox.GetFeatures().Where(f => f != null && f.enabled).Select(f => f.GetType().Name))}");
        }
#endif

        static void ConfigureURP()
        {
            Directory.CreateDirectory(SettingsDir);
            string rendererPath = SettingsDir + "/DAO_URP_Renderer.asset";
            string assetPath = SettingsDir + "/DAO_URP_Quest.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, assetPath);
            }

            // Ajustes recomendados para Quest: MSAA 4x, sem HDR, sem depth/opaque texture, sem sombras em tempo real.
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.renderScale = 1f;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            var so = new SerializedObject(urp);
            SetBool(so, "m_MainLightShadowsSupported", false);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 2);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log("[DAO-SETUP] URP ativo (MSAA 4x, sem HDR, sem sombras).");
        }

        static void SetBool(SerializedObject so, string prop, bool v) { var p = so.FindProperty(prop); if (p != null) p.boolValue = v; }
        static void SetInt(SerializedObject so, string prop, int v) { var p = so.FindProperty(prop); if (p != null) p.intValue = v; }

        static void ConfigureMetaProject()
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.targetDeviceTypes = new System.Collections.Generic.List<OVRProjectConfig.DeviceType>
            {
                OVRProjectConfig.DeviceType.Quest2, OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S
            };
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            OVRProjectConfig.CommitProjectConfig(cfg);
            Debug.Log("[DAO-SETUP] Meta: Quest 2, Quest 3 e Quest 3S; controles e mãos.");
        }

        /// <summary>Reimporta os modelos para os materiais usarem os shaders do URP.</summary>
        static void ReimportModels()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/DAO/Models" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        }
    }
}
#endif
