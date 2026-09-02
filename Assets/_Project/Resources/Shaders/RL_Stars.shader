// Real Life Sky — stars & planets as point sources.
// Each star is a camera-facing quad whose *integrated* luminance equals the physical illuminance of the star
// (from its visual magnitude: E = 10^(-0.4 (m + 13.98)) lux) spread over the eye's point-spread function
// (~1 arcmin FWHM Gaussian, plus glare wings for bright objects), attenuated by the atmosphere's transmittance
// along the (refracted) line of sight, tinted by B-V colour index, and twinkling (scintillation) with an
// amplitude that grows with airmass. Planets are also rendered here (disc smaller than the PSF, except Venus/Jupiter
// which get a physically sized core).
Shader "RealLife/Stars"
{
    SubShader
    {
        Tags { "Queue"="Background+5" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend One One
        Pass
        {
            Name "Stars"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RL_Atmosphere.hlsl"

            float _PsfRadiusRad;     // eye PSF radius in radians at which the quad is cut (≈ 3 sigma)
            float _PixelAngle;       // radians per pixel (vertical FOV / screen height)
            float _Scintillation;    // 0..1 global seeing strength

            struct Attributes
            {
                float4 positionOS : POSITION;   // xyz = unit direction (true, geometric ENU->world), w unused
                float4 color      : COLOR;      // rgb = linear colour (unit luminance), a = unused
                float2 uv         : TEXCOORD0;  // corner (-1..1)
                float2 data       : TEXCOORD1;  // x = visual magnitude, y = twinkle seed
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 color : TEXCOORD1;   // radiance scale (already includes exposure & transmittance)
                float  sigmaPx : TEXCOORD2; // psf sigma in quad units
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 dirTrue = normalize(v.positionOS.xyz);
                float3 dirApp = RL_RefractDir(dirTrue);        // where we SEE it
                float mag = v.data.x;
                // Atmospheric extinction along the ray (transmittance LUT, observer radius)
                float r = RL_Rg + max(_RL_ObserverHeightKm, 0.001);
                float3 T = RL_Transmittance(r, max(dirTrue.y, -0.02));
                float lux = RL_IlluminanceFromMagnitude(mag);
                // Scintillation: amplitude ~ airmass^1.75 (Dravins et al. 1997), 0..~40% at horizon; 12 Hz-ish flicker
                float z = max(dirApp.y, 0.05);
                float airmass = 1.0 / (z + 0.025 * exp(-11.0 * z));
                float amp = _Scintillation * saturate(0.02 * pow(airmass, 1.75));
                float tw = 1.0 + amp * (sin(_RL_Time * 37.0 + v.data.y * 91.7) * 0.6 + sin(_RL_Time * 61.0 + v.data.y * 13.3) * 0.4);
                lux *= tw;

                // Point-source -> radiance over PSF: quad half-size = _PsfRadiusRad (world angle). Peak radiance of a Gaussian
                // PSF with total illuminance E and sigma (rad): L0 = E / (2 pi sigma^2). We render exp(-r^2/2sigma^2) * L0.
                float sigma = max(_PsfRadiusRad / 3.0, _PixelAngle * 0.7); // never below sub-pixel (energy conservation)
                float L0 = lux / (2.0 * RL_PI * sigma * sigma);
                float3 radiance = v.color.rgb * L0 * T;

                // Build camera-facing quad at 'infinite' distance (skybox-like: use view rotation only)
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 camUp = normalize(UNITY_MATRIX_V[1].xyz);
                float half = _PsfRadiusRad;
                float3 pos = dirApp + (camRight * v.uv.x + camUp * v.uv.y) * half;
                float4 posWS = float4(pos * 100.0 + GetCameraPositionWS(), 1.0);
                o.positionCS = TransformWorldToHClip(posWS.xyz);
                o.positionCS.z = o.positionCS.w * 0.999999; // far
                o.uv = v.uv;
                o.color = RL_Perceive(radiance) * _RL_Exposure;
                o.sigmaPx = sigma / half;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                float s2 = i.sigmaPx * i.sigmaPx;
                float g = exp(-r2 / (2.0 * s2));
                // glare wings (Vos 1984 disability glare ~ 1/theta^2), tiny fraction of the energy
                float wings = 0.02 * s2 / max(r2, s2);
                float3 c = i.color * (g + wings) * step(r2, 1.0);
                return float4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
