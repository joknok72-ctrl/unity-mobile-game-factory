// Real Life Sky — Astronomy Core
// High-level sky model: apparent topocentric positions of Sun, Moon, planets and stars for a real observer.
// Everything here is physically real: light-time, aberration, precession, nutation, parallax, refraction.
using System;

namespace RealLife.Astronomy
{
    public enum CelestialBodyId { Sun = 0, Moon = 1, Mercury = 2, Venus = 3, Mars = 4, Jupiter = 5, Saturn = 6 }

    /// <summary>Observer on Earth (WGS-84).</summary>
    public struct Observer
    {
        public double LatitudeDeg, LongitudeDeg, AltitudeM;
        public double TemperatureC, PressureHPa;
        public Observer(double lat, double lon, double altM = 0, double tempC = 15, double presHPa = 1013.25)
        { LatitudeDeg = lat; LongitudeDeg = lon; AltitudeM = altM; TemperatureC = tempC; PressureHPa = presHPa; }
    }

    /// <summary>Apparent horizontal coordinates.</summary>
    public struct HorizontalCoords
    {
        public double AzimuthRad;      // from North, eastwards (N=0, E=90°)
        public double AltitudeRad;     // geometric (unrefracted)
        public double ApparentAltRad;  // with atmospheric refraction
        public double AzimuthDeg => AzimuthRad * AstroTime.Rad2Deg;
        public double AltitudeDeg => AltitudeRad * AstroTime.Rad2Deg;
        public double ApparentAltDeg => ApparentAltRad * AstroTime.Rad2Deg;
    }

    public struct BodyState
    {
        public CelestialBodyId Id;
        public HorizontalCoords Horizontal;
        public double RaRad, DecRad;        // apparent, true equator & equinox of date
        public double DistanceAu;           // geocentric (topocentric for Moon)
        public double DistanceKm;
        public double AngularDiameterRad;   // apparent
        public double Elongation;           // angle Sun-Earth-body (rad)
        public double PhaseAngle;           // angle Sun-body-Earth (rad)
        public double IlluminatedFraction;  // 0..1
        public double VisualMagnitude;
        public double BrightLimbAngleRad;   // position angle of bright limb, from north through east
        public Vec3d TopocentricUnitEnu;    // unit direction in local East-North-Up frame (geometric)
        public MoonAspect Aspect;           // Moon only: libration, axis position angle, subsolar point
    }

    public static class SkyModel
    {
        public const double AuKm = 149597870.7;
        public const double SunRadiusKm = 695700.0;
        public const double MoonRadiusKm = 1737.4;
        public const double EarthEqRadiusKm = 6378.137;
        public const double EarthFlattening = 1.0 / 298.257223563;
        public const double LightTimeDaysPerAu = 0.0057755183; // days per AU (c)

        // ---------- Frames ----------

        /// <summary>Rotate ecliptic J2000 -> equatorial J2000 (ICRS-ish).</summary>
        public static Vec3d EclipticToEquatorialJ2000(Vec3d ecl)
        {
            const double eps0 = 84381.406 * AstroTime.ArcSec2Rad;
            double c = Math.Cos(eps0), s = Math.Sin(eps0);
            return new Vec3d(ecl.X, c * ecl.Y - s * ecl.Z, s * ecl.Y + c * ecl.Z);
        }

        /// <summary>Rotate ecliptic-of-date -> equatorial-of-date using the true obliquity.</summary>
        public static Vec3d EquatorialToEclipticOfDate(Vec3d eq, double trueObliquity)
        {
            double c = Math.Cos(trueObliquity), s = Math.Sin(trueObliquity);
            return new Vec3d(eq.X, eq.Y * c + eq.Z * s, -eq.Y * s + eq.Z * c);
        }

        public static Vec3d EclipticToEquatorialOfDate(Vec3d ecl, double trueObliquity)
        {
            double c = Math.Cos(trueObliquity), s = Math.Sin(trueObliquity);
            return new Vec3d(ecl.X, c * ecl.Y - s * ecl.Z, s * ecl.Y + c * ecl.Z);
        }

