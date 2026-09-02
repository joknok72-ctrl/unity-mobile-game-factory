// Real Life Sky — the conductor. Every frame:
//   1. asks WorldClock for the true UTC instant and GeoLocation for the observer,
//   2. computes the full sky (SkyModel) — Sun, Moon (+libration), planets — in double precision,
//   3. converts ENU -> Unity world (+X East, +Y Up, +Z North),
//   4. drives the URP main Directional Light (direction, colour, PHYSICAL illuminance in lux) and ambient,
//   5. uploads global shader uniforms, renders the Sky-View LUT, positions/orients the Moon,
//   6. hands photometric data to ExposureController (eye adaptation) and StarField.
// Nothing here is faked: the light colour is the transmittance-filtered sunlight, the lux value is the
// real ground illuminance, the Moon's phase is a consequence of geometry, not a texture.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using RealLife.Astronomy;

namespace RealLife.Sky
{
    [DefaultExecutionOrder(-100)]
    public class CelestialDirector : MonoBehaviour
    {
        public static CelestialDirector Instance { get; private set; }

        [Header("Scene references (created by Bootstrap if null)")]
        public Light sunLight;
        public Light moonLight;
        public Transform moonTransform;
        public Material skyboxMaterial;
        public Material moonMaterial;
        public Material skyViewLutMaterial;

        [Header("Update policy")]
        [Tooltip("Ephemeris recompute period (s). Sun/Moon move 15\"/s: 1 s keeps error < 15\" between updates; positions are interpolated.")]
        public float ephemerisPeriod = 1.0f;

        // ---- public state for HUD / others ----
        public SkyModel.SkySnapshot Snapshot { get; private set; }
        public SkyModel.SkySnapshot SnapshotNext { get; private set; }
        public Observer CurrentObserver { get; private set; }
        public DateTime CurrentUtc { get; private set; }
        public Vector3 SunDirWorld { get; private set; }         // true geometric
        public Vector3 SunDirWorldApparent { get; private set; } // refracted
        public Vector3 MoonDirWorld { get; private set; }
        public Vector3 MoonDirWorldApparent { get; private set; }
        public double SunIlluminanceLux { get; private set; }    // on a surface normal to the Sun, at the observer
        public double SkyIrradianceLux { get; private set; }     // horizontal, from the sky
        public double GroundIlluminanceLux { get; private set; } // total horizontal illuminance (sun*cos + sky + moon)
        public double MoonIlluminanceLux { get; private set; }
        public Color SunLightColor { get; private set; }
        public SkyModel.DayPhase Phase { get; private set; }
        public Texture2D TransmittanceLUT { get; private set; }
        public Texture2D MultiScatLUT { get; private set; }
        public RenderTexture SkyViewLUT { get; private set; }
        public double ObserverRadiusKm => AtmosphereModel.Rg + Math.Max(0, CurrentObserver.AltitudeM) / 1000.0;
        /// <summary>Current display exposure (set by ExposureController; previous frame value, 1 frame latency is invisible).</summary>
        public double LightExposure => ExposureController.Instance != null ? ExposureController.Instance.Exposure : 1e-4;

        float _nextEphemeris;
        DateTime _t0, _t1;
        readonly double[] _tmp3 = new double[3], _tmp3b = new double[3];

