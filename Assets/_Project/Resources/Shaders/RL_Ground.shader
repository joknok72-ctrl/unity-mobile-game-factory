// Real Life Sky — the ground.
// A physically-consistent matte (Lambertian) ground lit by the SAME photometric quantities that drive the sky:
//   L_ground = albedo/π × ( E_sun·max(0,N·L) + E_moon·max(0,N·L) + E_sky )  [cd/m² when E is in lux]
// URP's main light is fed intensity = E × exposure / π (CelestialDirector), and the flat ambient carries E_sky × exposure / π,
// so the output here is already in the display-referred exposure space used by the skybox, Moon and stars.
// This shader lives in Resources so it is always included in the build (a runtime Shader.Find on
// "Universal Render Pipeline/Lit" is stripped from a build that has no material asset referencing it → magenta).
Shader "RealLife/Ground"
{
    Properties
    {
        _Albedo ("Albedo (linear, 0.15 = dry asphalt / soil)", Color) = (0.15, 0.15, 0.15, 1)
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Back ZWrite On
        Pass
        {
            Name "GroundForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RL_Atmosphere.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Albedo;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings  { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                // Main light (Sun by day, Moon by night — CelestialDirector swaps which one is the URP main light)
                Light mainLight = GetMainLight();
                float3 radiance = mainLight.color * saturate(dot(n, mainLight.direction));
                // Additional lights (the other of Sun/Moon when both are above the horizon)
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    radiance += l.color * l.distanceAttenuation * saturate(dot(n, l.direction));
                }
                #endif
                // Flat ambient = sky irradiance × exposure / π (set by CelestialDirector)
                radiance += unity_AmbientSky.rgb;
                float3 L = _Albedo.rgb * radiance;
                // Night vision (rod-dominated, desaturated) — same perceptual model as the sky
                L = RL_Perceive(L);
                return float4(L, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