        /// <summary>Earth heliocentric position & velocity (AU, AU/day), J2000 ecliptic, via VSOP87.</summary>
        public static void EarthStateJ2000Ecliptic(double jdTt, out Vec3d pos, out Vec3d vel)
        {
            pos = Vsop87.HeliocentricEclipticJ2000(Vsop87Data.Earth, jdTt);
            const double h = 0.01; // days
            Vec3d p2 = Vsop87.HeliocentricEclipticJ2000(Vsop87Data.Earth, jdTt + h);
            Vec3d p1 = Vsop87.HeliocentricEclipticJ2000(Vsop87Data.Earth, jdTt - h);
            vel = (p2 - p1) * (1.0 / (2 * h));
        }

        /// <summary>
        /// Geocentric apparent direction (true equator & equinox of date) of a solar-system body given its
        /// heliocentric ecliptic-J2000 position function. Applies light-time iteration and annual aberration.
        /// </summary>
        private static Vec3d GeocentricApparentEquatorialOfDate(Func<double, Vec3d> helioPosJ2000, double jdTt,
            Vec3d earthPos, Vec3d earthVel, Mat3 bpn, out double distanceAu, out Vec3d geocentricJ2000Ecl)
        {
            // Light-time iteration (2 passes is plenty for < 1e-6 AU)
            double tau = 0;
            Vec3d rel = new Vec3d();
            for (int i = 0; i < 3; i++)
            {
                Vec3d body = helioPosJ2000(jdTt - tau);
                rel = body - earthPos;
                tau = rel.Length * LightTimeDaysPerAu;
            }
            distanceAu = rel.Length;
            geocentricJ2000Ecl = rel;
            // Annual aberration (classical, adequate to 0.001"): u' = u + v/c
            Vec3d u = rel.Normalized;
            Vec3d vOverC = earthVel * LightTimeDaysPerAu;
            Vec3d dirEcl = (u + vOverC).Normalized;
            Vec3d dirEq = EclipticToEquatorialJ2000(dirEcl);
            return (bpn * dirEq).Normalized;
        }

        // ---------- Local frame ----------

        /// <summary>Local apparent sidereal time (radians) at observer's longitude.</summary>
        public static double LocalApparentSiderealTime(double jdUtc, double jdTt, double lonDeg)
        {
            return AstroTime.NormalizeRadians(AstroTime.GreenwichApparentSiderealTime(jdUtc, jdTt) + lonDeg * AstroTime.Deg2Rad);
        }

        /// <summary>Convert an apparent equatorial-of-date unit vector to horizontal ENU (East, North, Up).</summary>
        public static Vec3d EquatorialToEnu(Vec3d eq, double lastRad, double latRad)
        {
            // Rotate about Z by -LAST => hour-angle frame (x toward local meridian, y toward west... we handle sign carefully)
            double cH = Math.Cos(lastRad), sH = Math.Sin(lastRad);
            // xh points to the meridian (HA=0), yh points to HA=+6h (west), z to north pole.
            double xh = cH * eq.X + sH * eq.Y;
            double yh = -(-sH * eq.X + cH * eq.Y); // HA increases westward => flip sign of the 'RA-ward' axis
            double zh = eq.Z;
            double cL = Math.Cos(latRad), sL = Math.Sin(latRad);
            // Up = xh*cosφ + zh*sinφ ; North = -xh*sinφ + zh*cosφ ; East = -yh (west) => East = -yh
            double up = xh * cL + zh * sL;
            double north = -xh * sL + zh * cL;
            double east = -yh;
            return new Vec3d(east, north, up);
        }

