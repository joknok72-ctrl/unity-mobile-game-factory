// Real Life Sky — Sky-View LUT generation (Hillaire 2020). Rendered once per frame into a 192x108 RGBAHalf RT.
// Each texel = luminance (per channel, cd/m^2) of the sky in one direction from the observer:
// single scattering (ray-marched, 32 steps) + multiple scattering (LUT) + ground bounce.
Shader "RealLife/SkyViewLUT"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "SkyViewLUT"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "RL_Atmosphere.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = float4(v.positionOS.xy * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                o.positionCS.y = -o.positionCS.y;
                #endif
                o.uv = v.uv;
                return o;
            }

            float3 IntegrateSky(float3 dir, float3 sunDir, float r)
            {
                const int STEPS = 32;
                float mu = dir.y;
                bool ground = RL_HitsGround(r, mu);
                float tMax = ground ? (-r*mu - RL_SafeSqrt(r*r*(mu*mu-1.0)+RL_Rg*RL_Rg)) : RL_DistanceToTop(r, mu);
                float cosTheta = dot(dir, sunDir);
                float phR = RL_RayleighPhase(cosTheta), phM = RL_MiePhase(cosTheta);
                float3 L = 0, T = 1;
                float dt = tMax / STEPS;
                for (int i = 0; i < STEPS; i++)
                {
                    float t = (i + 0.5) * dt;
                    float3 p = float3(0, r, 0) + dir * t;
                    float pr = length(p);
                    float3 sR, ext; float sM;
                    RL_Medium(pr - RL_Rg, sR, sM, ext);
                    float muS = dot(p, sunDir) / pr;
                    float3 tSun = RL_TransmittanceToSun(pr, muS);
                    float3 ms = RL_MultiScattering(pr, muS);
                    float3 sampleT = exp(-ext * dt);
                    float3 S = (sR * phR + sM * phM) * tSun + (sR + sM) * ms;
                    L += T * (S - S * sampleT) / max(ext, 1e-6);
                    T *= sampleT;
                }
                if (ground)
                {
                    float3 g = float3(0, r, 0) + dir * tMax;
                    float gr = length(g);
                    float muS = dot(g, sunDir) / gr;
                    L += T * RL_TransmittanceToSun(gr, muS) * max(muS, 0.0) * 0.3 / RL_PI;
                }
                return L;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 dir = RL_SkyViewDir(i.uv);
                float r = RL_Rg + max(_RL_ObserverHeightKm, 0.001);
                float3 L = IntegrateSky(dir, _RL_SunDir, r) * _RL_SunIlluminance;
                // Moonlight: same scattering with the Moon as a (much dimmer) light source, illuminance from magnitude
                float moonLux = RL_IlluminanceFromMagnitude(_RL_MoonMagnitude);
                float3 Lm = IntegrateSky(dir, _RL_MoonDir, r) * moonLux * float3(1.0, 0.98, 0.94);
                return float4(L + Lm, 1.0);
            }
            ENDHLSL
        }
    }
}
