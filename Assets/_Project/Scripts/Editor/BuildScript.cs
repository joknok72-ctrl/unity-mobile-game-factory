// Real Life Sky — CI build entry point, compatible with game-ci/unity-builder@v4.
// The builder invokes:  -executeMethod BuildScript.Build -customBuildPath <path> -androidKeystoreName ... etc.
// (we set buildMethod: BuildScript.Build in the workflow). We honour the same command-line arguments as GameCI's
// default UnityBuilderAction so keystore signing / target SDK behave identically, and we make sure the scene
// list, URP settings and Android player settings are exactly what we want before building.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    const string ScenePath = "Assets/_Project/Scenes/RealSky.unity";

    static Dictionary<string, string> Args()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("-")) continue;
            string key = a[i].Substring(1);
            string val = (i + 1 < a.Length && !a[i + 1].StartsWith("-")) ? a[i + 1] : "";
            d[key] = val;
        }
        return d;
    }

    [MenuItem("RealLife/Build Android APK")]
    public static void Build()
    {
        var args = Args();
        string buildPath = args.TryGetValue("customBuildPath", out var bp) && !string.IsNullOrEmpty(bp) ? bp : "build/Android/RealLifeSky.apk";
        string buildTargetArg = args.TryGetValue("buildTarget", out var bt) ? bt : "Android";
        BuildTarget target = (BuildTarget)Enum.Parse(typeof(BuildTarget), buildTargetArg, true);

        Console.WriteLine($"[BuildScript] target={target} path={buildPath}");
        EnsureSceneExists();
        ApplyPlayerSettings(target, args);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = buildPath,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        var s = report.summary;
        Console.WriteLine($"[BuildScript] result={s.result} size={s.totalSize} errors={s.totalErrors} warnings={s.totalWarnings} time={s.totalTime}");
        foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception) Console.WriteLine($"[BuildScript][{step.name}] {m.content}");
        if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        EditorApplication.Exit(0);
    }

    static void ApplyPlayerSettings(BuildTarget target, Dictionary<string, string> args)
    {
        PlayerSettings.companyName = "RealLife Studio";
        PlayerSettings.productName = "Real Life Sky";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.reallife.sky");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.Android.startInFullscreen = true;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
        PlayerSettings.gcIncremental = true;

        if (args.TryGetValue("androidVersionCode", out var vc) && int.TryParse(vc, out int code)) PlayerSettings.Android.bundleVersionCode = code;
        else PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);
        if (args.TryGetValue("buildVersion", out var bv) && !string.IsNullOrEmpty(bv)) PlayerSettings.bundleVersion = bv;

        // GameCI passes either an enum name ("AndroidApiLevel34") or a bare integer ("34").
        if (args.TryGetValue("androidTargetSdkVersion", out var tsdk) && !string.IsNullOrEmpty(tsdk))
        {
            if (int.TryParse(tsdk, out int tsdkI) && tsdkI > 0) PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)tsdkI;
            else if (Enum.TryParse(tsdk, true, out AndroidSdkVersions tsdkE)) PlayerSettings.Android.targetSdkVersion = tsdkE;
        }

        // Keystore (GameCI passes these when secrets exist; otherwise Unity's debug keystore signs the APK — still installable)
        // Values come from -args (GameCI style) or from environment variables (keeps passwords out of the command line).
        string ks = Arg(args, "androidKeystoreName", "ANDROID_KEYSTORE_PATH");
        if (!string.IsNullOrEmpty(ks) && File.Exists(ks))
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ks;
            PlayerSettings.Android.keystorePass = Arg(args, "androidKeystorePass", "ANDROID_KEYSTORE_PASS");
            PlayerSettings.Android.keyaliasName = Arg(args, "androidKeyaliasName", "ANDROID_KEYALIAS_NAME");
            PlayerSettings.Android.keyaliasPass = Arg(args, "androidKeyaliasPass", "ANDROID_KEYALIAS_PASS");
            Console.WriteLine("[BuildScript] custom keystore applied");
        }
        else
        {
            PlayerSettings.Android.useCustomKeystore = false;
            Console.WriteLine("[BuildScript] debug keystore");
        }
        EditorUserBuildSettings.buildAppBundle = args.TryGetValue("androidExportType", out var et) && et == "androidAppBundle";
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        AssetDatabase.SaveAssets();
    }

    static string Arg(Dictionary<string, string> args, string key, string envName)
    {
        if (args.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return Environment.GetEnvironmentVariable(envName) ?? "";
    }

    /// <summary>Guarantee the scene exists and is in the build list (it only needs the Bootstrap object).</summary>
    static void EnsureSceneExists()
    {
        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var go = new GameObject("Bootstrap");
            go.AddComponent<RealLife.Sky.Bootstrap>();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, ScenePath);
            Console.WriteLine("[BuildScript] scene created");
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
