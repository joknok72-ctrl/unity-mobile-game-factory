// Real Life Sky — skybox: samples the per-frame Sky-View LUT, adds the Sun disc (limb-darkened, refracted,
// attenuated by transmittance, flattened near horizon by differential refraction), night-sky airglow,
// and applies the physical exposure. Output is linear HDR; URP tonemapping/colour grading happens after.
Shader "RealLife/Skybox"
{
    Properties { }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            Name "Sky"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RL_Atmosphere.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dirWS = TransformObjectToWorldDir(v.positionOS.xyz, false);
                return o;
            }

            // Radiance of the solar disc seen in direction 'view' (apparent), Sun apparent direction 'sunApp'.
            // Vertical flattening: the refraction gradient near the horizon compresses the disc vertically.
            float3 SunDisc(float3 view, float3 sunApp, float3 transmittance)
            {
                // work in a local frame: compress vertical offset by local refraction gradient
                float hSun = degrees(asin(clamp(sunApp.y, -1.0, 1.0)));
                float dR = (RL_RefractionFromTrue(hSun + 0.05) - RL_RefractionFromTrue(hSun - 0.05)) / 0.1; // d(refraction)/d(alt)
                float vScale = 1.0 / max(1.0 + dR, 0.2); // apparent vertical size factor
                float3 up = float3(0,1,0);
                float3 right = normalize(cross(up, sunApp));
                float3 fwdUp = normalize(cross(sunApp, right));
                float3 d = view - sunApp;
                float x = dot(d, right);
                float y = dot(d, fwdUp) / vScale;
                float ang = sqrt(x*x + y*y);
                float rN = ang / _RL_SunAngularRadius;
                float3 disc = 0;
                if (rN < 1.0)
                {
                    disc = _RL_SunRadiance * RL_SolarLimbDarkening(rN) * transmittance;
                }
                // Aureole (forward Mie scattering, already in LUT) — here only a tiny pixel-coverage antialias
                float edge = 1.0 - smoothstep(0.985, 1.0, rN);
                return disc * edge;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 view = normalize(i.dirWS);
                // What we see in direction 'view' actually came from the geometric direction 'trueDir'
                float3 trueDir = RL_UnrefractDir(view);
                float2 uv = RL_SkyViewUV(trueDir);
                float3 L = SAMPLE_TEXTURE2D_LOD(_RL_SkyViewLUT, sampler_RL_SkyViewLUT, uv, 0).rgb;

                // Night sky (airglow etc.) attenuated by the atmosphere along the view ray
                float r = RL_Rg + max(_RL_ObserverHeightKm, 0.001);
                float3 Tview = RL_Transmittance(r, max(trueDir.y, -0.02));
                L += RL_NightSky(trueDir) * Tview;

                // Sun disc (below the geometric horizon the ground occludes; -0.3° margin for terrain-less horizon)
                if (_RL_SunDir.y > -0.02 && view.y > -0.002)
                {
                    float3 Tsun = RL_Transmittance(r, _RL_SunDir.y);
                    L += SunDisc(view, _RL_SunDirApparent, Tsun);
                }
                // Below horizon: dark ground (albedo 0.3 lit by sun+sky) — the "earth" (no terrain in phase 1)
                if (view.y < 0.0)
                {
                    float3 ground = L; // LUT already contains ground bounce for downward rays
                    L = lerp(L, ground * 0.6, saturate(-view.y * 40.0));
                }
                L = RL_Perceive(L);
                return float4(L * _RL_Exposure, 1.0);
            }
            ENDHLSL
        }
    }
}
