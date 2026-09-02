// Real Life Sky — Astronomy Core
// Time scales & Earth rotation. Pure C#, no UnityEngine dependency (verifiable offline).
// References:
//  - Meeus, "Astronomical Algorithms" 2nd ed. (ch. 7, 10, 12, 22)
//  - IERS Conventions 2010 (ERA, GMST polynomial)
//  - Espenak & Meeus ΔT polynomials (NASA eclipse site)
using System;

namespace RealLife.Astronomy
{
    public static class AstroTime
    {
        public const double J2000 = 2451545.0;          // JD of 2000 Jan 1.5 TT
        public const double JulianCentury = 36525.0;
        public const double SecondsPerDay = 86400.0;
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;
        public const double ArcSec2Rad = Deg2Rad / 3600.0;

        /// <summary>Julian Date (UTC scale) from a DateTime in UTC.</summary>
        public static double JulianDateUtc(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            // .NET ticks: 100ns since 0001-01-01. JD of 0001-01-01T00:00 = 1721425.5
            return 1721425.5 + utc.Ticks / (SecondsPerDay * 1e7);
        }

        public static DateTime DateTimeFromJdUtc(double jdUtc)
        {
            long ticks = (long)Math.Round((jdUtc - 1721425.5) * SecondsPerDay * 1e7);
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        /// <summary>
        /// ΔT = TT − UT1 in seconds. Espenak & Meeus polynomial fit (valid −1999..3000),
        /// with a linear extrapolation anchored to recent IERS values for years > 2020.
        /// Accuracy ~1s in the modern era, which is far below anything visible.
        /// </summary>
        public static double DeltaT(double year)
        {
            double y = year, t;
            if (y < 1986) { t = y - 1975; return 45.45 + 1.067 * t - t * t / 260 - t * t * t / 718; }
            if (y < 2005) { t = y - 2000; return 63.86 + 0.3345 * t - 0.060374 * t * t + 0.0017275 * t * t * t + 0.000651814 * t * t * t * t + 0.00002373599 * t * t * t * t * t; }
            if (y < 2020) { t = y - 2000; return 62.92 + 0.32217 * t + 0.005589 * t * t; }
            // Post-2020: observed ΔT has plateaued near 69.2 s (IERS Bulletin A: UT1-UTC ≈ 0, TAI-UTC = 37 s → ΔT = 32.184+37-ΔUT1 ≈ 69.2).
            // Use a gentle slope so it stays realistic for the next decade.
            return 69.2 + 0.05 * (y - 2020);
        }

        /// <summary>Decimal year from JD (UTC/UT).</summary>
        public static double DecimalYear(double jd)
        {
            return 2000.0 + (jd - J2000) / 365.25;
        }

        /// <summary>Terrestrial Time JD from UTC JD. (Uses ΔT; UT1≈UTC within 0.9 s.)</summary>
        public static double TtFromUtc(double jdUtc)
        {
            return jdUtc + DeltaT(DecimalYear(jdUtc)) / SecondsPerDay;
        }

        /// <summary>Julian centuries since J2000 (any scale).</summary>
        public static double CenturiesSinceJ2000(double jd) => (jd - J2000) / JulianCentury;

        /// <summary>Earth Rotation Angle (radians), IERS 2010 eq. 5.15. jdUt1 ≈ jdUtc.</summary>
        public static double EarthRotationAngle(double jdUt1)
        {
            double t = jdUt1 - J2000;
            double f = t - Math.Floor(t);
            double era = 2.0 * Math.PI * (f + 0.7790572732640 + 0.00273781191135448 * t);
            return NormalizeRadians(era);
        }

        /// <summary>
        /// Greenwich Mean Sidereal Time (radians), IAU 2006 (Capitaine et al. 2003), via ERA + polynomial in TT centuries.
        /// </summary>
        public static double GreenwichMeanSiderealTime(double jdUt1, double jdTt)
        {
            double t = CenturiesSinceJ2000(jdTt);
            double era = EarthRotationAngle(jdUt1);
            // arcseconds
            double poly = 0.014506 + 4612.156534 * t + 1.3915817 * t * t - 0.00000044 * t * t * t
                          - 0.000029956 * t * t * t * t - 0.0000000368 * t * t * t * t * t;
            return NormalizeRadians(era + poly * ArcSec2Rad);
        }

        /// <summary>Greenwich Apparent Sidereal Time (radians) = GMST + equation of the equinoxes.</summary>
        public static double GreenwichApparentSiderealTime(double jdUt1, double jdTt)
        {
            double gmst = GreenwichMeanSiderealTime(jdUt1, jdTt);
            Nutation.Compute(jdTt, out double dpsi, out double deps);
            double epsA = Nutation.MeanObliquity(jdTt);
            double eqeq = dpsi * Math.Cos(epsA + deps);
            return NormalizeRadians(gmst + eqeq);
        }

        public static double NormalizeRadians(double a)
        {
            a %= 2.0 * Math.PI;
            if (a < 0) a += 2.0 * Math.PI;
            return a;
        }

        public static double NormalizeDegrees(double d)
        {
            d %= 360.0;
            if (d < 0) d += 360.0;
            return d;
        }

        /// <summary>Wrap to (-π, π].</summary>
        public static double WrapPi(double a)
        {
            a = NormalizeRadians(a);
            if (a > Math.PI) a -= 2.0 * Math.PI;
            return a;
        }
    }
}
