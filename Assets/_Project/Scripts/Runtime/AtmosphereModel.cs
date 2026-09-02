// Real Life Sky — CPU side of the physically based atmosphere.
// Mirrors RL_Atmosphere.hlsl exactly (same constants, same parametrisation) so that:
//   1) the Transmittance LUT (256x64) and Multiple-Scattering LUT (32x32) are generated here in double
//      precision once at start-up (they depend only on the planet, not on the Sun),
//   2) photometric quantities (sun illuminance at ground, sky luminance for exposure metering, ambient
//      irradiance for the ground) are computed on the CPU with the very same model the GPU renders.
// References: Bruneton & Neyret 2008 (EGSR) "Precomputed Atmospheric Scattering";
//             Hillaire 2020 (EGSR) "A Scalable and Production Ready Sky and Atmosphere Rendering Technique".
using System;
using UnityEngine;

namespace RealLife.Sky
{
    public static class AtmosphereModel
    {
        public const double Rg = 6360.0, Rt = 6460.0;
        public static readonly double[] RayleighScattering = { 5.802e-3, 13.558e-3, 33.100e-3 };
        public const double RayleighScaleHeight = 8.0;
        public const double MieScattering = 3.996e-3, MieAbsorption = 4.400e-3, MieScaleHeight = 1.2, MieG = 0.80;
        public static readonly double[] OzoneAbsorption = { 0.650e-3, 1.881e-3, 0.085e-3 };

        /// <summary>Top-of-atmosphere solar illuminance (lux) at 1 AU — 127,700 lx (Wyszecki & Stiles; 1361 W/m² spectrum).</summary>
        public const double SolarIlluminanceTOA = 127700.0;
        /// <summary>Photometric solar disc radiance (cd/m²) at 1 AU: E / (π sin²θ), θ = 0.2666°.</summary>
        public const double SolarDiscLuminance = 1.88e9;
        /// <summary>Per-channel TOA spectral weights (sun colour in linear sRGB, luminance-normalised, 5772 K blackbody through sRGB).</summary>
        public static readonly double[] SunColorLinear = { 1.0, 0.9663, 0.9140 }; // normalised to unit luminance (Rec.709 weights)

        public const int TransmittanceW = 256, TransmittanceH = 64;
        public const int MultiScatN = 32;

        // ---------------------------------------------------------------- medium
        public static void Medium(double h, double[] scatR, out double scatM, double[] ext)
        {
            double dR = Math.Exp(-h / RayleighScaleHeight);
            double dM = Math.Exp(-h / MieScaleHeight);
            double dO = Math.Max(0.0, 1.0 - Math.Abs(h - 25.0) / 15.0);
            scatM = MieScattering * dM;
            for (int c = 0; c < 3; c++)
            {
                scatR[c] = RayleighScattering[c] * dR;
                ext[c] = scatR[c] + (MieScattering + MieAbsorption) * dM + OzoneAbsorption[c] * dO;
            }
        }
        public static double RayleighPhase(double c) => 3.0 / (16.0 * Math.PI) * (1.0 + c * c);
        public static double MiePhase(double c)
        {
            double g = MieG, g2 = g * g;
            double k = 3.0 / (8.0 * Math.PI) * (1.0 - g2) / (2.0 + g2);
            return k * (1.0 + c * c) / Math.Pow(Math.Max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5);
        }
        public static double DistanceToTop(double r, double mu)
        {
            double d = r * r * (mu * mu - 1.0) + Rt * Rt;
            return Math.Max(-r * mu + Math.Sqrt(Math.Max(d, 0.0)), 0.0);
        }
        public static bool HitsGround(double r, double mu) => mu < 0.0 && r * r * (mu * mu - 1.0) + Rg * Rg >= 0.0;
        public static double DistanceToGround(double r, double mu)
        {
            double d = r * r * (mu * mu - 1.0) + Rg * Rg;
            return Math.Max(-r * mu - Math.Sqrt(Math.Max(d, 0.0)), 0.0);
        }

