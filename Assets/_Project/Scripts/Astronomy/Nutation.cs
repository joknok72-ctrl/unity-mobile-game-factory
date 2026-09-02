// Real Life Sky — Astronomy Core
// IAU 2000B nutation (77 luni-solar terms, McCarthy & Luzum 2003) — accuracy ~1 mas 1995-2050.
// Coefficients transcribed from the IAU SOFA / ERFA reference implementation (nut00b).
// Mean obliquity: IAU 2006 (Capitaine et al. 2003). Precession: IAU 2006/2000A Fukushima–Williams angles.
using System;

namespace RealLife.Astronomy
{
    public static class Nutation
    {
        private struct Term
        {
            public readonly int Nl, Nlp, Nf, Nd, Nom;
            public readonly double Ps, Pst, Pc, Ec, Ect, Es;
            public Term(int nl, int nlp, int nf, int nd, int nom, double ps, double pst, double pc, double ec, double ect, double es)
            { Nl = nl; Nlp = nlp; Nf = nf; Nd = nd; Nom = nom; Ps = ps; Pst = pst; Pc = pc; Ec = ec; Ect = ect; Es = es; }
        }

        private static readonly Term[] Series =
        {
            new Term(0, 0, 0, 0, 1, -172064161.0, -174666.0, 33386.0, 92052331.0, 9086.0, 15377.0),
            new Term(0, 0, 2, -2, 2, -13170906.0, -1675.0, -13696.0, 5730336.0, -3015.0, -4587.0),
            new Term(0, 0, 2, 0, 2, -2276413.0, -234.0, 2796.0, 978459.0, -485.0, 1374.0),
            new Term(0, 0, 0, 0, 2, 2074554.0, 207.0, -698.0, -897492.0, 470.0, -291.0),
            new Term(0, 1, 0, 0, 0, 1475877.0, -3633.0, 11817.0, 73871.0, -184.0, -1924.0),
            new Term(0, 1, 2, -2, 2, -516821.0, 1226.0, -524.0, 224386.0, -677.0, -174.0),
            new Term(1, 0, 0, 0, 0, 711159.0, 73.0, -872.0, -6750.0, 0.0, 358.0),
            new Term(0, 0, 2, 0, 1, -387298.0, -367.0, 380.0, 200728.0, 18.0, 318.0),
            new Term(1, 0, 2, 0, 2, -301461.0, -36.0, 816.0, 129025.0, -63.0, 367.0),
            new Term(0, -1, 2, -2, 2, 215829.0, -494.0, 111.0, -95929.0, 299.0, 132.0),
            new Term(0, 0, 2, -2, 1, 128227.0, 137.0, 181.0, -68982.0, -9.0, 39.0),
            new Term(-1, 0, 2, 0, 2, 123457.0, 11.0, 19.0, -53311.0, 32.0, -4.0),
            new Term(-1, 0, 0, 2, 0, 156994.0, 10.0, -168.0, -1235.0, 0.0, 82.0),
            new Term(1, 0, 0, 0, 1, 63110.0, 63.0, 27.0, -33228.0, 0.0, -9.0),
            new Term(-1, 0, 0, 0, 1, -57976.0, -63.0, -189.0, 31429.0, 0.0, -75.0),
            new Term(-1, 0, 2, 2, 2, -59641.0, -11.0, 149.0, 25543.0, -11.0, 66.0),
            new Term(1, 0, 2, 0, 1, -51613.0, -42.0, 129.0, 26366.0, 0.0, 78.0),
            new Term(-2, 0, 2, 0, 1, 45893.0, 50.0, 31.0, -24236.0, -10.0, 20.0),
            new Term(0, 0, 0, 2, 0, 63384.0, 11.0, -150.0, -1220.0, 0.0, 29.0),
            new Term(0, 0, 2, 2, 2, -38571.0, -1.0, 158.0, 16452.0, -11.0, 68.0),
            new Term(0, -2, 2, -2, 2, 32481.0, 0.0, 0.0, -13870.0, 0.0, 0.0),
            new Term(-2, 0, 0, 2, 0, -47722.0, 0.0, -18.0, 477.0, 0.0, -25.0),
            new Term(2, 0, 2, 0, 2, -31046.0, -1.0, 131.0, 13238.0, -11.0, 59.0),
            new Term(1, 0, 2, -2, 2, 28593.0, 0.0, -1.0, -12338.0, 10.0, -3.0),
            new Term(-1, 0, 2, 0, 1, 20441.0, 21.0, 10.0, -10758.0, 0.0, -3.0),
            new Term(2, 0, 0, 0, 0, 29243.0, 0.0, -74.0, -609.0, 0.0, 13.0),
            new Term(0, 0, 2, 0, 0, 25887.0, 0.0, -66.0, -550.0, 0.0, 11.0),
            new Term(0, 1, 0, 0, 1, -14053.0, -25.0, 79.0, 8551.0, -2.0, -45.0),
            new Term(-1, 0, 0, 2, 1, 15164.0, 10.0, 11.0, -8001.0, 0.0, -1.0),
            new Term(0, 2, 2, -2, 2, -15794.0, 72.0, -16.0, 6850.0, -42.0, -5.0),
            new Term(0, 0, -2, 2, 0, 21783.0, 0.0, 13.0, -167.0, 0.0, 13.0),
            new Term(1, 0, 0, -2, 1, -12873.0, -10.0, -37.0, 6953.0, 0.0, -14.0),
            new Term(0, -1, 0, 0, 1, -12654.0, 11.0, 63.0, 6415.0, 0.0, 26.0),
            new Term(-1, 0, 2, 2, 1, -10204.0, 0.0, 25.0, 5222.0, 0.0, 15.0),
            new Term(0, 2, 0, 0, 0, 16707.0, -85.0, -10.0, 168.0, -1.0, 10.0),
            new Term(1, 0, 2, 2, 2, -7691.0, 0.0, 44.0, 3268.0, 0.0, 19.0),
            new Term(-2, 0, 2, 0, 0, -11024.0, 0.0, -14.0, 104.0, 0.0, 2.0),
            new Term(0, 1, 2, 0, 2, 7566.0, -21.0, -11.0, -3250.0, 0.0, -5.0),
            new Term(0, 0, 2, 2, 1, -6637.0, -11.0, 25.0, 3353.0, 0.0, 14.0),
            new Term(0, -1, 2, 0, 2, -7141.0, 21.0, 8.0, 3070.0, 0.0, 4.0),
            new Term(0, 0, 0, 2, 1, -6302.0, -11.0, 2.0, 3272.0, 0.0, 4.0),
            new Term(1, 0, 2, -2, 1, 5800.0, 10.0, 2.0, -3045.0, 0.0, -1.0),
            new Term(2, 0, 2, -2, 2, 6443.0, 0.0, -7.0, -2768.0, 0.0, -4.0),
            new Term(-2, 0, 0, 2, 1, -5774.0, -11.0, -15.0, 3041.0, 0.0, -5.0),
            new Term(2, 0, 2, 0, 1, -5350.0, 0.0, 21.0, 2695.0, 0.0, 12.0),
            new Term(0, -1, 2, -2, 1, -4752.0, -11.0, -3.0, 2719.0, 0.0, -3.0),
            new Term(0, 0, 0, -2, 1, -4940.0, -11.0, -21.0, 2720.0, 0.0, -9.0),
            new Term(-1, -1, 0, 2, 0, 7350.0, 0.0, -8.0, -51.0, 0.0, 4.0),
            new Term(2, 0, 0, -2, 1, 4065.0, 0.0, 6.0, -2206.0, 0.0, 1.0),
            new Term(1, 0, 0, 2, 0, 6579.0, 0.0, -24.0, -199.0, 0.0, 2.0),
            new Term(0, 1, 2, -2, 1, 3579.0, 0.0, 5.0, -1900.0, 0.0, 1.0),
            new Term(1, -1, 0, 0, 0, 4725.0, 0.0, -6.0, -41.0, 0.0, 3.0),
            new Term(-2, 0, 2, 0, 2, -3075.0, 0.0, -2.0, 1313.0, 0.0, -1.0),
            new Term(3, 0, 2, 0, 2, -2904.0, 0.0, 15.0, 1233.0, 0.0, 7.0),
            new Term(0, -1, 0, 2, 0, 4348.0, 0.0, -10.0, -81.0, 0.0, 2.0),
            new Term(1, -1, 2, 0, 2, -2878.0, 0.0, 8.0, 1232.0, 0.0, 4.0),
            new Term(0, 0, 0, 1, 0, -4230.0, 0.0, 5.0, -20.0, 0.0, -2.0),
            new Term(-1, -1, 2, 2, 2, -2819.0, 0.0, 7.0, 1207.0, 0.0, 3.0),
            new Term(-1, 0, 2, 0, 0, -4056.0, 0.0, 5.0, 40.0, 0.0, -2.0),
            new Term(0, -1, 2, 2, 2, -2647.0, 0.0, 11.0, 1129.0, 0.0, 5.0),
            new Term(-2, 0, 0, 0, 1, -2294.0, 0.0, -10.0, 1266.0, 0.0, -4.0),
            new Term(1, 1, 2, 0, 2, 2481.0, 0.0, -7.0, -1062.0, 0.0, -3.0),
            new Term(2, 0, 0, 0, 1, 2179.0, 0.0, -2.0, -1129.0, 0.0, -2.0),
            new Term(-1, 1, 0, 1, 0, 3276.0, 0.0, 1.0, -9.0, 0.0, 0.0),
            new Term(1, 1, 0, 0, 0, -3389.0, 0.0, 5.0, 35.0, 0.0, -2.0),
            new Term(1, 0, 2, 0, 0, 3339.0, 0.0, -13.0, -107.0, 0.0, 1.0),
            new Term(-1, 0, 2, -2, 1, -1987.0, 0.0, -6.0, 1073.0, 0.0, -2.0),
            new Term(1, 0, 0, 0, 2, -1981.0, 0.0, 0.0, 854.0, 0.0, 0.0),
            new Term(-1, 0, 0, 1, 0, 4026.0, 0.0, -353.0, -553.0, 0.0, -139.0),
            new Term(0, 0, 2, 1, 2, 1660.0, 0.0, -5.0, -710.0, 0.0, -2.0),
            new Term(-1, 0, 2, 4, 2, -1521.0, 0.0, 9.0, 647.0, 0.0, 4.0),
            new Term(-1, 1, 0, 1, 1, 1314.0, 0.0, 0.0, -700.0, 0.0, 0.0),
            new Term(0, -2, 2, -2, 1, -1283.0, 0.0, 0.0, 672.0, 0.0, 0.0),
            new Term(1, 0, 2, 2, 1, -1331.0, 0.0, 8.0, 663.0, 0.0, 4.0),
            new Term(-2, 0, 2, 2, 2, 1383.0, 0.0, -2.0, -594.0, 0.0, -2.0),
            new Term(-1, 0, 0, 0, 2, 1405.0, 0.0, 4.0, -610.0, 0.0, 2.0),
            new Term(1, 1, 2, -2, 2, 1290.0, 0.0, 0.0, -556.0, 0.0, 0.0),
        };

