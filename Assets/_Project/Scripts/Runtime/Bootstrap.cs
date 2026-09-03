// Real Life Sky — scene assembly at start-up. The .unity scene contains only this component on one object;
// everything else (camera, lights, skybox material, Moon sphere, star field, HUD, post-processing volume) is
// created here from code + Resources. This avoids hand-written scene YAML and guarantees a consistent setup in
// the CI build.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RealLife.Sky
{
    public class Bootstrap : MonoBehaviour
    {
        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 0;

            // --- Services ---
            var services = new GameObject("Services");
            services.AddComponent<WorldClock>();
            services.AddComponent<GeoLocation>();

            // --- Camera ---
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.allowHDR = true; cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = false;
            camGo.AddComponent<SkyCamera>();
            camGo.transform.position = new Vector3(0, 1.6f, 0); // eye height
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f;   // the ground disc is 2 km across

            // --- Shaders & materials ---
            // All shaders live in Resources/Shaders so they are guaranteed to be in the build. Never use Shader.Find on
            // a pipeline shader (e.g. "Universal Render Pipeline/Lit") here: with no material asset referencing it, the build
            // strips it, Shader.Find returns null and every surface using it renders magenta.
            Shader skySh = RequireShader("RealLife/Skybox");
            Shader lutSh = RequireShader("RealLife/SkyViewLUT");
            Shader moonSh = RequireShader("RealLife/Moon");
            Shader starSh = RequireShader("RealLife/Stars");
            Shader groundSh = RequireShader("RealLife/Ground");
            var skyMat = new Material(skySh) { name = "RealSkybox" };
            var lutMat = new Material(lutSh) { name = "SkyViewLUT" };
            var moonMat = new Material(moonSh) { name = "Moon" };
            var starMat = new Material(starSh) { name = "Stars" };
            var moonTex = Resources.Load<Texture2D>("Textures/Moon_LROC_Albedo");
            if (moonTex != null) moonMat.SetTexture("_Albedo", moonTex);
            RenderSettings.skybox = skyMat;
            RenderSettings.sun = null;

            // --- Lights ---
            var sunGo = new GameObject("Sun Light");
            var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 1f;
            var sunData = sunGo.AddComponent<UniversalAdditionalLightData>(); sunData.usePipelineSettings = true;
            RenderSettings.sun = sun;
            var moonLightGo = new GameObject("Moon Light");
            var moonLight = moonLightGo.AddComponent<Light>(); moonLight.type = LightType.Directional; moonLight.shadows = LightShadows.None;
            moonLightGo.AddComponent<UniversalAdditionalLightData>();

            // --- Moon sphere (exact angular size set every frame) ---
            var moonGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moonGo.name = "Moon";
            Destroy(moonGo.GetComponent<Collider>());
            var moonMr = moonGo.GetComponent<MeshRenderer>();
            moonMr.sharedMaterial = moonMat; moonMr.shadowCastingMode = ShadowCastingMode.Off; moonMr.receiveShadows = false;
            moonMr.lightProbeUsage = LightProbeUsage.Off; moonMr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // --- Stars ---
            var starsGo = new GameObject("Stars");
            var sf = starsGo.AddComponent<StarField>(); sf.starMaterial = starMat;

            // --- Director ---
            var dirGo = new GameObject("Celestial Director");
            var dir = dirGo.AddComponent<CelestialDirector>();
            dir.sunLight = sun; dir.moonLight = moonLight; dir.moonTransform = moonGo.transform;
            dir.skyboxMaterial = skyMat; dir.moonMaterial = moonMat; dir.skyViewLutMaterial = lutMat;

            // --- Post-processing volume (ACES tonemap, exposure controlled by ExposureController) ---
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 1;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.name = "RealSky Profile";
            var tm = profile.Add<Tonemapping>(true); tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.overrideState = true; ca.postExposure.value = 0f;
            var bloom = profile.Add<Bloom>(true); bloom.threshold.overrideState = true; bloom.threshold.value = 1.0f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.35f; bloom.scatter.overrideState = true; bloom.scatter.value = 0.6f;
            bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = false;
            vol.profile = profile;
            var exp = volGo.AddComponent<ExposureController>(); exp.volume = vol;

            // --- HUD ---
            var hudGo = new GameObject("HUD");
            hudGo.AddComponent<SkyHud>();

            // --- Ground plane (a real, dark, matte ground so the sun/moon light has something to fall on; albedo 0.15 asphalt) ---
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(200, 1, 200);
            ground.transform.position = Vector3.zero;
            var gm = new Material(groundSh) { name = "Ground" };
            gm.SetColor("_Albedo", new Color(0.15f, 0.15f, 0.15f, 1f));
            var groundMr = ground.GetComponent<MeshRenderer>();
            groundMr.sharedMaterial = gm; groundMr.shadowCastingMode = ShadowCastingMode.Off;
            groundMr.lightProbeUsage = LightProbeUsage.Off; groundMr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Destroy(ground.GetComponent<Collider>());

            Debug.Log($"[RealLifeSky] boot ok — shaders: {ShaderReport} gfx={SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName} sl={SystemInfo.graphicsShaderLevel}");
        }

        /// <summary>Names of shaders that failed to load (empty when everything is fine) — shown by the HUD.</summary>
        public static string MissingShaders = "";
        static string ShaderReport = "";

        static Shader RequireShader(string name)
        {
            var sh = Shader.Find(name);
            if (sh == null)
            {
                Debug.LogError($"[RealLifeSky] shader '{name}' not found in build");
                MissingShaders += (MissingShaders.Length > 0 ? ", " : "") + name;
                sh = Shader.Find("Hidden/InternalErrorShader");
            }
            else if (!sh.isSupported)
            {
                Debug.LogError($"[RealLifeSky] shader '{name}' is not supported on this GPU");
                MissingShaders += (MissingShaders.Length > 0 ? ", " : "") + name + " (غير مدعوم)";
            }
            ShaderReport += $"{name}={(sh != null && sh.isSupported ? "ok" : "FAIL")} ";
            return sh;
        }
    }
}