        // Shader property IDs
        static readonly int ID_SunDir = Shader.PropertyToID("_RL_SunDir");
        static readonly int ID_SunDirApp = Shader.PropertyToID("_RL_SunDirApparent");
        static readonly int ID_SunRadiance = Shader.PropertyToID("_RL_SunRadiance");
        static readonly int ID_SunIllum = Shader.PropertyToID("_RL_SunIlluminance");
        static readonly int ID_SunAngR = Shader.PropertyToID("_RL_SunAngularRadius");
        static readonly int ID_ObsH = Shader.PropertyToID("_RL_ObserverHeightKm");
        static readonly int ID_RefrScale = Shader.PropertyToID("_RL_RefractionScale");
        static readonly int ID_MoonDir = Shader.PropertyToID("_RL_MoonDir");
        static readonly int ID_MoonAngR = Shader.PropertyToID("_RL_MoonAngularRadius");
        static readonly int ID_MoonMag = Shader.PropertyToID("_RL_MoonMagnitude");
        static readonly int ID_Time = Shader.PropertyToID("_RL_Time");
        static readonly int ID_NightSky = Shader.PropertyToID("_RL_NightSkyRadiance");
        static readonly int ID_TransLut = Shader.PropertyToID("_RL_TransmittanceLUT");
        static readonly int ID_MsLut = Shader.PropertyToID("_RL_MultiScatLUT");
        static readonly int ID_SkyViewLut = Shader.PropertyToID("_RL_SkyViewLUT");
        static readonly int ID_MoonSunLocal = Shader.PropertyToID("_MoonSunDirLocal");
        static readonly int ID_MoonEarthLocal = Shader.PropertyToID("_MoonEarthDirLocal");
        static readonly int ID_MoonPhase = Shader.PropertyToID("_MoonPhaseAngle");
        static readonly int ID_Earthshine = Shader.PropertyToID("_EarthshineLux");
        static readonly int ID_MoonTrans = Shader.PropertyToID("_MoonTransmittance");

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            TransmittanceLUT = AtmosphereModel.BuildTransmittanceLUT();
            MultiScatLUT = AtmosphereModel.BuildMultiScatteringLUT();
            Shader.SetGlobalTexture(ID_TransLut, TransmittanceLUT);
            Shader.SetGlobalTexture(ID_MsLut, MultiScatLUT);
            SkyViewLUT = new RenderTexture(192, 108, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { name = "RL_SkyViewLUT", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false };
            SkyViewLUT.wrapModeU = TextureWrapMode.Repeat; // azimuth wraps
            SkyViewLUT.Create();
            Shader.SetGlobalTexture(ID_SkyViewLut, SkyViewLUT);
            // Zenith night sky ≈ 22.0 mag/arcsec² (V) ≈ 1.7e-4 cd/m², greenish airglow tint (OI 557.7 nm)
            Shader.SetGlobalVector(ID_NightSky, new Vector4(1.35e-4f, 1.80e-4f, 1.55e-4f, 0));
        }

        void OnDestroy() { if (SkyViewLUT != null) SkyViewLUT.Release(); }

        static Vector3 EnuToWorld(Vec3d enu) => new Vector3((float)enu.X, (float)enu.Z, (float)enu.Y);
        static Vector3 DirFromAltAz(double altRad, double azRad)
        {
            double ca = Math.Cos(altRad);
            return new Vector3((float)(ca * Math.Sin(azRad)), (float)Math.Sin(altRad), (float)(ca * Math.Cos(azRad)));
        }