        private const double TurnAs = 1296000.0;                 // arcsec per turn
        private const double Das2R = Math.PI / (180.0 * 3600.0); // arcsec -> rad
        private const double U2R = Das2R / 1e7;                  // 0.1 µas -> rad
        private const double Dmas2R = Das2R / 1e3;
        private const double DpPlan = -0.135 * Dmas2R;           // fixed offset in lieu of planetary nutation
        private const double DePlan = 0.388 * Dmas2R;

        /// <summary>Delaunay fundamental arguments (radians), Simon et al. 1994 as used by IAU 2000B.</summary>
        public static void FundamentalArguments(double t, out double l, out double lp, out double f, out double d, out double om)
        {
            l  = ((485868.249036 + 1717915923.2178 * t) % TurnAs) * Das2R;
            lp = ((1287104.79305 + 129596581.0481 * t) % TurnAs) * Das2R;
            f  = ((335779.526232 + 1739527262.8478 * t) % TurnAs) * Das2R;
            d  = ((1072260.70369 + 1602961601.2090 * t) % TurnAs) * Das2R;
            om = ((450160.398036 - 6962890.5431 * t) % TurnAs) * Das2R;
        }

        /// <summary>Nutation in longitude (dpsi) and obliquity (deps), radians, for TT Julian date.</summary>
        public static void Compute(double jdTt, out double dpsi, out double deps)
        {
            double t = AstroTime.CenturiesSinceJ2000(jdTt);
            FundamentalArguments(t, out double el, out double elp, out double f, out double d, out double om);
            double dp = 0, de = 0;
            for (int i = Series.Length - 1; i >= 0; i--)
            {
                ref readonly Term x = ref Series[i];
                double arg = (x.Nl * el + x.Nlp * elp + x.Nf * f + x.Nd * d + x.Nom * om) % (2.0 * Math.PI);
                double s = Math.Sin(arg), c = Math.Cos(arg);
                dp += (x.Ps + x.Pst * t) * s + x.Pc * c;
                de += (x.Ec + x.Ect * t) * c + x.Es * s;
            }
            dpsi = dp * U2R + DpPlan;
            deps = de * U2R + DePlan;
        }

