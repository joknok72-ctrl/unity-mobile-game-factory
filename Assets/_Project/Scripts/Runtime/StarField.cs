// Real Life Sky — 8,920 real stars (HYG v4.1, Hipparcos/Yale/Gliese, V ≤ 6.5 = naked-eye limit) + 5 planets.
// Every star's apparent place: J2000 ICRS + proper motion → aberration → precession-nutation (IAU2006/2000B)
// → hour angle → topocentric ENU. Recomputed every 2 s on a background thread (precession and aberration
// change slowly); Earth rotation between updates is applied EXACTLY by rotating the whole mesh about the
// celestial pole by the elapsed sidereal angle, so the sky turns at the true 15.041"/s.
// Rendering: one mesh (4 verts/star), custom shader; colour from B-V (Ballesteros 2012 blackbody fit);
// magnitude → illuminance handled in the shader. Planets get their live magnitudes & positions each frame.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using RealLife.Astronomy;

namespace RealLife.Sky
{
    public class StarField : MonoBehaviour
    {
        public Material starMaterial;
        [Tooltip("Eye point-spread cut radius in arcmin (quad half-size).")]
        public float psfRadiusArcmin = 6f;
        public float scintillation = 1f;

        struct Star { public float Ra, Dec, PmRa, PmDec, Mag, Ci; }
        Star[] _stars;
        Mesh _mesh;
        Vector3[] _verts; Color[] _cols; Vector2[] _uv0, _uv1;
        MeshFilter _mf; MeshRenderer _mr;

        // planets appended after stars
        const int PlanetCount = 5;
        int _planetBase;

        double _lastJdUtc;   // JD of the last full recompute (for sidereal rotation delta)
        double _lastLat, _lastLon;
        Task _job; Vector3[] _jobResult; double _jobJd;
        float _nextJob;

        static readonly int ID_Psf = Shader.PropertyToID("_PsfRadiusRad");
        static readonly int ID_PixAng = Shader.PropertyToID("_PixelAngle");
        static readonly int ID_Scint = Shader.PropertyToID("_Scintillation");

        void Awake()
        {
            LoadCatalog();
            BuildMesh();
        }

        void LoadCatalog()
        {
            var ta = Resources.Load<TextAsset>("Data/stars_hyg41");
            if (ta == null) { Debug.LogError("StarField: catalog missing"); _stars = new Star[0]; return; }
            using (var br = new BinaryReader(new MemoryStream(ta.bytes)))
            {
                var magic = br.ReadBytes(4);
                int n = br.ReadInt32();
                _stars = new Star[n];
                for (int i = 0; i < n; i++)
                {
                    _stars[i].Ra = br.ReadSingle(); _stars[i].Dec = br.ReadSingle();
                    _stars[i].PmRa = br.ReadSingle(); _stars[i].PmDec = br.ReadSingle();
                    _stars[i].Mag = br.ReadSingle(); _stars[i].Ci = br.ReadSingle();
                }
            }
        }

        /// <summary>B-V colour index → linear sRGB (unit luminance). Ballesteros (2012) T = 4600(1/(0.92BV+1.7)+1/(0.92BV+0.62)); blackbody → sRGB.</summary>
        public static Color ColorFromBV(float bv)
        {
            bv = Mathf.Clamp(bv, -0.4f, 2.0f);
            double T = 4600.0 * (1.0 / (0.92 * bv + 1.7) + 1.0 / (0.92 * bv + 0.62));
            // Planckian locus approximation (Kim et al. 2002) → CIE xy → linear sRGB
            double t = T, x;
            if (t <= 4000) x = -0.2661239e9 / (t * t * t) - 0.2343589e6 / (t * t) + 0.8776956e3 / t + 0.179910;
            else x = -3.0258469e9 / (t * t * t) + 2.1070379e6 / (t * t) + 0.2226347e3 / t + 0.240390;
            double y;
            if (t <= 2222) y = -1.1063814 * x * x * x - 1.34811020 * x * x + 2.18555832 * x - 0.20219683;
            else if (t <= 4000) y = -0.9549476 * x * x * x - 1.37418593 * x * x + 2.09137015 * x - 0.16748867;
            else y = 3.0817580 * x * x * x - 5.87338670 * x * x + 3.75112997 * x - 0.37001483;
            double Y = 1.0, X = x / y * Y, Z = (1 - x - y) / y * Y;
            double r = 3.2406 * X - 1.5372 * Y - 0.4986 * Z;
            double g = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
            double b = 0.0557 * X - 0.2040 * Y + 1.0570 * Z;
            r = Math.Max(0, r); g = Math.Max(0, g); b = Math.Max(0, b);
            double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            if (lum <= 0) return Color.white;
            return new Color((float)(r / lum), (float)(g / lum), (float)(b / lum), 1);
        }

