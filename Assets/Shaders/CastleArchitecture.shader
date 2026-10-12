Shader "Infinity Castle/Architecture"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Glow ("Window Glow", Range(0,2)) = 0
        _WoodMap ("Castle Timber", 2D) = "white" {}
        _PaperMap ("Shoji Fibres", 2D) = "white" {}
        _ThemeStrength ("Unified Finish", Range(0,1)) = 0
        _FinishMode ("Surface Family", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Castle Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_WoodMap); SAMPLER(sampler_WoodMap);
            TEXTURE2D(_PaperMap); SAMPLER(sampler_PaperMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Glow;
                half _ThemeStrength;
                half _FinishMode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; float3 positionOS : TEXCOORD4; half3 normalOS : TEXCOORD5; };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS; output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz; output.normalOS = input.normalOS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap); output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lighting = half3(0.24,0.19,0.16) + max(SampleSH(normal), 0) * 0.2;
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
                    albedo = finish;
                }
                return half4(MixFog(albedo * (lighting + emission), input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
