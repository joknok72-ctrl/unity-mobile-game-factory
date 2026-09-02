#ifndef RL_ATMOSPHERE_INCLUDED
#define RL_ATMOSPHERE_INCLUDED
// ============================================================================
// Real Life Sky — physically based atmosphere (HLSL side)
// Model: Bruneton & Neyret 2008 / Hillaire 2020 parametrisation of Earth's atmosphere.
//   Rayleigh (molecules), Mie (aerosols, Cornette–Shanks phase), ozone absorption layer.
//   Units: kilometres for lengths, 1/km for coefficients, radiance in cd/m^2-equivalent
//   (spectral sun irradiance scaled so that TOA illuminance = 127,700 lx).
// The C# class RealLife.Sky.AtmosphereModel implements EXACTLY the same functions for
// CPU-side LUT generation and photometric metering (exposure). Keep them in sync.
// ============================================================================

#define RL_PI 3.14159265358979323846

// --- Earth ---
static const float RL_Rg = 6360.0;   // ground radius (km)
static const float RL_Rt = 6460.0;   // top of atmosphere (km)

// --- Rayleigh (sea-level scattering coefficient, 1/km, for 680/550/440 nm -> R/G/B) ---
static const float3 RL_RayleighScattering = float3(5.802e-3, 13.558e-3, 33.100e-3);
static const float  RL_RayleighScaleHeight = 8.0;
// --- Mie (1/km) ---
static const float  RL_MieScattering = 3.996e-3;
static const float  RL_MieAbsorption = 4.400e-3;
static const float  RL_MieScaleHeight = 1.2;
static const float  RL_MieG = 0.80;
// --- Ozone absorption (1/km), tent profile centred 25 km, half-width 15 km ---
static const float3 RL_OzoneAbsorption = float3(0.650e-3, 1.881e-3, 0.085e-3);

// Global uniforms (set by CelestialDirector / ExposureController)
float3 _RL_SunDir;            // world (Unity) unit vector to the Sun (true/geometric)
float3 _RL_SunDirApparent;    // refracted direction (what you see)
float3 _RL_SunRadiance;       // disc radiance at TOA per channel (cd/m^2 eq.), incl. distance
float3 _RL_SunIlluminance;    // TOA illuminance per channel (lux eq.)
float  _RL_SunAngularRadius;  // radians
float  _RL_ObserverHeightKm;  // altitude above sea level (km)
float  _RL_Exposure;          // physical exposure multiplier (radiance -> display linear)
float  _RL_RefractionScale;   // (P/1010)*(283/(273+T))
float3 _RL_MoonDir;           // true direction
float  _RL_MoonAngularRadius;
float  _RL_Time;
float3 _RL_NightSkyRadiance;  // airglow + zodiacal + unresolved starlight at zenith

TEXTURE2D(_RL_TransmittanceLUT); SAMPLER(sampler_RL_TransmittanceLUT);
TEXTURE2D(_RL_MultiScatLUT);     SAMPLER(sampler_RL_MultiScatLUT);
TEXTURE2D(_RL_SkyViewLUT);       SAMPLER(sampler_RL_SkyViewLUT);

// ---------------------------------------------------------------------------
// Geometry helpers
// ---------------------------------------------------------------------------
// Distance along ray (origin at radius r, cos zenith mu) to sphere of radius R; -1 if none.
float RL_RaySphere(float r, float mu, float R)
{
    float b = r * mu;
    float c = r * r - R * R;
    float disc = b * b - c;
    if (disc < 0.0) return -1.0;
    float s = sqrt(disc);
    float t0 = -b - s, t1 = -b + s;
    if (t0 > 0.0) return t0;
    if (t1 > 0.0) return t1;
    return -1.0;
}
float RL_DistanceToTop(float r, float mu)
{
    float d = r * r * (mu * mu - 1.0) + RL_Rt * RL_Rt;
    return max(-r * mu + sqrt(max(d, 0.0)), 0.0);
}
bool RL_HitsGround(float r, float mu)
{
    return mu < 0.0 && r * r * (mu * mu - 1.0) + RL_Rg * RL_Rg >= 0.0;
}

// Medium properties at altitude h (km above ground)
void RL_Medium(float h, out float3 scatR, out float scatM, out float3 extinction)
{
    float dR = exp(-h / RL_RayleighScaleHeight);
    float dM = exp(-h / RL_MieScaleHeight);
    float dO = max(0.0, 1.0 - abs(h - 25.0) / 15.0);
    scatR = RL_RayleighScattering * dR;
    scatM = RL_MieScattering * dM;
    extinction = scatR + (RL_MieScattering + RL_MieAbsorption) * dM + RL_OzoneAbsorption * dO;
}

float RL_RayleighPhase(float c) { return 3.0 / (16.0 * RL_PI) * (1.0 + c * c); }
float RL_MiePhase(float c)
{
    float g = RL_MieG, g2 = g * g;
    float k = 3.0 / (8.0 * RL_PI) * (1.0 - g2) / (2.0 + g2);
    return k * (1.0 + c * c) / pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5);
}