        public static HorizontalCoords EnuToHorizontal(Vec3d enu, Observer obs)
        {
            var h = new HorizontalCoords();
            h.AzimuthRad = AstroTime.NormalizeRadians(Math.Atan2(enu.X, enu.Y));
            h.AltitudeRad = Math.Asin(Math.Max(-1, Math.Min(1, enu.Z)));
            h.ApparentAltRad = h.AltitudeRad + Refraction.FromTrueAltitude(h.AltitudeRad, obs.PressureHPa, obs.TemperatureC);
            return h;
        }

        /// <summary>Observer geocentric position (km) in the true-equator-of-date frame rotating with Earth (x through Greenwich→ rotated by LAST).</summary>
        public static Vec3d ObserverGeocentricEquatorialKm(in Observer obs, double lastRad)
        {
            double lat = obs.LatitudeDeg * AstroTime.Deg2Rad;
            double e2 = EarthFlattening * (2 - EarthFlattening);
            double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
            double N = EarthEqRadiusKm / Math.Sqrt(1 - e2 * sinLat * sinLat);
            double hKm = obs.AltitudeM / 1000.0;
            double rho = (N + hKm) * cosLat;
            double z = (N * (1 - e2) + hKm) * sinLat;
            // Position at local sidereal angle LAST measured from equinox: x toward equinox
            return new Vec3d(rho * Math.Cos(lastRad), rho * Math.Sin(lastRad), z);
        }

        // ---------- Public API ----------

        public struct SkySnapshot
        {
            public double JdUtc, JdTt, LastRad, TrueObliquity;
            public Mat3 Bpn;
            public Vec3d EarthVelEclAuPerDay; // for stellar aberration
            public double ObserverLatRad;
            public BodyState Sun, Moon;
            public BodyState[] Planets; // Mercury, Venus, Mars, Jupiter, Saturn
            public double SunAltitudeDeg => Sun.Horizontal.ApparentAltDeg;
        }

