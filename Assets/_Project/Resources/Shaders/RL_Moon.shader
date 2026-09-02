// Real Life Sky — the Moon.
// A unit sphere placed on the celestial sphere at the true topocentric direction, scaled to the exact
// apparent angular diameter, oriented by libration (l, b) and axis position angle P.
// Shading: Hapke-style lunar BRDF approximation with opposition surge (the real "full moon is 10x brighter
// than quarter" effect), NASA LROC albedo, earthshine on the dark side, transmittance and refraction.
Shader "RealLife/Moon"
{
    Properties
    {
        _Albedo ("LROC albedo", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Background+10" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Back ZWrite Off ZTest Always
        Pass
        {
            Name "Moon"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RL_Atmosphere.hlsl"

            TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
            float3 _MoonSunDirLocal;      // direction to the Sun in the Moon's object (selenographic) frame
            float3 _MoonEarthDirLocal;    // direction to the Earth in the Moon frame (for earthshine)
            float  _MoonPhaseAngle;       // radians
            float  _EarthshineLux;        // illuminance of the Earth on the Moon (lux), phase dependent
            float3 _MoonTransmittance;    // atmospheric transmittance along the view ray to the Moon

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalOS : TEXCOORD0; float2 uv : TEXCOORD1; float3 viewOS : TEXCOORD2; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                // Apparent (refracted) position handled on the CPU by placing the transform; here plain projection
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalOS = normalize(v.normalOS);
                o.uv = v.uv;
                float3 camOS = TransformWorldToObject(GetCameraPositionWS());
                o.viewOS = normalize(camOS - v.positionOS.xyz);
                return o;
            }

            // Lunar photometric function: Lommel–Seeliger with Hapke opposition surge (B0=1.5, h=0.05 rad) and
            // a phase function fitted to Hillier et al. 1999 / Buratti et al. 2011 lunar phase curve.
            float LunarBRDF(float mu0, float mu, float alpha)
            {
                float ls = mu0 / max(mu0 + mu, 1e-4);            // Lommel-Seeliger
                float B = 1.5 / (1.0 + tan(alpha * 0.5) / 0.05);  // opposition effect (Hapke)
                // phase function P(alpha): normalised so that P(0)=1, decreasing ~exponentially (backscattering regolith)
                float P = exp(-2.5 * alpha) * 0.55 + 0.45 * exp(-0.7 * alpha);
                return ls * (1.0 + B) * P;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalOS);
                float3 s = normalize(_MoonSunDirLocal);
                float3 v = normalize(i.viewOS);
                float mu0 = dot(n, s);
                float mu = max(dot(n, v), 1e-3);
                float3 albedo = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, i.uv).rgb;
                // LROC WAC 643 nm map is ~normal-albedo; geometric albedo of the Moon is 0.12 (V)
                albedo *= 0.12 / 0.16;
                // Sun illuminance at the Moon (1 AU-ish): 127,700 lx * (1/d^2) — the CPU folds distance into the radiance.
                float sunLux = 127700.0;
                float3 sunlit = albedo * (sunLux / RL_PI) * LunarBRDF(saturate(mu0), mu, _MoonPhaseAngle) * float3(1.0, 0.98, 0.94);
                // Earthshine: Earth's illuminance on the dark side (up to ~15 lx at new moon)
                float3 e = normalize(_MoonEarthDirLocal);
                float3 earthshine = albedo * (_EarthshineLux / RL_PI) * saturate(dot(n, e)) * float3(0.80, 0.88, 1.0);
                float3 L = (sunlit * step(0.0, mu0) + earthshine) * _MoonTransmittance;
                L = RL_Perceive(L);
                return float4(L * _RL_Exposure, 1.0);
            }
            ENDHLSL
        }
    }
}