// ---------------------------------------------------------------------------
// Transmittance LUT parametrisation (Bruneton 2017 improved mapping)
//   u <- mu (cos zenith), v <- r (radius). Texture: 256 x 64.
// ---------------------------------------------------------------------------
float RL_SafeSqrt(float a) { return sqrt(max(a, 0.0)); }
float2 RL_TransmittanceUV(float r, float mu)
{
    float H = RL_SafeSqrt(RL_Rt * RL_Rt - RL_Rg * RL_Rg);
    float rho = RL_SafeSqrt(r * r - RL_Rg * RL_Rg);
    float d = RL_DistanceToTop(r, mu);
    float dMin = RL_Rt - r, dMax = rho + H;
    float xMu = (d - dMin) / max(dMax - dMin, 1e-6);
    float xR = rho / H;
    return float2(xMu, xR);
}
float3 RL_Transmittance(float r, float mu)
{
    float2 uv = RL_TransmittanceUV(r, mu);
    return SAMPLE_TEXTURE2D_LOD(_RL_TransmittanceLUT, sampler_RL_TransmittanceLUT, uv, 0).rgb;
}
// Transmittance to the Sun; zero when the Sun is geometrically below the ground horizon.
float3 RL_TransmittanceToSun(float r, float muS)
{
    float sinH = RL_Rg / r;
    float cosH = -RL_SafeSqrt(max(1.0 - sinH * sinH, 0.0));
    float3 T = RL_Transmittance(r, muS);
    // smooth the limb (sun angular radius)
    float s = smoothstep(-_RL_SunAngularRadius, _RL_SunAngularRadius, muS - cosH);
    return T * s;
}
// Multi-scattering LUT (32x32): u <- muS, v <- r
float3 RL_MultiScattering(float r, float muS)
{
    float2 uv = float2(muS * 0.5 + 0.5, (r - RL_Rg) / (RL_Rt - RL_Rg));
    return SAMPLE_TEXTURE2D_LOD(_RL_MultiScatLUT, sampler_RL_MultiScatLUT, uv, 0).rgb;
}

// ---------------------------------------------------------------------------
// Sky-view LUT parametrisation: u = azimuth/(2π) (from +Z=North toward +X=East), v = elevation (non-linear)
// ---------------------------------------------------------------------------
float2 RL_SkyViewUV(float3 dir)
{
    float az = atan2(dir.x, dir.z);            // 0 = North, +90° = East
    float u = az / (2.0 * RL_PI); u = frac(u + 1.0);
    float el = asin(clamp(dir.y, -1.0, 1.0));
    float v = 0.5 + 0.5 * sign(el) * sqrt(abs(el) / (0.5 * RL_PI));
    return float2(u, v);
}
float3 RL_SkyViewDir(float2 uv)
{
    float az = uv.x * 2.0 * RL_PI;
    float t = (uv.y - 0.5) * 2.0;
    float el = sign(t) * t * t * (0.5 * RL_PI);
    float ce = cos(el);
    return float3(sin(az) * ce, sin(el), cos(az) * ce);
}

// ---------------------------------------------------------------------------
// Atmospheric refraction (Bennett 1982 / Sæmundsson 1986, Meeus ch.16), degrees in/out
// ---------------------------------------------------------------------------
// Refraction (deg) from APPARENT altitude ha (deg): true = ha - R
float RL_RefractionFromApparent(float haDeg)
{
    float h = max(haDeg, -1.0);
    float R = 1.0 / tan(radians(h + 7.31 / (h + 4.4))); // arcmin
    return R * _RL_RefractionScale / 60.0;
}
// Refraction (deg) from TRUE altitude h (deg): apparent = h + R
float RL_RefractionFromTrue(float hDeg)
{
    float h = max(hDeg, -1.5);
    float R = 1.02 / tan(radians(h + 10.3 / (h + 5.11))); // arcmin
    return R * _RL_RefractionScale / 60.0;
}
// Convert an apparent (seen) view direction to the true geometric direction of the light source.
float3 RL_UnrefractDir(float3 dirApparent)
{
    float ha = degrees(asin(clamp(dirApparent.y, -1.0, 1.0)));
    if (ha < -1.0) return dirApparent;
    float ht = ha - RL_RefractionFromApparent(ha);
    float2 horiz = dirApparent.xz;
    float lh = length(horiz);
    if (lh < 1e-6) return dirApparent;
    horiz /= lh;
    float ct = cos(radians(ht));
    return float3(horiz.x * ct, sin(radians(ht)), horiz.y * ct);
}
float3 RL_RefractDir(float3 dirTrue)
{
    float ht = degrees(asin(clamp(dirTrue.y, -1.0, 1.0)));
    if (ht < -1.5) return dirTrue;
    float ha = ht + RL_RefractionFromTrue(ht);
    float2 horiz = dirTrue.xz;
    float lh = length(horiz);
    if (lh < 1e-6) return dirTrue;
    horiz /= lh;
    float ca = cos(radians(ha));
    return float3(horiz.x * ca, sin(radians(ha)), horiz.y * ca);
}

// Luminance of linear sRGB radiance (Rec.709 weights)
float RL_Luminance(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// Night-sky background: airglow (mostly OI 557.7 nm, greenish) + zodiacal + integrated starlight.
// Zenith ≈ 22.0 mag/arcsec² ≈ 1.7e-4 cd/m²; van Rhijn brightening toward horizon.
float3 RL_NightSky(float3 dir)
{
    float z = max(dir.y, 0.02);
    float airmass = 1.0 / (z + 0.025 * exp(-11.0 * z));
    float vanRhijn = min(airmass, 4.0);
    return _RL_NightSkyRadiance * vanRhijn;
}
#endif