        void Update()
        {
            var clock = WorldClock.Instance; var geo = GeoLocation.Instance;
            if (clock == null || geo == null) return;
            CurrentUtc = clock.UtcNow;
            CurrentObserver = geo.Observer;

            if (Time.unscaledTime >= _nextEphemeris || Snapshot.Planets == null)
            {
                _nextEphemeris = Time.unscaledTime + ephemerisPeriod;
                _t0 = CurrentUtc; _t1 = CurrentUtc.AddSeconds(ephemerisPeriod);
                Snapshot = SkyModel.Compute(_t0, CurrentObserver);
                SnapshotNext = SkyModel.Compute(_t1, CurrentObserver);
            }
            // interpolation factor for smooth motion between exact samples (linear over ≤1 s — error ≪ 1")
            double f = Math.Max(0, Math.Min(1, (CurrentUtc - _t0).TotalSeconds / Math.Max(1e-3, (_t1 - _t0).TotalSeconds)));

            // ---- Sun ----
            Vector3 sunDir = Vector3.Slerp(EnuToWorld(Snapshot.Sun.TopocentricUnitEnu), EnuToWorld(SnapshotNext.Sun.TopocentricUnitEnu), (float)f).normalized;
            double sunAltTrue = Math.Asin(Math.Max(-1, Math.Min(1, sunDir.y)));
            double sunAltApp = sunAltTrue + Refraction.FromTrueAltitude(sunAltTrue, CurrentObserver.PressureHPa, CurrentObserver.TemperatureC);
            double sunAz = Math.Atan2(sunDir.x, sunDir.z);
            SunDirWorld = sunDir;
            SunDirWorldApparent = DirFromAltAz(sunAltApp, sunAz);
            Phase = SkyModel.ClassifyDayPhase(sunAltApp * AstroTime.Rad2Deg);

            double r = ObserverRadiusKm;
            double sunDistAu = Snapshot.Sun.DistanceAu;
            AtmosphereModel.SunIlluminanceAtObserver(r, sunDir.y, sunDistAu, _tmp3);
            SunIlluminanceLux = 0.2126 * _tmp3[0] + 0.7152 * _tmp3[1] + 0.0722 * _tmp3[2];
            SunLightColor = SunIlluminanceLux > 1e-6
                ? new Color((float)(_tmp3[0] / SunIlluminanceLux), (float)(_tmp3[1] / SunIlluminanceLux), (float)(_tmp3[2] / SunIlluminanceLux))
                : Color.black;
            AtmosphereModel.SkyIrradianceHorizontal(r, sunDir, sunDistAu, _tmp3b);
            SkyIrradianceLux = 0.2126 * _tmp3b[0] + 0.7152 * _tmp3b[1] + 0.0722 * _tmp3b[2];

            // ---- Moon ----
            Vector3 moonDir = Vector3.Slerp(EnuToWorld(Snapshot.Moon.TopocentricUnitEnu), EnuToWorld(SnapshotNext.Moon.TopocentricUnitEnu), (float)f).normalized;
            double moonAltTrue = Math.Asin(Math.Max(-1, Math.Min(1, moonDir.y)));
            double moonAltApp = moonAltTrue + Refraction.FromTrueAltitude(moonAltTrue, CurrentObserver.PressureHPa, CurrentObserver.TemperatureC);
            double moonAz = Math.Atan2(moonDir.x, moonDir.z);
            MoonDirWorld = moonDir;
            MoonDirWorldApparent = DirFromAltAz(moonAltApp, moonAz);
            var moon = Snapshot.Moon;
            // Moon illuminance at ground (lux) from apparent magnitude, atmospheric extinction applied
            double moonLuxTOA = Math.Pow(10, -0.4 * (moon.VisualMagnitude + 13.98));
            AtmosphereModel.Transmittance(r, Math.Max(moonDir.y, -0.02), _tmp3);
            double moonT = 0.2126 * _tmp3[0] + 0.7152 * _tmp3[1] + 0.0722 * _tmp3[2];
            MoonIlluminanceLux = moonDir.y > -0.01 ? moonLuxTOA * moonT : 0;

            GroundIlluminanceLux = Math.Max(0, sunDir.y) * SunIlluminanceLux + SkyIrradianceLux + Math.Max(0, moonDir.y) * MoonIlluminanceLux;

            // ---- Lights ----
            if (sunLight != null)
            {
                sunLight.transform.rotation = Quaternion.LookRotation(-SunDirWorldApparent, Vector3.up);
                sunLight.color = SunLightColor;
                // Physical units: a Lambertian surface (albedo ρ) under illuminance E has luminance ρE/π (cd/m²).
                // URP Lambert gives albedo × intensity × N·L (π folded), so intensity = E × exposure / π puts lit geometry
                // on exactly the same photometric scale as the sky (which is L × exposure).
                sunLight.intensity = (float)(SunIlluminanceLux * LightExposure / Math.PI);
                sunLight.enabled = sunDir.y > -0.05f;
                sunLight.shadows = sunAltApp > 0.02 ? LightShadows.Soft : LightShadows.None;
            }
            if (moonLight != null)
            {
                moonLight.transform.rotation = Quaternion.LookRotation(-MoonDirWorldApparent, Vector3.up);
                moonLight.color = new Color(1.0f, 0.98f, 0.94f);
                moonLight.intensity = (float)(MoonIlluminanceLux * LightExposure / Math.PI);
                moonLight.enabled = moonDir.y > -0.02f && MoonIlluminanceLux > 1e-5;
            }
            // Ambient: sky irradiance (hemisphere) — set as flat ambient using the sky's mean colour
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Ambient (flat) = sky irradiance E_sky: surface luminance ρ E_sky/π → ambient colour = E_sky × exposure / π
            double ae = LightExposure / Math.PI;
            RenderSettings.ambientLight = new Color((float)(_tmp3b[0] * ae), (float)(_tmp3b[1] * ae), (float)(_tmp3b[2] * ae));

            // ---- Global shader uniforms ----
            Shader.SetGlobalVector(ID_SunDir, SunDirWorld);
            Shader.SetGlobalVector(ID_SunDirApp, SunDirWorldApparent);
            double E_toa = AtmosphereModel.SolarIlluminanceTOA / (sunDistAu * sunDistAu);
            Shader.SetGlobalVector(ID_SunIllum, new Vector4((float)(E_toa * AtmosphereModel.SunColorLinear[0]), (float)(E_toa * AtmosphereModel.SunColorLinear[1]), (float)(E_toa * AtmosphereModel.SunColorLinear[2]), 0));
            double sunAngR = Snapshot.Sun.AngularDiameterRad * 0.5;
            // Disc radiance L = E / (π sin²θ)
            double Ldisc = E_toa / (Math.PI * Math.Sin(sunAngR) * Math.Sin(sunAngR));
            Shader.SetGlobalVector(ID_SunRadiance, new Vector4((float)(Ldisc * AtmosphereModel.SunColorLinear[0]), (float)(Ldisc * AtmosphereModel.SunColorLinear[1]), (float)(Ldisc * AtmosphereModel.SunColorLinear[2]), 0));
            Shader.SetGlobalFloat(ID_SunAngR, (float)sunAngR);
            Shader.SetGlobalFloat(ID_ObsH, (float)(Math.Max(0, CurrentObserver.AltitudeM) / 1000.0));
            Shader.SetGlobalFloat(ID_RefrScale, (float)((CurrentObserver.PressureHPa / 1010.0) * (283.0 / (273.0 + CurrentObserver.TemperatureC))));
            Shader.SetGlobalVector(ID_MoonDir, MoonDirWorld);
            Shader.SetGlobalFloat(ID_MoonAngR, (float)(moon.AngularDiameterRad * 0.5));
            Shader.SetGlobalFloat(ID_MoonMag, moonDir.y > -0.02f ? (float)moon.VisualMagnitude : 99f);
            Shader.SetGlobalFloat(ID_Time, Time.unscaledTime);

            // ---- Sky-View LUT (one 192x108 fullscreen pass per frame) ----
            if (skyViewLutMaterial != null) Graphics.Blit(null, SkyViewLUT, skyViewLutMaterial, 0);

            // ---- Moon transform & material ----
            UpdateMoon(moon, moonDir, (float)f);
        }