        // ---------------------------------------------------------------- transmittance (numerical, exact model)
        /// <summary>Optical depth integral along a ray from (r, mu) to the top of atmosphere (or ground).</summary>
        public static void TransmittanceToBoundary(double r, double mu, double[] result, int steps = 48)
        {
            bool ground = HitsGround(r, mu);
            double dMax = ground ? DistanceToGround(r, mu) : DistanceToTop(r, mu);
            double dt = dMax / steps;
            double[] od = new double[3];
            double[] sR = new double[3], ext = new double[3];
            for (int i = 0; i < steps; i++)
            {
                double t = (i + 0.5) * dt;
                double rr = Math.Sqrt(t * t + 2.0 * r * mu * t + r * r);
                Medium(rr - Rg, sR, out _, ext);
                for (int c = 0; c < 3; c++) od[c] += ext[c] * dt;
            }
            for (int c = 0; c < 3; c++) result[c] = ground ? 0.0 : Math.Exp(-od[c]);
        }

        // LUT mapping identical to HLSL
        static void TransmittanceUV(double r, double mu, out double u, out double v)
        {
            double H = Math.Sqrt(Rt * Rt - Rg * Rg);
            double rho = Math.Sqrt(Math.Max(r * r - Rg * Rg, 0.0));
            double d = DistanceToTop(r, mu);
            double dMin = Rt - r, dMax = rho + H;
            u = (d - dMin) / Math.Max(dMax - dMin, 1e-6);
            v = rho / H;
        }
        static void TransmittanceRMu(double u, double v, out double r, out double mu)
        {
            double H = Math.Sqrt(Rt * Rt - Rg * Rg);
            double rho = H * v;
            r = Math.Sqrt(rho * rho + Rg * Rg);
            double dMin = Rt - r, dMax = rho + H;
            double d = dMin + u * (dMax - dMin);
            mu = d == 0.0 ? 1.0 : (H * H - rho * rho - d * d) / (2.0 * r * d);
            mu = Math.Max(-1.0, Math.Min(1.0, mu));
        }

        static float[] _transLut; // RGB floats, TransmittanceW*TransmittanceH*3
        static float[] _msLut;    // MultiScatN*MultiScatN*3

