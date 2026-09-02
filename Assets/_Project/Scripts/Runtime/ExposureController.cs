// Real Life Sky — the human eye. Everything is rendered in absolute photometric units (cd/m², lux):
// the Sun disc is 1.6e9 cd/m², the full Moon 2,500 cd/m², the night sky 1.7e-4 cd/m². A phone screen
// shows ~0.5–500 cd/m², so we model the visual system, not a camera:
//   • Adaptation luminance La = mean field luminance (log-domain, from the CPU sky model & sun/moon),
//   • Temporal adaptation: light-adaptation fast (τ≈0.1–0.5 s), dark-adaptation slow (cones ~5 min, rods ~30 min),
//     Pattanaik et al. 2000 / Durand & Dorsey 2000 model,
//   • Mesopic/scotopic transition (Purkinje shift, loss of colour) between 3 cd/m² and 0.003 cd/m²,
//   • Display mapping: exposure = key / La (Reinhard key 0.18) then URP ACES tonemap.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RealLife.Sky
{
    public class ExposureController : MonoBehaviour
    {
        public static ExposureController Instance { get; private set; }
        public Volume volume;
        [Range(0.05f, 0.5f)] public float key = 0.18f;

        public double AdaptationLuminance { get; private set; } = 1000; // cd/m²
        public double TargetLuminance { get; private set; }
        public float Scotopic { get; private set; }
        public float Exposure { get; private set; }

        static readonly int ID_Exposure = Shader.PropertyToID("_RL_Exposure");
        static readonly int ID_Scotopic = Shader.PropertyToID("_RL_Scotopic");

        ColorAdjustments _colorAdj;
        readonly double[] _tmp = new double[3];
        bool _first = true;

        void Awake() { Instance = this; }

        void Start()
        {
            if (volume != null && volume.profile != null)
            {
                if (!volume.profile.TryGet(out _colorAdj)) _colorAdj = volume.profile.Add<ColorAdjustments>(true);
                _colorAdj.postExposure.overrideState = true;
                if (!volume.profile.TryGet(out Tonemapping tm)) tm = volume.profile.Add<Tonemapping>(true);
                tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;
            }
        }

        void LateUpdate()
        {
            var dir = CelestialDirector.Instance;
            if (dir == null || dir.Snapshot.Planets == null) return;
            var cam = Camera.main;

            // ---- Metering: field luminance seen by the eye (what the camera is pointing at) ----
            // Sample the sky model in 9 directions across the field of view (real luminance, cd/m²)
            double sum = 0; int n = 0;
            double r = dir.ObserverRadiusKm;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            float half = (cam != null ? cam.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    Vector3 d = (fwd + right * (i * Mathf.Tan(half) * 0.6f) + up * (j * Mathf.Tan(half) * 0.6f)).normalized;
                    double L;
                    if (d.y >= 0)
                    {
                        AtmosphereModel.SkyLuminance(r, d, dir.SunDirWorld, dir.Snapshot.Sun.DistanceAu, _tmp, 10);
                        L = 0.2126 * _tmp[0] + 0.7152 * _tmp[1] + 0.0722 * _tmp[2];
                        // moonlit sky contribution (proportional to moon illuminance / sun illuminance ratio)
                        L += MoonSkyLuminance(dir, d);
                        L += 1.7e-4; // night sky floor
                    }
                    else
                    {
                        // ground (albedo 0.3) lit by total horizontal illuminance
                        L = dir.GroundIlluminanceLux * 0.3 / Math.PI + 1e-5;
                    }
                    sum += Math.Log(Math.Max(L, 1e-6)); n++;
                }
            // Sun in field of view? Add glare (veiling luminance, Vos 1984: Lveil = 10 E / θ² (θ in degrees))
            if (cam != null)
            {
                float ang = Vector3.Angle(fwd, dir.SunDirWorldApparent);
                if (dir.SunDirWorld.y > -0.01f && ang < 60f)
                {
                    double E = dir.SunIlluminanceLux * Math.Cos(ang * Mathf.Deg2Rad);
                    double veil = 10.0 * E / Math.Max(ang * ang, 1.0);
                    sum += Math.Log(1 + veil / Math.Exp(sum / n)) * n * 0.5; // blend into the log mean
                }
            }
            TargetLuminance = Math.Exp(sum / n);

            // ---- Temporal adaptation ----
            if (_first) { AdaptationLuminance = TargetLuminance; _first = false; }
            double la = AdaptationLuminance, lt = TargetLuminance;
            double tau;
            if (lt > la) tau = 0.4;                                   // light adaptation: sub-second
            else
            {
                // dark adaptation: cones ~ 3 min to 0.01 cd/m², rods take ~30 min; approximated by a luminance-dependent τ
                double logL = Math.Log10(Math.Max(lt, 1e-6));
                tau = logL > 0.5 ? 8.0 : logL > -2.5 ? 60.0 : 400.0;
            }
            double k = 1.0 - Math.Exp(-Time.unscaledDeltaTime / tau);
            AdaptationLuminance = Math.Exp(Math.Log(la) + (Math.Log(lt) - Math.Log(la)) * k);

            // ---- Mesopic / scotopic ----
            // photopic > 3 cd/m², scotopic < 0.003 cd/m² (CIE); logistic blend in log10
            double l10 = Math.Log10(Math.Max(AdaptationLuminance, 1e-6));
            Scotopic = (float)(1.0 / (1.0 + Math.Exp(3.0 * (l10 + 1.0)))); // 0.5 at 0.1 cd/m²
            Shader.SetGlobalFloat(ID_Scotopic, Scotopic);

            // ---- Display mapping ----
            // Reinhard key: mapped mid-grey = key ⇒ exposure = key / La. Night: absolute sensitivity is capped so the sky
            // stays dark (you cannot see the Milky Way as bright as daylight): floor La at 0.01 cd/m² equivalent.
            double laEff = Math.Max(AdaptationLuminance, 5e-3);
            Exposure = (float)(key / laEff);
            Shader.SetGlobalFloat(ID_Exposure, Exposure);
            // Sky shaders and scene lights both multiply by Exposure (CelestialDirector reads it) → post-exposure stays 0 EV.
            if (_colorAdj != null) _colorAdj.postExposure.value = 0f;
        }

        static double MoonSkyLuminance(CelestialDirector dir, Vector3 d)
        {
            // Moonlit sky ≈ (E_moon / E_sun_TOA) × daylight sky luminance for the Moon's direction. Cheap and adequate for metering.
            if (dir.MoonIlluminanceLux <= 0) return 0;
            double ratio = dir.MoonIlluminanceLux / AtmosphereModel.SolarIlluminanceTOA;
            // daylight sky at similar sun altitude ~ 3000–8000 cd/m²; use 5000 × ratio × elevation factor
            return 5000.0 * ratio * Math.Max(0.1, dir.MoonDirWorld.y);
        }
    }
}
