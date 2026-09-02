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

            // --- Shaders & materials ---
            Shader skySh = Shader.Find("RealLife/Skybox");
            Shader lutSh = Shader.Find("RealLife/SkyViewLUT");
            Shader moonSh = Shader.Find("RealLife/Moon");
            Shader starSh = Shader.Find("RealLife/Stars");
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
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.15f, 0.15f, 0.15f, 1f) };
            gm.SetFloat("_Smoothness", 0.05f); gm.SetFloat("_Metallic", 0f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
            Destroy(ground.GetComponent<Collider>());
        }
    }
}
