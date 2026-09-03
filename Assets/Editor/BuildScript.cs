// Unity Mobile Game Factory — generic CI build entry point (Android).
// Called by .github/workflows/build-android.yml through:
//   -executeMethod BuildScript.Build -customBuildPath <apk> -androidVersionCode <n> -buildVersion <x.y.z>
//   -androidTargetSdkVersion 34 -androidExportType androidPackage|androidAppBundle
// Everything game-specific (product name, package id, scenes, orientation) is read from the PROJECT itself
// (ProjectSettings + EditorBuildSettings) or from an optional `build.json` at the repo root:
//   { "productName": "My Game", "companyName": "My Studio", "packageName": "com.mystudio.mygame",
//     "orientation": "Portrait|Landscape|AutoRotation", "scenes": ["Assets/Scenes/Main.unity"] }
// This file must NOT be edited per game — put game settings in build.json / ProjectSettings instead.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    [Serializable]
    class BuildConfig
    {
        public string productName;
        public string companyName;
        public string packageName;
        public string orientation;
        public string[] scenes;
        public bool armv7 = true;   // include 32-bit ARM as well as ARM64
        public int minSdk = 24;     // Android 7.0
    }

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

    static BuildConfig LoadConfig()
    {
        var cfg = new BuildConfig();
        if (File.Exists("build.json"))
        {
            try { JsonUtility.FromJsonOverwrite(File.ReadAllText("build.json"), cfg); Console.WriteLine("[BuildScript] build.json loaded"); }
            catch (Exception e) { Console.WriteLine("[BuildScript] build.json invalid: " + e.Message); }
        }
        return cfg;
    }

    static string[] ResolveScenes(BuildConfig cfg)
    {
        if (cfg.scenes != null && cfg.scenes.Length > 0)
        {
            var ok = cfg.scenes.Where(File.Exists).ToArray();
            if (ok.Length > 0) return ok;
            Console.WriteLine("[BuildScript] scenes in build.json not found on disk, falling back");
        }
        var enabled = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
        if (enabled.Length > 0) return enabled;
        // Last resort: every scene under Assets, sorted (a scene named *Main*/*Boot* first)
        var all = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => Regex.IsMatch(Path.GetFileName(p), "boot|main|start|menu", RegexOptions.IgnoreCase) ? 0 : 1).ThenBy(p => p).ToArray();
        return all;
    }

    static string Slug(string s) => Regex.Replace(s ?? "Game", @"[^A-Za-z0-9]+", "").Trim('_');

    [MenuItem("Build/Android APK")]
    public static void Build()
    {
        var args = Args();
        var cfg = LoadConfig();
        string buildTargetArg = args.TryGetValue("buildTarget", out var bt) && !string.IsNullOrEmpty(bt) ? bt : "Android";
        BuildTarget target = (BuildTarget)Enum.Parse(typeof(BuildTarget), buildTargetArg, true);

        ApplyPlayerSettings(cfg, args);
        string[] scenes = ResolveScenes(cfg);
        if (scenes.Length == 0)
        {
            Console.WriteLine("[BuildScript] ERROR: no scene found. Add a scene under Assets/ and list it in build.json or File > Build Settings.");
            EditorApplication.Exit(1); return;
        }
        EditorBuildSettings.scenes = scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();

        bool aab = args.TryGetValue("androidExportType", out var et) && et == "androidAppBundle";
        string ext = aab ? ".aab" : ".apk";
        string buildPath = args.TryGetValue("customBuildPath", out var bp) && !string.IsNullOrEmpty(bp) ? bp : $"build/Android/{Slug(PlayerSettings.productName)}{ext}";
        if (!buildPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) buildPath = Path.ChangeExtension(buildPath, ext);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(buildPath)));

        Console.WriteLine($"[BuildScript] product='{PlayerSettings.productName}' id={PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)} version={PlayerSettings.bundleVersion} code={PlayerSettings.Android.bundleVersionCode}");
        Console.WriteLine($"[BuildScript] scenes: {string.Join(", ", scenes)}");
        Console.WriteLine($"[BuildScript] target={target} path={buildPath}");

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
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
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }

    static void ApplyPlayerSettings(BuildConfig cfg, Dictionary<string, string> args)
    {
        if (!string.IsNullOrEmpty(cfg.productName)) PlayerSettings.productName = cfg.productName;
        if (!string.IsNullOrEmpty(cfg.companyName)) PlayerSettings.companyName = cfg.companyName;
        if (!string.IsNullOrEmpty(cfg.packageName)) PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, cfg.packageName);
        else
        {
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (string.IsNullOrEmpty(id) || id.StartsWith("com.DefaultCompany") || id == "com.unity.template.mobile")
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, $"com.{Slug(PlayerSettings.companyName).ToLowerInvariant()}.{Slug(PlayerSettings.productName).ToLowerInvariant()}");
        }
        if (!string.IsNullOrEmpty(cfg.orientation) && Enum.TryParse(cfg.orientation, true, out UIOrientation o)) PlayerSettings.defaultInterfaceOrientation = o;

        // Mobile-only, modern, safe defaults (identical for every game built by this repo)
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = cfg.armv7 ? AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7 : AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)Math.Max(23, cfg.minSdk);
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.Android.startInFullscreen = true;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
        PlayerSettings.gcIncremental = true;

        if (args.TryGetValue("androidVersionCode", out var vc) && int.TryParse(vc, out int code)) PlayerSettings.Android.bundleVersionCode = code;
        else PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);
        if (args.TryGetValue("buildVersion", out var bv) && !string.IsNullOrEmpty(bv)) PlayerSettings.bundleVersion = bv;

        if (args.TryGetValue("androidTargetSdkVersion", out var tsdk) && !string.IsNullOrEmpty(tsdk))
        {
            if (int.TryParse(tsdk, out int tsdkI) && tsdkI > 0) PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)tsdkI;
            else if (Enum.TryParse(tsdk, true, out AndroidSdkVersions tsdkE)) PlayerSettings.Android.targetSdkVersion = tsdkE;
        }

        // Signing: keystore path/passwords come from environment variables set by the workflow (never from the repo).
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
            Console.WriteLine("[BuildScript] debug keystore (installable, not for Play Store)");
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
}