        public static Texture2D BuildTransmittanceLUT()
        {
            _transLut = new float[TransmittanceW * TransmittanceH * 3];
            var tex = new Texture2D(TransmittanceW, TransmittanceH, TextureFormat.RGBAHalf, false, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RL_TransmittanceLUT" };
            var cols = new Color[TransmittanceW * TransmittanceH];
            double[] t = new double[3];
            for (int j = 0; j < TransmittanceH; j++)
                for (int i = 0; i < TransmittanceW; i++)
                {
                    double u = (i + 0.5) / TransmittanceW, v = (j + 0.5) / TransmittanceH;
                    TransmittanceRMu(u, v, out double r, out double mu);
                    TransmittanceToBoundary(r, mu, t, 64);
                    int k = j * TransmittanceW + i;
                    _transLut[k * 3] = (float)t[0]; _transLut[k * 3 + 1] = (float)t[1]; _transLut[k * 3 + 2] = (float)t[2];
                    cols[k] = new Color((float)t[0], (float)t[1], (float)t[2], 1);
                }
            tex.SetPixels(cols); tex.Apply(false, false);
            return tex;
        }

        /// <summary>Bilinear sample of the CPU transmittance LUT (same as GPU).</summary>
        public static void Transmittance(double r, double mu, double[] outT)
        {
            if (_transLut == null) { TransmittanceToBoundary(r, mu, outT); return; }
            TransmittanceUV(r, mu, out double u, out double v);
            SampleLut(_transLut, TransmittanceW, TransmittanceH, u, v, outT);
        }
        static void SampleLut(float[] lut, int w, int h, double u, double v, double[] o)
        {
            double x = Math.Max(0, Math.Min(w - 1, u * w - 0.5)), y = Math.Max(0, Math.Min(h - 1, v * h - 0.5));
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            double fx = x - x0, fy = y - y0;
            for (int c = 0; c < 3; c++)
            {
                double a = lut[(y0 * w + x0) * 3 + c], b = lut[(y0 * w + x1) * 3 + c];
                double cc = lut[(y1 * w + x0) * 3 + c], d = lut[(y1 * w + x1) * 3 + c];
                o[c] = (a * (1 - fx) + b * fx) * (1 - fy) + (cc * (1 - fx) + d * fx) * fy;
            }
        }
        public static void TransmittanceToSun(double r, double muS, double[] o)
        {
            double sinH = Rg / r;
            double cosH = -Math.Sqrt(Math.Max(1.0 - sinH * sinH, 0.0));
            Transmittance(r, muS, o);
            double s = Smooth(-0.00465, 0.00465, muS - cosH);
            for (int c = 0; c < 3; c++) o[c] *= s;
        }
        static double Smooth(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }

        // ---------------------------------------------------------------- multiple scattering (Hillaire 2020, §5.2)
        /// <summary>Ψ_ms(r, muS): total multi-scattered luminance factor, isotropic approximation, per channel.</summary>
        public static Texture2D BuildMultiScatteringLUT()
        {
            _msLut = new float[MultiScatN * MultiScatN * 3];
            var tex = new Texture2D(MultiScatN, MultiScatN, TextureFormat.RGBAHalf, false, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RL_MultiScatLUT" };
            var cols = new Color[MultiScatN * MultiScatN];
            const int sqrtSamples = 8; // 64 directions
            double[] L2 = new double[3], fms = new double[3];
            double[] Lsum = new double[3], Fsum = new double[3];
            for (int j = 0; j < MultiScatN; j++)
                for (int i = 0; i < MultiScatN; i++)
                {
                    double muS = ((i + 0.5) / MultiScatN) * 2.0 - 1.0;
                    double r = Rg + ((j + 0.5) / MultiScatN) * (Rt - Rg);
                    Array.Clear(Lsum, 0, 3); Array.Clear(Fsum, 0, 3);
                    for (int a = 0; a < sqrtSamples; a++)
                        for (int b = 0; b < sqrtSamples; b++)
                        {
                            double theta = Math.PI * (a + 0.5) / sqrtSamples;
                            double phi = 2.0 * Math.PI * (b + 0.5) / sqrtSamples;
                            double dx = Math.Sin(theta) * Math.Cos(phi), dy = Math.Cos(theta), dz = Math.Sin(theta) * Math.Sin(phi);
                            double sx = Math.Sqrt(Math.Max(0, 1 - muS * muS)), sy = muS, sz = 0.0;
                            IntegrateScatteredLuminance(r, dx, dy, dz, sx, sy, sz, 20, true, L2, fms);
                            double w = Math.Sin(theta) * (Math.PI / sqrtSamples) * (2.0 * Math.PI / sqrtSamples) / (4.0 * Math.PI);
                            for (int c = 0; c < 3; c++) { Lsum[c] += L2[c] * w; Fsum[c] += fms[c] * w; }
                        }
                    int k = j * MultiScatN + i;
                    for (int c = 0; c < 3; c++)
                    {
                        double psi = Lsum[c] / Math.Max(1e-6, 1.0 - Fsum[c]);
                        _msLut[k * 3 + c] = (float)psi;
                    }
                    cols[k] = new Color(_msLut[k * 3], _msLut[k * 3 + 1], _msLut[k * 3 + 2], 1);
                }
            tex.SetPixels(cols); tex.Apply(false, false);
            return tex;
        }
        public static void MultiScattering(double r, double muS, double[] o)
        {
            if (_msLut == null) { o[0] = o[1] = o[2] = 0; return; }
            SampleLut(_msLut, MultiScatN, MultiScatN, muS * 0.5 + 0.5, (r - Rg) / (Rt - Rg), o);
        }

        /// <summary>
        /// Ray-march single scattering (+ multiple scattering from LUT if useMs) from a point at radius r along direction d,
        /// with Sun direction s (both in a local frame where +Y is up at the observer). Returns luminance per channel for unit
        /// TOA sun illuminance (multiply by sun illuminance & colour), and (for MS LUT generation) the multi-scattering
        /// transfer factor f_ms. Includes ground albedo bounce (0.3) when the ray hits the ground.
        /// </summary>
        public static void IntegrateScatteredLuminance(double r, double dx, double dy, double dz, double sx, double sy, double sz,
            int steps, bool forMsLut, double[] L, double[] fms)
        {
            double mu = dy; // cos zenith of view dir (observer at (0, r, 0))
            bool ground = HitsGround(r, mu);
            double tMax = ground ? DistanceToGround(r, mu) : DistanceToTop(r, mu);
            if (!forMsLut) tMax = Math.Min(tMax, 9e9);
            double cosTheta = dx * sx + dy * sy + dz * sz;
            double phR = RayleighPhase(cosTheta), phM = MiePhase(cosTheta);
            const double uniformPhase = 1.0 / (4.0 * Math.PI);
            double[] T = { 1, 1, 1 };
            double[] sR = new double[3], ext = new double[3], tSun = new double[3], ms = new double[3];
            Array.Clear(L, 0, 3); if (fms != null) Array.Clear(fms, 0, 3);
            double dt = tMax / steps;
            for (int i = 0; i < steps; i++)
            {
                double t = (i + 0.5) * dt;
                double px = dx * t, py = r + dy * t, pz = dz * t;
                double pr = Math.Sqrt(px * px + py * py + pz * pz);
                double h = pr - Rg;
                Medium(h, sR, out double sM, ext);
                double muS = (px * sx + py * sy + pz * sz) / pr;
                TransmittanceToSun(pr, muS, tSun);
                if (!forMsLut) MultiScattering(pr, muS, ms); else { ms[0] = ms[1] = ms[2] = 0; }
                for (int c = 0; c < 3; c++)
                {
                    double sampleT = Math.Exp(-ext[c] * dt);
                    double scat = forMsLut ? (sR[c] + sM) * uniformPhase : (sR[c] * phR + sM * phM);
                    double S = scat * tSun[c] + (sR[c] + sM) * ms[c];
                    double integ = (S - S * sampleT) / Math.Max(ext[c], 1e-9);
                    L[c] += T[c] * integ;
                    if (fms != null)
                    {
                        double Sm = (sR[c] + sM);
                        fms[c] += T[c] * (Sm - Sm * sampleT) / Math.Max(ext[c], 1e-9);
                    }
                    T[c] *= sampleT;
                }
            }
            if (ground)
            {
                // Lambertian ground bounce, albedo 0.3
                double gx = dx * tMax, gy = r + dy * tMax, gz = dz * tMax;
                double gr = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                double muS = (gx * sx + gy * sy + gz * sz) / gr;
                TransmittanceToSun(gr, muS, tSun);
                double nDotL = Math.Max(0, muS);
                for (int c = 0; c < 3; c++) L[c] += T[c] * tSun[c] * nDotL * 0.3 / Math.PI;
            }
        }

        // ---------------------------------------------------------------- photometry helpers used by the runtime
        /// <summary>Sun illuminance (lux) on a surface normal to the Sun at the observer (r, muS), per channel (linear sRGB).</summary>
        public static void SunIlluminanceAtObserver(double r, double muS, double sunDistAu, double[] o)
        {
            double[] T = new double[3]; TransmittanceToSun(r, muS, T);
            double E = SolarIlluminanceTOA / (sunDistAu * sunDistAu);
            for (int c = 0; c < 3; c++) o[c] = E * SunColorLinear[c] * T[c];
        }

        /// <summary>Sky luminance (cd/m²) toward a direction (Unity world frame: +Y up), per channel.</summary>
        public static void SkyLuminance(double r, Vector3 dir, Vector3 sunDir, double sunDistAu, double[] o, int steps = 24)
        {
            double[] L = new double[3];
            IntegrateScatteredLuminance(r, dir.x, dir.y, dir.z, sunDir.x, sunDir.y, sunDir.z, steps, false, L, null);
            double E = SolarIlluminanceTOA / (sunDistAu * sunDistAu);
            for (int c = 0; c < 3; c++) o[c] = L[c] * E * SunColorLinear[c];
        }

        /// <summary>Hemispherical sky irradiance on an upward-facing surface (lux), per channel, cosine weighted, 6x12 samples.</summary>
        public static void SkyIrradianceHorizontal(double r, Vector3 sunDir, double sunDistAu, double[] o)
        {
            double[] L = new double[3]; Array.Clear(o, 0, 3);
            const int nT = 6, nP = 12;
            for (int a = 0; a < nT; a++)
                for (int b = 0; b < nP; b++)
                {
                    double theta = (a + 0.5) / nT * (Math.PI / 2), phi = (b + 0.5) / nP * 2 * Math.PI;
                    var d = new Vector3((float)(Math.Sin(theta) * Math.Cos(phi)), (float)Math.Cos(theta), (float)(Math.Sin(theta) * Math.Sin(phi)));
                    SkyLuminance(r, d, sunDir, sunDistAu, L, 12);
                    double w = Math.Cos(theta) * Math.Sin(theta) * (Math.PI / 2 / nT) * (2 * Math.PI / nP);
                    for (int c = 0; c < 3; c++) o[c] += L[c] * w;
                }
        }
    }
}