        /// <summary>Compute the full sky state for an observer at a UTC instant.</summary>
        public static SkySnapshot Compute(DateTime utc, Observer obs)
        {
            var snap = new SkySnapshot();
            snap.JdUtc = AstroTime.JulianDateUtc(utc);
            snap.JdTt = AstroTime.TtFromUtc(snap.JdUtc);
            snap.Bpn = Precession.BiasPrecessionNutationMatrix(snap.JdTt);
            snap.TrueObliquity = Nutation.TrueObliquity(snap.JdTt);
            snap.LastRad = LocalApparentSiderealTime(snap.JdUtc, snap.JdTt, obs.LongitudeDeg);
            double latRad = obs.LatitudeDeg * AstroTime.Deg2Rad;

            EarthStateJ2000Ecliptic(snap.JdTt, out Vec3d earthPos, out Vec3d earthVel);
            snap.EarthVelEclAuPerDay = earthVel; snap.ObserverLatRad = latRad;
            Vec3d obsGeoKm = ObserverGeocentricEquatorialKm(obs, snap.LastRad);
            Vec3d obsGeoAu = obsGeoKm * (1.0 / AuKm);

            // ---- Sun ----
            {
                Vec3d dir = GeocentricApparentEquatorialOfDate(_ => new Vec3d(0, 0, 0), snap.JdTt, earthPos, earthVel, snap.Bpn, out double dist, out _);
                // topocentric (parallax ~8.8" — include for completeness)
                Vec3d topo = (dir * dist - obsGeoAu);
                double tdist = topo.Length; topo = topo.Normalized;
                var s = new BodyState { Id = CelestialBodyId.Sun, DistanceAu = tdist, DistanceKm = tdist * AuKm };
                topo.ToSpherical(out s.RaRad, out s.DecRad);
                s.TopocentricUnitEnu = EquatorialToEnu(topo, snap.LastRad, latRad);
                s.Horizontal = EnuToHorizontal(s.TopocentricUnitEnu, obs);
                s.AngularDiameterRad = 2 * Math.Asin(SunRadiusKm / s.DistanceKm);
                s.IlluminatedFraction = 1; s.VisualMagnitude = -26.74; s.Elongation = 0; s.PhaseAngle = 0;
                snap.Sun = s;
            }

            // ---- Moon ----
            {
                MoonPosition.Compute(snap.JdTt, out double lam, out double bet, out double distKm);
                // Meeus gives ecliptic of date referred to mean equinox; add nutation in longitude for apparent.
                Nutation.Compute(snap.JdTt, out double dpsi, out _);
                lam += dpsi;
                Vec3d eclDate = Vec3d.FromSpherical(lam, bet) * distKm;
                Vec3d eqDate = EclipticToEquatorialOfDate(eclDate, snap.TrueObliquity);
                // Topocentric: subtract observer geocentric vector (parallax up to ~1°: essential!)
                Vec3d topo = eqDate - obsGeoKm;
                double tdist = topo.Length;
                Vec3d dir = topo.Normalized;
                var m = new BodyState { Id = CelestialBodyId.Moon, DistanceKm = tdist, DistanceAu = tdist / AuKm };
                dir.ToSpherical(out m.RaRad, out m.DecRad);
                m.TopocentricUnitEnu = EquatorialToEnu(dir, snap.LastRad, latRad);
                m.Horizontal = EnuToHorizontal(m.TopocentricUnitEnu, obs);
                m.AngularDiameterRad = 2 * Math.Asin(MoonRadiusKm / tdist);
                // Phase geometry (Meeus ch. 48) using topocentric moon and sun vectors
                Vec3d sunDirEq = Vec3d.FromSpherical(snap.Sun.RaRad, snap.Sun.DecRad);
                double sunDistKm = snap.Sun.DistanceKm;
                m.Elongation = Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(dir, sunDirEq))));
                // phase angle i: tan i = R sin ψ / (Δ − R cos ψ)
                m.PhaseAngle = Math.Atan2(sunDistKm * Math.Sin(m.Elongation), tdist - sunDistKm * Math.Cos(m.Elongation));
                m.IlluminatedFraction = (1 + Math.Cos(m.PhaseAngle)) / 2;
                m.BrightLimbAngleRad = BrightLimbPositionAngle(snap.Sun.RaRad, snap.Sun.DecRad, m.RaRad, m.DecRad);
                m.VisualMagnitude = MoonMagnitude(m.PhaseAngle, tdist, sunDistKm);
                // Orientation (libration) uses the TOPOCENTRIC ecliptic lon/lat (Meeus 53: "for a topocentric
                // observer use topocentric coordinates") — parallax shifts libration by up to ~1°.
                Vec3d topoEcl = EquatorialToEclipticOfDate(topo, snap.TrueObliquity);
                topoEcl.ToSpherical(out double tlam, out double tbet);
                Vec3d sunEcl = EquatorialToEclipticOfDate(sunDirEq, snap.TrueObliquity);
                sunEcl.ToSpherical(out double slam, out _);
                m.Aspect = MoonOrientation.Compute(snap.JdTt, tlam, tbet, m.RaRad, m.DecRad, slam, snap.Sun.DistanceAu, tdist);
                snap.Moon = m;
            }

            // ---- Planets ----
            snap.Planets = new BodyState[5];
            double[][][][] bodies = { Vsop87Data.Mercury, Vsop87Data.Venus, Vsop87Data.Mars, Vsop87Data.Jupiter, Vsop87Data.Saturn };
            CelestialBodyId[] ids = { CelestialBodyId.Mercury, CelestialBodyId.Venus, CelestialBodyId.Mars, CelestialBodyId.Jupiter, CelestialBodyId.Saturn };
            double[] radiiKm = { 2439.7, 6051.8, 3389.5, 69911, 58232 };
            for (int i = 0; i < 5; i++)
            {
                var body = bodies[i];
                Vec3d dir = GeocentricApparentEquatorialOfDate(t => Vsop87.HeliocentricEclipticJ2000(body, t), snap.JdTt, earthPos, earthVel, snap.Bpn, out double dist, out Vec3d geoEcl);
                Vec3d topo = (dir * dist - obsGeoAu);
                double tdist = topo.Length; topo = topo.Normalized;
                var p = new BodyState { Id = ids[i], DistanceAu = tdist, DistanceKm = tdist * AuKm };
                topo.ToSpherical(out p.RaRad, out p.DecRad);
                p.TopocentricUnitEnu = EquatorialToEnu(topo, snap.LastRad, latRad);
                p.Horizontal = EnuToHorizontal(p.TopocentricUnitEnu, obs);
                p.AngularDiameterRad = 2 * Math.Asin(radiiKm[i] / p.DistanceKm);
                // Phase geometry
                Vec3d helio = Vsop87.HeliocentricEclipticJ2000(body, snap.JdTt);
                double r = helio.Length, R = earthPos.Length, delta = geoEcl.Length;
                double cosI = (r * r + delta * delta - R * R) / (2 * r * delta);
                p.PhaseAngle = Math.Acos(Math.Max(-1, Math.Min(1, cosI)));
                p.IlluminatedFraction = (1 + Math.Cos(p.PhaseAngle)) / 2;
                double cosE = (R * R + delta * delta - r * r) / (2 * R * delta);
                p.Elongation = Math.Acos(Math.Max(-1, Math.Min(1, cosE)));
                p.VisualMagnitude = PlanetMagnitude(ids[i], r, delta, p.PhaseAngle * AstroTime.Rad2Deg);
                snap.Planets[i] = p;
            }
            return snap;
        }

        /// <summary>Apparent position of a catalog star (ICRS RA/Dec at J2000 + proper motion) in horizontal ENU.</summary>
        public static Vec3d StarToEnu(double raJ2000Rad, double decJ2000Rad, double pmRaRadPerYr, double pmDecRadPerYr,
            SkySnapshot snap)
        {
            double latRad = snap.ObserverLatRad; Vec3d earthVelEclAuPerDay = snap.EarthVelEclAuPerDay;
            double yrs = (snap.JdTt - AstroTime.J2000) / 365.25;
            double ra = raJ2000Rad + pmRaRadPerYr * yrs / Math.Max(1e-9, Math.Cos(decJ2000Rad));
            double dec = decJ2000Rad + pmDecRadPerYr * yrs;
            Vec3d u = Vec3d.FromSpherical(ra, dec);
            // aberration
            Vec3d vEq = EclipticToEquatorialJ2000(earthVelEclAuPerDay) * LightTimeDaysPerAu;
            u = (u + vEq).Normalized;
            Vec3d eqDate = snap.Bpn * u;
            return EquatorialToEnu(eqDate, snap.LastRad, latRad);
        }

        // ---------- Photometry & phase ----------

        /// <summary>Position angle of the Moon's bright limb (Meeus 48.5).</summary>
        public static double BrightLimbPositionAngle(double raSun, double decSun, double raMoon, double decMoon)
        {
            double dRa = raSun - raMoon;
            double y = Math.Cos(decSun) * Math.Sin(dRa);
            double x = Math.Sin(decSun) * Math.Cos(decMoon) - Math.Cos(decSun) * Math.Sin(decMoon) * Math.Cos(dRa);
            return AstroTime.NormalizeRadians(Math.Atan2(y, x));
        }

        /// <summary>Lunar V magnitude vs phase angle (Allen's Astrophysical Quantities fit) scaled to distance.</summary>
        public static double MoonMagnitude(double phaseAngleRad, double distKm, double sunDistKm)
        {
            double i = phaseAngleRad * AstroTime.Rad2Deg;
            double m = -12.73 + 0.026 * i + 4e-9 * Math.Pow(i, 4);
            m += 5 * Math.Log10(distKm / 384400.0) + 5 * Math.Log10(sunDistKm / AuKm);
            return m;
        }

        /// <summary>Planetary visual magnitudes — Mallama & Hilton (2018), Astronomy and Computing 25.</summary>
        public static double PlanetMagnitude(CelestialBodyId id, double r, double delta, double alphaDeg)
        {
            double a = alphaDeg, d = 5 * Math.Log10(r * delta);
            switch (id)
            {
                case CelestialBodyId.Mercury:
                    return -0.613 + d + 6.3280e-2 * a - 1.6336e-3 * a * a + 3.3644e-5 * Math.Pow(a, 3) - 3.4265e-7 * Math.Pow(a, 4) + 1.6893e-9 * Math.Pow(a, 5) - 3.0334e-12 * Math.Pow(a, 6);
                case CelestialBodyId.Venus:
                    if (a <= 163.7) return -4.384 + d - 1.044e-3 * a + 3.687e-4 * a * a - 2.814e-6 * Math.Pow(a, 3) + 8.938e-9 * Math.Pow(a, 4);
                    return 236.05828 + d - 2.81914 * a + 8.39034e-3 * a * a;
                case CelestialBodyId.Mars:
                    if (a <= 50) return -1.601 + d + 0.02267 * a - 0.0001302 * a * a;
                    return -0.367 + d - 0.02573 * a + 0.0003445 * a * a;
                case CelestialBodyId.Jupiter:
                    if (a <= 12) return -9.395 + d - 3.7e-4 * a + 6.16e-4 * a * a;
                    { double x = a / 180.0; return -9.428 + d - 2.5 * Math.Log10(1.0 - 1.507 * x - 0.363 * x * x - 0.062 * x * x * x + 2.809 * Math.Pow(x, 4) - 1.876 * Math.Pow(x, 5)); }
                case CelestialBodyId.Saturn:
                    // Globe only (rings ignored in this phase — Saturn is a point for the naked eye)
                    return -8.914 + d + 0.026 * a; // mean including typical ring contribution ~ -8.9
                default: return 0;
            }
        }

        // ---------- Twilight classification ----------

        public enum DayPhase { Day, CivilTwilight, NauticalTwilight, AstronomicalTwilight, Night }

        public static DayPhase ClassifyDayPhase(double sunApparentAltDeg)
        {
            // Standard definitions use geometric center altitude: -0.833° (rise/set incl. refraction & semi-diameter), -6, -12, -18.
            if (sunApparentAltDeg > -0.833) return DayPhase.Day;
            if (sunApparentAltDeg > -6) return DayPhase.CivilTwilight;
            if (sunApparentAltDeg > -12) return DayPhase.NauticalTwilight;
            if (sunApparentAltDeg > -18) return DayPhase.AstronomicalTwilight;
            return DayPhase.Night;
        }

        // ---------- Rise / set / transit (search based, exact to the second) ----------

        public struct RiseSet { public DateTime? Rise, Set, Transit; public bool AlwaysUp, AlwaysDown; }

        /// <summary>
        /// Find rise/set/transit of a body for the local calendar day starting at 'localMidnightUtc' (UTC instant of local 00:00).
        /// Standard altitude h0 = -(34′ refraction + semidiameter) for Sun/Moon (topocentric), -0.5667° for planets/stars.
        /// Robust bracketing + bisection on real computed altitudes (no approximations).
        /// </summary>
        public static RiseSet FindRiseSet(CelestialBodyId id, DateTime localMidnightUtc, Observer obs)
        {
            var rs = new RiseSet();
            // NOTE: all altitudes produced by Compute() are already TOPOCENTRIC (parallax applied),
            // so the standard altitude must NOT include the Meeus geocentric parallax term (0.7275*pi).
            // Rise/set = moment the upper limb touches the refracted horizon: h0 = -(34' + semidiameter).
            Func<DateTime, double> Alt = t =>
            {
                var s = Compute(t, obs);
                BodyState b = id == CelestialBodyId.Sun ? s.Sun : id == CelestialBodyId.Moon ? s.Moon : s.Planets[(int)id - 2];
                double h0;
                if (id == CelestialBodyId.Sun || id == CelestialBodyId.Moon)
                    h0 = -(0.5667 + 0.5 * b.AngularDiameterRad * AstroTime.Rad2Deg); // Sun ≈ -0.8333°, Moon ≈ -0.83..-0.85°
                else
                    h0 = -0.5667;
                return b.Horizontal.AltitudeDeg - h0; // geometric topocentric altitude vs standard altitude
            };
            const int steps = 144; // 10-minute grid
            double prev = Alt(localMidnightUtc);
            bool anyUp = prev > 0, anyDown = prev <= 0;
            DateTime tPrev = localMidnightUtc;
            double maxAlt = prev; DateTime tMax = localMidnightUtc;
            for (int i = 1; i <= steps; i++)
            {
                DateTime t = localMidnightUtc.AddMinutes(10 * i);
                double a = Alt(t);
                if (a > maxAlt) { maxAlt = a; tMax = t; }
                anyUp |= a > 0; anyDown |= a <= 0;
                if (prev <= 0 && a > 0 && rs.Rise == null) rs.Rise = Bisect(Alt, tPrev, t, true);
                if (prev > 0 && a <= 0 && rs.Set == null) rs.Set = Bisect(Alt, tPrev, t, false);
                prev = a; tPrev = t;
            }
            rs.AlwaysUp = anyUp && !anyDown; rs.AlwaysDown = anyDown && !anyUp;
            // Transit: golden-section around the grid maximum
            DateTime lo = tMax.AddMinutes(-10), hi = tMax.AddMinutes(10);
            for (int k = 0; k < 30; k++)
            {
                double span = (hi - lo).TotalSeconds;
                DateTime m1 = lo.AddSeconds(span * 0.382), m2 = lo.AddSeconds(span * 0.618);
                if (Alt(m1) < Alt(m2)) lo = m1; else hi = m2;
            }
            rs.Transit = lo.AddSeconds((hi - lo).TotalSeconds / 2);
            return rs;
        }

        private static DateTime Bisect(Func<DateTime, double> f, DateTime a, DateTime b, bool rising)
        {
            for (int i = 0; i < 24; i++) // 600 s / 2^24 → sub-millisecond
            {
                DateTime m = a.AddSeconds((b - a).TotalSeconds / 2);
                double v = f(m);
                bool upAtM = v > 0;
                if (rising ? !upAtM : upAtM) a = m; else b = m;
            }
            return a.AddSeconds((b - a).TotalSeconds / 2);
        }
    }

    /// <summary>Atmospheric refraction, Bennett (1982) / Sæmundsson (1986) as in Meeus ch. 16, with P/T scaling.</summary>
    public static class Refraction
    {
        /// <summary>Refraction (radians) to ADD to true altitude to get apparent altitude. Valid for h ≥ -1°; clamped below.</summary>
        public static double FromTrueAltitude(double trueAltRad, double pressureHPa = 1013.25, double tempC = 15)
        {
            double h = trueAltRad * AstroTime.Rad2Deg;
            if (h < -1.5) h = -1.5; // beyond the horizon: freeze value (bodies below are not rendered anyway)
            // Sæmundsson formula (arcminutes)
            double R = 1.02 / Math.Tan((h + 10.3 / (h + 5.11)) * AstroTime.Deg2Rad);
            R += 0.0019279; // so that R(90°)=0
            R *= (pressureHPa / 1010.0) * (283.0 / (273.0 + tempC));
            return Math.Max(0, R) / 60.0 * AstroTime.Deg2Rad;
        }

        /// <summary>Refraction (radians) from apparent altitude (Bennett).</summary>
        public static double FromApparentAltitude(double appAltRad, double pressureHPa = 1013.25, double tempC = 15)
        {
            double h0 = appAltRad * AstroTime.Rad2Deg;
            if (h0 < -1.0) h0 = -1.0;
            double R = 1.0 / Math.Tan((h0 + 7.31 / (h0 + 4.4)) * AstroTime.Deg2Rad);
            R *= (pressureHPa / 1010.0) * (283.0 / (273.0 + tempC));
            return Math.Max(0, R) / 60.0 * AstroTime.Deg2Rad;
        }
    }
}