        void UpdateMoon(BodyState moon, Vector3 moonDirTrue, float f)
        {
            if (moonTransform == null) return;
            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            const float dist = 500f; // metres from camera (within far plane; scale gives exact angular size)
            float radius = dist * (float)Math.Tan(moon.AngularDiameterRad * 0.5);
            moonTransform.position = camPos + MoonDirWorldApparent * dist;
            moonTransform.localScale = Vector3.one * radius * 2f; // Unity sphere has diameter 1

            // Orientation: build the Moon's body frame in world space.
            //  - Moon's north pole is rotated from celestial north by the axis position angle P (eastward positive).
            //  - Libration (l, b) = selenographic lon/lat of the sub-Earth point: rotate so that texture (lon=l, lat=b)
            //    faces the observer.
            var a = moon.Aspect;
            // Local sky basis at the Moon: 'up' toward celestial north pole projected on the sky, 'east'
            Vector3 toMoon = MoonDirWorldApparent;
            // Celestial north pole direction in world: alt = latitude, az = 0 (North)
            double latRad = CurrentObserver.LatitudeDeg * AstroTime.Deg2Rad;
            Vector3 ncp = new Vector3(0, (float)Math.Sin(latRad), (float)Math.Cos(latRad));
            Vector3 skyNorth = Vector3.ProjectOnPlane(ncp, toMoon).normalized;   // "up" on the Moon's disc toward celestial north
            Vector3 skyEast = Vector3.Cross(toMoon, skyNorth).normalized;         // east on the sky (toward increasing RA)
            // Sky east: for a viewer looking at the Moon with north up, east is to the LEFT. Cross(toMoon, north): check sign:
            // toMoon = forward(z), north = up(y): cross(z, y) = -x = left. Good: east is left.
            // Rotate north vector by P toward east to get the Moon's pole direction on the disc
            Vector3 poleOnDisc = (skyNorth * (float)Math.Cos(a.AxisPositionAngleRad) + skyEast * (float)Math.Sin(a.AxisPositionAngleRad)).normalized;
            // Frame: Z axis (Unity sphere forward) — we want selenographic lon/lat mapping of the UV sphere:
            // Unity's built-in sphere: UV u wraps around Y axis, v from south pole (0) to north pole (1); u=0 at +X?  We define
            // the body frame with +Y = Moon north pole. The sub-Earth point at (l, b) must point toward the observer (-toMoon).
            Vector3 bodyY = poleOnDisc;                              // Moon's rotation axis in world
            // The direction from Moon centre to Earth in world is -toMoon; its selenographic coords are (l, b).
            // Body frame vector for (lon, lat): x = cos b cos l, y = sin b, z = cos b sin l  (lon measured from the mean Earth direction toward east/Mare Crisium)
            Vector3 subEarthBody = new Vector3((float)(Math.Cos(a.LibrationLatRad) * Math.Cos(a.LibrationLonRad)), (float)Math.Sin(a.LibrationLatRad), (float)(Math.Cos(a.LibrationLatRad) * Math.Sin(a.LibrationLonRad)));
            // Find rotation R such that R*bodyY_local(0,1,0) = bodyY and R*subEarthBody = -toMoon (approximately consistent since b is the latitude of -toMoon relative to pole)
            Quaternion q = Quaternion.LookRotation(Vector3.Cross(bodyY, -toMoon).normalized, bodyY); // temp frame: y=pole, z=east-ish
            // Now adjust the spin about the pole so that subEarthBody maps to -toMoon
            Vector3 target = -toMoon;
            Vector3 cur = q * subEarthBody;
            Vector3 tProj = Vector3.ProjectOnPlane(target, bodyY).normalized;
            Vector3 cProj = Vector3.ProjectOnPlane(cur, bodyY).normalized;
            float spin = Vector3.SignedAngle(cProj, tProj, bodyY);
            q = Quaternion.AngleAxis(spin, bodyY) * q;
            moonTransform.rotation = q;

            if (moonMaterial != null)
            {
                // Sun direction in Moon body frame: world sun dir (true) -> body
                Vector3 sunBody = Quaternion.Inverse(q) * SunDirWorld;
                Vector3 earthBody = Quaternion.Inverse(q) * (-toMoon);
                moonMaterial.SetVector(ID_MoonSunLocal, sunBody);
                moonMaterial.SetVector(ID_MoonEarthLocal, earthBody);
                moonMaterial.SetFloat(ID_MoonPhase, (float)moon.PhaseAngle);
                // Earthshine: Earth illuminance on the Moon ≈ 15 lx * illuminated fraction of Earth as seen from Moon
                double earthPhaseFrac = (1 - Math.Cos(moon.PhaseAngle)) / 2.0; // Earth's phase is complementary
                moonMaterial.SetFloat(ID_Earthshine, (float)(15.0 * earthPhaseFrac));
                AtmosphereModel.Transmittance(ObserverRadiusKm, Math.Max(moonDirTrue.y, -0.02), _tmp3);
                // Sun-Moon distance factor (AU²) folded into transmittance
                double dsun2 = Snapshot.Sun.DistanceAu * Snapshot.Sun.DistanceAu;
                moonMaterial.SetVector(ID_MoonTrans, new Vector4((float)(_tmp3[0] / dsun2), (float)(_tmp3[1] / dsun2), (float)(_tmp3[2] / dsun2), 1));
            }
            moonTransform.gameObject.SetActive(moonDirTrue.y > -0.02f);
        }
    }
}
