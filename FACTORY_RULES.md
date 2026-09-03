# FACTORY RULES — read this FIRST before adding a game to this repository

This repository is a **Unity Mobile Game Factory**: a pre-wired Unity project + GitHub Actions pipeline that
turns every push to `main` into a signed Android APK published under **Releases**. The user has **no PC** —
everything (code, assets, scenes, .meta files, settings) is written by an AI agent in a sandbox and pushed.
Nothing is ever opened in the Unity Editor. Therefore the rules below are not style preferences: each one
exists because breaking it produced a failed build or a broken APK.

## 0. Fixed stack (do not change unless the user explicitly asks)
| Item | Value | Where |
|---|---|---|
| Unity | **6000.0.82f1 LTS** | `ProjectSettings/ProjectVersion.txt` |
| Render pipeline | **URP 17.0.4** (`Assets/Settings/Mobile_RPAsset.asset` + `Mobile_Renderer.asset`) | `Packages/manifest.json`, `GraphicsSettings.asset`, `QualitySettings.asset` |
| UI | uGUI 2.0.0 (`com.unity.ugui`) — TextMeshPro is inside uGUI | `Packages/manifest.json` |
| Input | **Legacy `UnityEngine.Input` only** (`activeInputHandler: 0`). Do NOT add `com.unity.inputsystem`. "Both" is rejected on Android; new Input System alone requires Editor-side setup. | `ProjectSettings.asset` |
| Scripting | IL2CPP, ARM64 + ARMv7, .NET Standard 2.1 (`apiCompatibilityLevel: 6`), C# 9 | `BuildScript.cs` |
| Android | minSdk 24, targetSdk 34, Vulkan + GLES3, ASTC, Linear color space | `BuildScript.cs` |
| CI | Buildalon actions (`unity-setup@v2` with `hub-version: '3.14.3'`, `activate-unity-license@v2` Personal, `unity-action@v3`) | `.github/workflows/build-android.yml` |
| Secrets already on the repo | `UNITY_EMAIL`, `UNITY_PASSWORD`, `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS` | GitHub → Settings → Secrets |

The workflow, `Assets/Editor/BuildScript.cs` and `Assets/Settings/*` are **infrastructure**. Do not edit them per game.
Per-game configuration lives in **`build.json`** (productName, companyName, packageName, orientation, scenes).

## 1. Where the game goes
```
Assets/
  Scenes/            ← at least one .unity scene, listed in build.json "scenes" AND ProjectSettings/EditorBuildSettings.asset
  Scripts/           ← runtime C# (optionally with an .asmdef; if you add one, reference Unity.RenderPipelines.Universal.Runtime, Unity.RenderPipelines.Core.Runtime, UnityEngine.UI as needed)
  Resources/         ← anything loaded at runtime by name (materials, shaders, textures, data)
  Editor/            ← BuildScript.cs (+ Factory.Editor.asmdef). Editor-only code MUST live here or in an Editor asmdef
  Settings/          ← URP assets (infrastructure)
  Plugins/Android/   ← optional *.androidlib with AndroidManifest.xml for permissions
build.json           ← per-game metadata read by BuildScript
```
Delete `Assets/Scripts/TemplateBootstrap.cs` and `Assets/Resources/TemplateMaterial.mat` when the real game replaces them
(or keep the material as the guaranteed-URP/Lit reference, see rule 3).

## 2. Every asset needs a `.meta` with a deterministic GUID
Unity in batch mode generates missing metas, but scene/prefab/material references need **known** GUIDs.
Generate them as `md5("unityfactory:" + assetPath)` → 32 hex chars:
```bash
mk(){ printf 'unityfactory:%s' "$1" | md5sum | cut -c1-32; }
```
Templates:
- folder → `folderAsset: yes` / `DefaultImporter`
- `.cs` → `MonoImporter` (`serializedVersion: 2`, `executionOrder: 0`)
- `.shader` → `ShaderImporter`; `.hlsl`/`.cginc` → `ShaderIncludeImporter`
- `.mat` / `.asset` → `NativeFormatImporter` (`mainObjectFileID: 2100000` for materials, `11400000` for ScriptableObjects)
- `.unity` → `DefaultImporter`
- `.asmdef` → `AssemblyDefinitionImporter`
- `.androidlib` folder → `PluginImporter` with Android enabled
- `.png` → `TextureImporter` (copy a real one from Unity docs; or load textures from `Resources` as `.bytes` and decode with `ImageConversion.LoadImage`)
- `.bytes`/`.json`/`.txt` → `TextScriptImporter`
A MonoBehaviour in a scene is referenced as `m_Script: {fileID: 11500000, guid: <script guid>, type: 3}`.

