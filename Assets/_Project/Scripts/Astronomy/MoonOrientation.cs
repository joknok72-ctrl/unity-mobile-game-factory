// Real Life Sky — Moon orientation (libration & position angle of axis)
// Meeus, Astronomical Algorithms 2nd ed., chapter 53 (optical + physical libration),
// selenographic coordinates in the mean-Earth/polar-axis system. Accuracy ≈ 0.01°..0.03°
// in libration; sufficient to place lunar features to ~5 km at the limb — invisible from Earth.
using System;

namespace RealLife.Astronomy
{
    public struct MoonAspect
    {
        public double LibrationLonRad;   // l  : selenographic longitude of Earth (total = optical+physical)
        public double LibrationLatRad;   // b  : selenographic latitude of Earth
        public double AxisPositionAngleRad; // P : position angle of the Moon's north pole (from celestial north, eastward)
        public double SubsolarLonRad;    // l0 : selenographic longitude of the Sun
        public double SubsolarLatRad;    // b0
        public double SubsolarColongitudeRad; // c0 = 90° - l0
    }

    public static class MoonOrientation
    {
        const double I_deg = 1.54242; // inclination of mean lunar equator to ecliptic

        /// <summary>
        /// Total libration and axis position angle for an apparent geocentric Moon
        /// (lambda, beta: apparent ecliptic lon/lat of date; alpha, delta: apparent RA/Dec of date).
        /// For a topocentric observer pass the topocentric λ, β (we do so in SkyModel).
        /// </summary>
        public static MoonAspect Compute(double jdTt, double lambdaRad, double betaRad, double alphaRad, double deltaRad,
            double sunLambdaRad, double sunDistAu, double moonDistKm)
        {
            double d2r = AstroTime.Deg2Rad;
            double T = AstroTime.CenturiesSinceJ2000(jdTt);
            // Mean elements (Meeus ch. 47)
            double D  = AstroTime.NormalizeDegrees(297.8501921 + 445267.1114034 * T - 0.0018819 * T * T + T * T * T / 545868.0 - T * T * T * T / 113065000.0);
            double M  = AstroTime.NormalizeDegrees(357.5291092 + 35999.0502909 * T - 0.0001536 * T * T + T * T * T / 24490000.0);
            double Mp = AstroTime.NormalizeDegrees(134.9633964 + 477198.8675055 * T + 0.0087414 * T * T + T * T * T / 69699.0 - T * T * T * T / 14712000.0);
            double F  = AstroTime.NormalizeDegrees(93.2720950 + 483202.0175233 * T - 0.0036539 * T * T - T * T * T / 3526000.0 + T * T * T * T / 863310000.0);
            double Om = AstroTime.NormalizeDegrees(125.0445479 - 1934.1362891 * T + 0.0020754 * T * T + T * T * T / 467441.0 - T * T * T * T / 60616000.0);
            double E = 1.0 - 0.002516 * T - 0.0000074 * T * T;

            Nutation.Compute(jdTt, out double dpsi, out double deps);
            double eps = Nutation.TrueObliquity(jdTt);
            double I = I_deg * d2r;

            // ---- Optical libration (53.1) ----
            double W = lambdaRad - dpsi - Om * d2r;
            double sinB = Math.Sin(betaRad), cosB = Math.Cos(betaRad);
            double A = Math.Atan2(Math.Sin(W) * cosB * Math.Cos(I) - sinB * Math.Sin(I), Math.Cos(W) * cosB);
            double lp = A - F * d2r; // l'
            lp = AstroTime.WrapPi(lp);
            double bp = Math.Asin(-Math.Sin(W) * cosB * Math.Sin(I) - sinB * Math.Cos(I)); // b'

            // ---- Physical libration (53.2) ----
            double K1 = (119.75 + 131.849 * T) * d2r;
            double K2 = (72.56 + 20.186 * T) * d2r;
            double Dr = D * d2r, Mr = M * d2r, Mpr = Mp * d2r, Fr = F * d2r;
            double rho = -0.02752 * Math.Cos(Mpr) - 0.02245 * Math.Sin(Fr) + 0.00684 * Math.Cos(Mpr - 2 * Fr)
                         - 0.00293 * Math.Cos(2 * Fr) - 0.00085 * Math.Cos(2 * Fr - 2 * Dr) - 0.00054 * Math.Cos(Mpr - 2 * Dr)
                         - 0.00020 * Math.Sin(Mpr + Fr) - 0.00020 * Math.Cos(Mpr + 2 * Fr) - 0.00020 * Math.Cos(Mpr - Fr)
                         + 0.00014 * Math.Cos(Mpr + 2 * Fr - 2 * Dr);
            double sigma = -0.02816 * Math.Sin(Mpr) + 0.02244 * Math.Cos(Fr) - 0.00682 * Math.Sin(Mpr - 2 * Fr)
                           - 0.00279 * Math.Sin(2 * Fr) - 0.00083 * Math.Sin(2 * Fr - 2 * Dr) + 0.00069 * Math.Sin(Mpr - 2 * Dr)
                           + 0.00040 * Math.Cos(Mpr + Fr) - 0.00025 * Math.Sin(2 * Mpr) - 0.00023 * Math.Sin(Mpr + 2 * Fr)
                           + 0.00020 * Math.Cos(Mpr - Fr) + 0.00019 * Math.Sin(Mpr - Fr) + 0.00013 * Math.Sin(Mpr + 2 * Fr - 2 * Dr)
                           - 0.00010 * Math.Cos(Mpr - 3 * Fr);
            double tau = 0.02520 * E * Math.Sin(Mr) + 0.00473 * Math.Sin(2 * Mpr - 2 * Fr) - 0.00467 * Math.Sin(Mpr)
                         + 0.00396 * Math.Sin(K1) + 0.00276 * Math.Sin(2 * Mpr - 2 * Dr) + 0.00196 * Math.Sin(Om * d2r)
                         - 0.00183 * Math.Cos(Mpr - Fr) + 0.00115 * Math.Sin(Mpr - 2 * Dr) - 0.00096 * Math.Sin(Mpr - Dr)
                         + 0.00046 * Math.Sin(2 * Fr - 2 * Dr) - 0.00039 * Math.Sin(Mpr - Fr) - 0.00032 * Math.Sin(Mpr - Mr - Dr)
                         + 0.00027 * Math.Sin(2 * Mpr - Mr - 2 * Dr) + 0.00023 * Math.Sin(K2) - 0.00014 * Math.Sin(2 * Dr)
                         + 0.00014 * Math.Cos(2 * Mpr - 2 * Fr) - 0.00012 * Math.Sin(Mpr - 2 * Fr) - 0.00012 * Math.Sin(2 * Mpr)
                         + 0.00011 * Math.Sin(2 * Mpr - 2 * Mr - 2 * Dr);
            rho *= d2r; sigma *= d2r; tau *= d2r;

            double lpp = -tau + (rho * Math.Cos(A) + sigma * Math.Sin(A)) * Math.Tan(bp); // l''
            double bpp = sigma * Math.Cos(A) - rho * Math.Sin(A);                          // b''

            var r = new MoonAspect();
            r.LibrationLonRad = lp + lpp;
            r.LibrationLatRad = bp + bpp;

            // ---- Position angle of the axis (53.3) ----
            double V = Om * d2r + dpsi + sigma / Math.Sin(I);
            double X = Math.Sin(I + rho) * Math.Sin(V);
            double Y = Math.Sin(I + rho) * Math.Cos(V) * Math.Cos(eps) - Math.Cos(I + rho) * Math.Sin(eps);
            double omega = Math.Atan2(X, Y);
            double sinP = Math.Sqrt(X * X + Y * Y) * Math.Cos(alphaRad - omega) / Math.Cos(r.LibrationLatRad);
            r.AxisPositionAngleRad = Math.Asin(Math.Max(-1, Math.Min(1, sinP)));

            // ---- Selenographic position of the Sun (53.4) ----
            double R = sunDistAu * 149597870.7; // km
            double Delta = moonDistKm;
            double lambdaH = sunLambdaRad + Math.PI + (Delta / R) * Math.Cos(betaRad) * Math.Sin(sunLambdaRad - lambdaRad);
            double betaH = (Delta / R) * betaRad;
            double WH = lambdaH - dpsi - Om * d2r;
            double AH = Math.Atan2(Math.Sin(WH) * Math.Cos(betaH) * Math.Cos(I) - Math.Sin(betaH) * Math.Sin(I), Math.Cos(WH) * Math.Cos(betaH));
            double l0p = AstroTime.WrapPi(AH - Fr);
            double b0p = Math.Asin(-Math.Sin(WH) * Math.Cos(betaH) * Math.Sin(I) - Math.Sin(betaH) * Math.Cos(I));
            double l0pp = -tau + (rho * Math.Cos(AH) + sigma * Math.Sin(AH)) * Math.Tan(b0p);
            double b0pp = sigma * Math.Cos(AH) - rho * Math.Sin(AH);
            r.SubsolarLonRad = l0p + l0pp;
            r.SubsolarLatRad = b0p + b0pp;
            r.SubsolarColongitudeRad = AstroTime.NormalizeRadians(Math.PI / 2 - r.SubsolarLonRad);
            return r;
        }
    }
}