        /// <summary>Mean obliquity of the ecliptic (radians), IAU 2006.</summary>
        public static double MeanObliquity(double jdTt)
        {
            double t = AstroTime.CenturiesSinceJ2000(jdTt);
            double eps = 84381.406 + (-46.836769 + (-0.0001831 + (0.00200340 + (-0.000000576 + (-0.0000000434) * t) * t) * t) * t) * t;
            return eps * Das2R;
        }

        /// <summary>True obliquity = mean + deps.</summary>
        public static double TrueObliquity(double jdTt)
        {
            Compute(jdTt, out _, out double deps);
            return MeanObliquity(jdTt) + deps;
        }
    }

    /// <summary>
    /// Precession, IAU 2006 (Fukushima–Williams parametrization; Capitaine, Wallace & Chapront 2003; Hilton et al. 2006).
    /// Builds the bias-precession(-nutation) rotation matrix GCRS -> true equator & equinox of date.
    /// </summary>
    public static class Precession
    {
        private const double Das2R = Math.PI / (180.0 * 3600.0);

        /// <summary>Fukushima–Williams angles gamma_bar, phi_bar, psi_bar (radians) and mean obliquity epsA.</summary>
        public static void FukushimaWilliams(double jdTt, out double gamb, out double phib, out double psib, out double epsa)
        {
            double t = AstroTime.CenturiesSinceJ2000(jdTt);
            gamb = (-0.052928 + (10.556378 + (0.4932044 + (-0.00031238 + (-0.000002788 + 0.0000000260 * t) * t) * t) * t) * t) * Das2R;
            phib = (84381.412819 + (-46.811016 + (0.0511268 + (0.00053289 + (-0.000000440 + (-0.0000000176) * t) * t) * t) * t) * t) * Das2R;
            psib = (-0.041775 + (5038.481484 + (1.5584175 + (-0.00018522 + (-0.000026452 + (-0.0000000148) * t) * t) * t) * t) * t) * Das2R;
            epsa = Nutation.MeanObliquity(jdTt);
        }