        void BuildMesh()
        {
            int n = _stars.Length + PlanetCount;
            _planetBase = _stars.Length;
            _verts = new Vector3[n * 4]; _cols = new Color[n * 4]; _uv0 = new Vector2[n * 4]; _uv1 = new Vector2[n * 4];
            var idx = new int[n * 6];
            var rng = new System.Random(12345);
            for (int i = 0; i < n; i++)
            {
                Color c = i < _stars.Length ? ColorFromBV(_stars[i].Ci) : Color.white;
                float mag = i < _stars.Length ? _stars[i].Mag : 99f;
                float seed = (float)rng.NextDouble();
                for (int k = 0; k < 4; k++)
                {
                    _cols[i * 4 + k] = c;
                    _uv0[i * 4 + k] = new Vector2((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1);
                    _uv1[i * 4 + k] = new Vector2(mag, seed);
                    _verts[i * 4 + k] = Vector3.up;
                }
                int b = i * 4, t = i * 6;
                idx[t] = b; idx[t + 1] = b + 2; idx[t + 2] = b + 1; idx[t + 3] = b + 1; idx[t + 4] = b + 2; idx[t + 5] = b + 3;
            }
            _mesh = new Mesh { name = "StarField", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.vertices = _verts; _mesh.colors = _cols; _mesh.uv = _uv0; _mesh.uv2 = _uv1; _mesh.triangles = idx;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            _mf = gameObject.GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
            _mr = gameObject.GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
            _mf.sharedMesh = _mesh;
            _mr.sharedMaterial = starMaterial;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        void Update()
        {
            var dir = CelestialDirector.Instance;
            if (dir == null || dir.Snapshot.Planets == null || _stars.Length == 0) return;
            var cam = Camera.main;
            if (cam != null)
            {
                starMaterial.SetFloat(ID_PixAng, cam.fieldOfView * Mathf.Deg2Rad / Screen.height);
            }
            starMaterial.SetFloat(ID_Psf, psfRadiusArcmin / 60f * Mathf.Deg2Rad);
            starMaterial.SetFloat(ID_Scint, scintillation);

            var obs = dir.CurrentObserver;
            // Kick a background recompute every 2 s (or immediately when location changed)
            bool locChanged = Math.Abs(obs.LatitudeDeg - _lastLat) > 1e-6 || Math.Abs(obs.LongitudeDeg - _lastLon) > 1e-6;
            if (_job == null && (Time.unscaledTime > _nextJob || locChanged || _lastJdUtc == 0))
            {
                _nextJob = Time.unscaledTime + 2f;
                var snap = dir.Snapshot; // struct copy (contains Bpn, LAST, Earth velocity)
                double lat = obs.LatitudeDeg, lon = obs.LongitudeDeg;
                var result = new Vector3[_stars.Length];
                double jd = snap.JdUtc;
                _job = Task.Run(() =>
                {
                    for (int i = 0; i < _stars.Length; i++)
                    {
                        var s = _stars[i];
                        Vec3d enu = SkyModel.StarToEnu(s.Ra, s.Dec, s.PmRa, s.PmDec, snap);
                        result[i] = new Vector3((float)enu.X, (float)enu.Z, (float)enu.Y);
                    }
                });
                _jobResult = result; _jobJd = jd; _lastLat = lat; _lastLon = lon;
            }
            if (_job != null && _job.IsCompleted)
            {
                if (_job.IsFaulted) Debug.LogException(_job.Exception);
                else { ApplyStars(_jobResult); _lastJdUtc = _jobJd; }
                _job = null;
            }

            // Exact Earth rotation since last full recompute: rotate the whole star mesh about the celestial pole.
            // Sidereal rate: 360.985647° per UT day. Sky rotates westward: from east through south to west => about the
            // NCP axis by -θ (right-hand rule with axis pointing to the pole).
            double dtDays = _lastJdUtc == 0 ? 0 : AstroTime.JulianDateUtc(dir.CurrentUtc) - _lastJdUtc;
            float thetaDeg = (float)(dtDays * 360.985647);
            double latRad = obs.LatitudeDeg * AstroTime.Deg2Rad;
            Vector3 ncp = new Vector3(0, (float)Math.Sin(latRad), (float)Math.Cos(latRad));
            transform.rotation = Quaternion.AngleAxis(-thetaDeg, ncp);
            transform.position = cam != null ? cam.transform.position : Vector3.zero;

            // Planets: live from the director (already topocentric, apparent-of-date). Counter-rotate so the mesh rotation cancels.
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            for (int p = 0; p < PlanetCount; p++)
            {
                var b = dir.Snapshot.Planets[p];
                Vector3 d = inv * new Vector3((float)b.TopocentricUnitEnu.X, (float)b.TopocentricUnitEnu.Z, (float)b.TopocentricUnitEnu.Y);
                int baseIdx = (_planetBase + p) * 4;
                float mag = (float)b.VisualMagnitude;
                // planets are not points: Venus/Jupiter up to 1'; still far below the eye's PSF, treat as point sources.
                Color c = PlanetColor(p);
                for (int k = 0; k < 4; k++) { _verts[baseIdx + k] = d; _uv1[baseIdx + k] = new Vector2(mag, 0.5f + p * 0.1f); _cols[baseIdx + k] = c; }
            }
            _mesh.vertices = _verts; _mesh.uv2 = _uv1; _mesh.colors = _cols;
        }

        static Color PlanetColor(int p)
        {
            // measured B-V: Mercury 0.93, Venus 0.82, Mars 1.36, Jupiter 0.83, Saturn 1.04 (Mallama 2018)
            float[] bv = { 0.93f, 0.82f, 1.36f, 0.83f, 1.04f };
            return ColorFromBV(bv[p]);
        }

        void ApplyStars(Vector3[] enu)
        {
            for (int i = 0; i < enu.Length; i++)
            {
                int b = i * 4;
                _verts[b] = enu[i]; _verts[b + 1] = enu[i]; _verts[b + 2] = enu[i]; _verts[b + 3] = enu[i];
            }
        }
    }
}
