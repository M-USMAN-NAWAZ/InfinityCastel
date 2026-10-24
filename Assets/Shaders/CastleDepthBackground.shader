Shader "Infinity Castle/Depth Background"
{
    Properties
    {
        _DeepColor ("Deep background", Color) = (0.075, 0.04, 0.025, 1)
        _WarmColor ("Distant warmth", Color) = (0.16, 0.09, 0.045, 1)
        _LightColor ("Faint distant lights", Color) = (0.55, 0.29, 0.09, 1)
        _LightStrength ("Distant light strength", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _WarmColor, _LightColor;
                half _LightStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }
            float3 Hash(float3 value)
            {
                value = frac(value * float3(.1031, .1030, .0973));
                value += dot(value, value.yxz + 33.33);
                return frac((value.xxy + value.yxx) * value.zyx);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                // Direction only: no world position, finite shell, horizon, or
                // moving texture. Jumping cannot approach this background.
                half warm = saturate(dot(direction, float3(.36, .48, .8)) * .5 + .5);
                warm = warm * warm * .65 + saturate(dot(direction, float3(-.7, -.3, .64))) * .2;
                half3 color = lerp(_DeepColor.rgb, _WarmColor.rgb, warm);
                // A 3D directional field avoids panoramic seams and pole pinching.
                // Sparse, low-contrast lights suggest depth, never painted rooms.
                float3 cell = direction * 42;
                float3 random = Hash(floor(cell));
                float3 offset = frac(cell) - (.25 + random * .5);
                half lamp = 1 - smoothstep(.003, .018, dot(offset, offset));
                lamp *= step(.78, random.z) * _LightStrength;
                return half4(color + _LightColor.rgb * lamp, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