        /// <summary>
        /// 3x3 rotation matrix (row-major) transforming GCRS/ICRS (≈J2000 mean) vectors to the true equator and equinox of date
        /// (bias + precession + nutation). r_true = M * r_icrs.
        /// </summary>
        public static Mat3 BiasPrecessionNutationMatrix(double jdTt)
        {
            FukushimaWilliams(jdTt, out double gamb, out double phib, out double psib, out double epsa);
            Nutation.Compute(jdTt, out double dpsi, out double deps);
            // SOFA eraFw2m: R = Rx(-(eps+deps)) * Rz(-(psi+dpsi)) * Rx(phi) * Rz(gam)
            Mat3 m = Mat3.Identity;
            m = Mat3.RotZ(gamb) * m;
            m = Mat3.RotX(phib) * m;
            m = Mat3.RotZ(-(psib + dpsi)) * m;
            m = Mat3.RotX(-(epsa + deps)) * m;
            return m;
        }

        /// <summary>Bias-precession only (mean equator & equinox of date).</summary>
        public static Mat3 BiasPrecessionMatrix(double jdTt)
        {
            FukushimaWilliams(jdTt, out double gamb, out double phib, out double psib, out double epsa);
            Mat3 m = Mat3.Identity;
            m = Mat3.RotZ(gamb) * m;
            m = Mat3.RotX(phib) * m;
            m = Mat3.RotZ(-psib) * m;
            m = Mat3.RotX(-epsa) * m;
            return m;
        }
    }

    /// <summary>Minimal double-precision 3-vector / 3x3 matrix (Unity's Vector3 is float — not precise enough here).</summary>
    public struct Vec3d
    {
        public double X, Y, Z;
        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vec3d Normalized { get { double l = Length; return l > 0 ? new Vec3d(X / l, Y / l, Z / l) : this; } }
        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.X * s, a.Y * s, a.Z * s);
        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.X, -a.Y, -a.Z);
        public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3d Cross(Vec3d a, Vec3d b) => new Vec3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        /// <summary>Unit vector from spherical (lon/RA, lat/Dec) radians: x toward lon=0, z toward pole.</summary>
        public static Vec3d FromSpherical(double lon, double lat)
        {
            double cl = Math.Cos(lat);
            return new Vec3d(cl * Math.Cos(lon), cl * Math.Sin(lon), Math.Sin(lat));
        }

        public void ToSpherical(out double lon, out double lat)
        {
            lon = AstroTime.NormalizeRadians(Math.Atan2(Y, X));
            lat = Math.Atan2(Z, Math.Sqrt(X * X + Y * Y));
        }
    }

    public struct Mat3
    {
        public double M00, M01, M02, M10, M11, M12, M20, M21, M22;
        public static Mat3 Identity => new Mat3 { M00 = 1, M11 = 1, M22 = 1 };

        public static Mat3 RotX(double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Mat3 { M00 = 1, M11 = c, M12 = s, M21 = -s, M22 = c };
        }
        public static Mat3 RotZ(double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Mat3 { M00 = c, M01 = s, M10 = -s, M11 = c, M22 = 1 };
        }
        public static Mat3 operator *(Mat3 a, Mat3 b)
        {
            return new Mat3
            {
                M00 = a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20, M01 = a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21, M02 = a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
                M10 = a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20, M11 = a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21, M12 = a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
                M20 = a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20, M21 = a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21, M22 = a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
            };
        }
        public static Vec3d operator *(Mat3 m, Vec3d v)
        {
            return new Vec3d(m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
                             m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
                             m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);
        }
        public Mat3 Transposed => new Mat3 { M00 = M00, M01 = M10, M02 = M20, M10 = M01, M11 = M11, M12 = M21, M20 = M02, M21 = M12, M22 = M22 };
    }
}
