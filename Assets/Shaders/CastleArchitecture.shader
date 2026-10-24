Shader "Infinity Castle/Architecture"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Glow ("Window Glow", Range(0,2)) = 0
        _AtlasEmission ("Shared atlas lantern emission", Range(0,2)) = 0
        _RailEndClearance ("Walkway end opening (metres)", Float) = 0
        _WoodMap ("Castle Timber", 2D) = "white" {}
        _PaperMap ("Shoji Fibres", 2D) = "white" {}
        _ThemeStrength ("Unified Finish", Range(0,1)) = 0
        _FinishMode ("Surface Family", Float) = 0
        _CastleAssembly ("Timber Assembly (progress, travel, seed, enabled)", Vector) = (1,0.45,0,0)
        _CastleBuild ("Construction clock (start, duration, seed, enabled)", Vector) = (0,0,0,0)
        _CastleInstanceBuild ("Instanced construction clock", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Castle Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Bound transform/property buffers below mobile uniform-block limits.
            #pragma instancing_options forcemaxcount:64
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_WoodMap); SAMPLER(sampler_WoodMap);
            TEXTURE2D(_PaperMap); SAMPLER(sampler_PaperMap);
            float4 _CastleStreamCenter;
            float4 _CastleStreamDistances;
            float _CastleConstructionTime;
            UNITY_INSTANCING_BUFFER_START(CastleConstruction)
                UNITY_DEFINE_INSTANCED_PROP(float4, _CastleInstanceBuild)
            UNITY_INSTANCING_BUFFER_END(CastleConstruction)
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Glow;
                half _AtlasEmission;
                float _RailEndClearance;
                half _ThemeStrength;
                half _FinishMode;
                float4 _CastleAssembly;
                float4 _CastleBuild;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 assembly : TEXCOORD1; float timberSize : TEXCOORD2; float4 rail : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; float3 positionOS : TEXCOORD4; half3 normalOS : TEXCOORD5; half2 assemblyVisible : TEXCOORD6; half2 rail : TEXCOORD7; };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float4 construction = _CastleBuild;
                #ifdef UNITY_INSTANCING_ENABLED
                    construction = UNITY_ACCESS_INSTANCED_PROP(CastleConstruction, _CastleInstanceBuild);
                #endif
                float4 assembly = _CastleAssembly;
                if (construction.w > .5)
                    assembly = float4(construction.w > 1.5 ? 0 : construction.y > 0 ?
                        saturate((_CastleConstructionTime - construction.x) / construction.y) : 1,
                        input.timberSize * .75, construction.z, 1);
                output.assemblyVisible = half2(1, construction.w > .5);
                if (assembly.w > .5 && input.assembly.w >= 1 && assembly.x < 1)
                {
                    float phase = input.assembly.w - 1;
                    float seed = frac(sin(dot(input.assembly.xyz, float3(127.1,311.7,74.7)) + assembly.z) * 43758.5453);
                    float flight = saturate((assembly.x - phase) / .42);
                    float remaining = 1 - smoothstep(0, 1, flight);
                    // All vertices in a timber piece share a centre and timing: rigid motion, never a stretched dissolve.
                    float angle = (seed - .5) * 3.14159265 * remaining;
                    float sine, cosine; sincos(angle, sine, cosine);
                    float3 local = input.positionOS.xyz - input.assembly.xyz;
                    local.xz = float2(local.x * cosine - local.z * sine, local.x * sine + local.z * cosine);
                    float3 normal = input.normalOS;
                    normal.xz = float2(normal.x * cosine - normal.z * sine, normal.x * sine + normal.z * cosine);
                    float3 travel = float3(sin(seed * 37), .35 + frac(seed * 11), cos(seed * 53)) * assembly.y;
                    input.positionOS.xyz = input.assembly.xyz + local + travel * remaining;
                    input.normalOS = normal;
                    output.assemblyVisible.x = flight > .0001;
                }
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS; output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 size = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                // Source route distances preserve a metre-sized opening at any
                // pooled instance scale, including during timber assembly.
                output.rail = input.rail.z > .5 ? input.rail.xy * (input.rail.w > 0 ? size.z : size.x) : half2(256,256);
                output.positionOS = input.positionOS.xyz * size; output.normalOS = input.normalOS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap); output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.assemblyVisible.x - .5);
                if (_RailEndClearance > 0)
                    clip(min(input.rail.x, input.rail.y) - _RailEndClearance);
                if (_CastleStreamCenter.w > .5)
                {
                    float distanceToPlayer = distance(input.positionWS, _CastleStreamCenter.xyz);
                    float coverage = saturate((_CastleStreamDistances.y - distanceToPlayer) /
                        max(1.0, _CastleStreamDistances.y - _CastleStreamDistances.x));
                    // Opaque screen-door fade: no transparent sorting or extra mobile draw pass.
                    float noise = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(.06711056, .00583715))));
                    // Constructed rooms remain solid; distance dithering must not hide their assembly.
                    if (input.assemblyVisible.y > .5) clip(_CastleStreamDistances.y - distanceToPlayer);
                    else clip(coverage - max(noise, .0001));
                }
                half3 normal = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lighting = half3(0.72,0.62,0.54) + max(SampleSH(normal), 0) * 0.35;
                lighting += light.color * saturate(dot(normal, light.direction)) * light.shadowAttenuation;
                #ifdef _ADDITIONAL_LIGHTS
                for (uint i = 0; i < GetAdditionalLightsCount(); i++)
                {
                    Light extra = GetAdditionalLight(i, input.positionWS);
                    lighting += extra.color * saturate(dot(normal, extra.direction)) * extra.distanceAttenuation;
                }
                #endif
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half emission = _Glow;
                // Only the two dedicated lantern-paper cells emit. This adds no
                // texture fetch, extra light, transparency, or shader variant.
                float2 atlasCell = floor(saturate(input.uv) * 4);
                half lanternCell = (atlasCell.x == 1 && atlasCell.y == 2) || (atlasCell.x == 3 && atlasCell.y == 0);
                emission += lanternCell * _AtlasEmission;
                if (_ThemeStrength > 0.5)
                {
                    half3 axis = abs(input.normalOS);
                    float2 grainUV = axis.y > axis.x && axis.y > axis.z ? input.positionOS.xz :
                        axis.x > axis.z ? input.positionOS.zy : input.positionOS.xy;
                    half3 wood = SAMPLE_TEXTURE2D(_WoodMap, sampler_WoodMap, grainUV * .6).rgb;
                    half3 paper = SAMPLE_TEXTURE2D(_PaperMap, sampler_PaperMap, grainUV).rgb;
                    half3 category = albedo;
                    #ifndef UNITY_COLORSPACE_GAMMA
                    category = sqrt(saturate(category));
                    #endif
                    half value = max(category.r, max(category.g, category.b));
                    half saturation = (value - min(category.r, min(category.g, category.b))) / max(value, .01);
                    half verticalPanel = 1 - smoothstep(.4, .8, abs(input.normalOS.z));
                    half paperMask = smoothstep(.38, .70, value) * (1 - smoothstep(.18, .42, saturation)) * verticalPanel;
                    half amber = smoothstep(.48, .8, value) * smoothstep(.5, .85, saturation) * step(category.b * 1.4, category.g);
                    half detail = clamp(value * 2.2, .4, 1.4);
                    half3 finish = lerp(wood * detail, paper * clamp(value * 1.5, .8, 1.05), paperMask);
                    finish = lerp(finish, half3(.62,.36,.095) * detail, amber * .8);
                    emission += amber * .14 + paperMask * .06;
                    if (_FinishMode > .5 && _FinishMode < 1.5) finish = wood;
                    if (_FinishMode > 1.5 && _FinishMode < 2.5)
                    { finish = lerp(wood * .5, paper, smoothstep(.12,.5,value)); emission += .035; }
                    if (_FinishMode > 2.5 && _FinishMode < 3.5) finish = wood * half3(.48,.58,.68);
                    if (_FinishMode > 3.5 && _FinishMode < 4.5) finish = half3(.48,.28,.08) * detail;
                    if (_FinishMode > 4.5) { finish = paper * half3(1,.7,.36); emission += .65; }
                    albedo = saturate(finish * 1.35 + half3(.035, .025, .018));
                }
                return half4(MixFog(albedo * (lighting + emission), input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