## 3. Shaders — the magenta rule
Unity **strips every shader not referenced by a built asset**. `Shader.Find("Universal Render Pipeline/Lit")`
in code at runtime returns `null` → everything using it renders **solid magenta**.
- Materials must be **assets** (`.mat` under `Assets/Resources` or referenced from the scene), never `new Material(Shader.Find(...))` for pipeline shaders.
- URP/Lit GUID is `933532a4fcc9baf4fa0491de14d08ed7`, URP/Unlit is `650dd9526735d5b46b79224bc6e94025`, URP/Simple Lit is `8d2bb70cbf9db8d4da26e15b26e74248`.
- Custom shaders go under `Assets/Resources/Shaders/` (guaranteed inclusion) and/or `ProjectSettings/GraphicsSettings.asset → m_AlwaysIncludedShaders` as `{fileID: 4800000, guid: <shader guid>, type: 3}`.
- Custom URP shaders: `Tags { "RenderPipeline"="UniversalPipeline" }`, `#pragma target 3.0` (or 3.5), include
  `Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl` and `Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl`, lit passes need `Tags{"LightMode"="UniversalForward"}` and `Lighting.hlsl`.
- Always guard: `var sh = Shader.Find(x); if (sh == null) Debug.LogError(...)` and show the failure in an on-screen HUD (the user can only send screenshots).

## 4. Code that compiles in batch mode
- **Verify C# locally before pushing** (the sandbox has `mono-mcs` + UnityEngine reference DLLs under `/tmp/unitydll/pkg/lib/net45/` if present; otherwise `pip download`/NuGet `UnityEngine.Modules`). A compile error costs a 12-minute CI round-trip.
- No `using UnityEditor` in runtime scripts (wrap with `#if UNITY_EDITOR`).
- Android-only APIs (`UnityEngine.Android.Permission`, `AndroidJavaObject`) inside `#if UNITY_ANDROID && !UNITY_EDITOR` / `#if UNITY_ANDROID`.
- Legacy input on device: `Input.touchCount`, `Input.GetTouch(i)`, `Input.gyro`, `Input.compass`, `Input.location`, `Input.acceleration`. Enable `Input.gyro.enabled = true`, `Input.compass.enabled = true`.
- uGUI buttons need an `EventSystem` + `StandaloneInputModule` in the scene (create at runtime if the scene is code-built).
- Runtime permissions: request `Permission.FineLocation` **and** `Permission.CoarseLocation` via `PermissionCallbacks`; declare them explicitly in `Assets/Plugins/Android/<Name>.androidlib/AndroidManifest.xml` (+ `project.properties` with `android.library=true`). Provide a HUD button to re-request.
- Keep `Debug.Log` diagnostics and an on-screen text HUD in every game: the only feedback channel is the user's screenshot.

## 5. Scenes
A scene can be a minimal YAML with a single GameObject holding a `Bootstrap` MonoBehaviour that builds everything in code
(`Assets/Scenes/Main.unity` is such a template — copy it, change the script GUID). Building everything in code avoids
hand-writing complex scene YAML. Add the scene path + GUID to `ProjectSettings/EditorBuildSettings.asset` and `build.json`.

## 6. Packages
Only add packages that need **no Editor-side setup**: e.g. `com.unity.textmeshpro` (already in uGUI), `com.unity.cinemachine`,
`com.unity.ai.navigation`, `com.unity.postprocessing` (not with URP), `com.unity.nuget.newtonsoft-json`, `com.unity.addressables` (needs settings assets — avoid).
Never add `com.unity.inputsystem` (see rule 0). Keep `com.unity.modules.*` as is.

## 7. Delivery checklist (every push)
1. `build.json` updated (name, package `com.<studio>.<game>`, orientation, scenes).
2. All new files have `.meta`; scene script GUIDs match.
3. Local C# compile passes with both `-define:UNITY_ANDROID` and `-define:UNITY_EDITOR`.
4. No `Shader.Find` on pipeline shaders without an asset reference.
5. Commit → push `main` → wait ~10–13 min → check `https://github.com/<owner>/<repo>/actions` → Release `vX.Y.Z` contains `<Slug>-vX.Y.Z.apk`.
6. If the run fails: download the `unity-logs` artifact (or `gh run view --log`) and grep `error CS`, `Shader error`, `[BuildScript]`.
7. Ask the user for a **screenshot** of the running game before calling anything done.

## 8. Things that were tried and do NOT work
- GameCI `game-ci/unity-builder` with manual `.alf/.ulf` license activation — manual activation is Enterprise-only now (license.unity3d.com/manual).
- Buildalon `unity-setup@v2` with the default Hub — `ENOENT /opt/unityhub/unityhub`; needs `hub-version: '3.14.3'`.
- `activeInputHandler: 2` ("Both") on Android — build error.
- Runtime `new Material(Shader.Find("Universal Render Pipeline/Lit"))` — magenta on device.
- Using `Input.location` without explicit manifest permissions after IL2CPP stripping — permission dialog never appears.
