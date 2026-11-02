Shader "Arena/Procedural Surface"
{
    Properties
    {
        _BaseColor ("Surface colour", Color) = (0.7,0.6,0.4,1)
        _DetailColor ("Detail colour", Color) = (0.4,0.35,0.25,1)
        _Pattern ("Pattern: sand 0, stone 1, plaster 2, tiles 3", Float) = 0
        _Repeat ("Pattern repeat", Float) = 12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _DetailColor;
                float _Pattern, _Repeat;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; };
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv; o.fog = ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _Repeat;
                float grain = Noise(uv*70);
                half3 colour = lerp(_BaseColor.rgb,_DetailColor.rgb,0.15+0.2*Noise(uv*3));
                if (_Pattern > 0.5 && _Pattern < 1.5 || _Pattern > 2.5)
                {
                    float2 grid = uv * float2(1, _Pattern > 2.5 ? 1.5 : 2);
                    grid.x += fmod(floor(grid.y),2)*0.5;
                    float2 cell = frac(grid);
                    float edge = min(min(cell.x,1-cell.x), min(cell.y,1-cell.y));
                    float mortar = 1-smoothstep(0.018,0.045,edge);
                    colour *= 0.85+0.25*Hash(floor(grid));
                    if (_Pattern > 2.5) colour *= 0.8+0.2*sin(cell.x*3.14159);
                    colour = lerp(colour,_DetailColor.rgb,mortar*0.85);
                }
                colour *= 0.92+grain*0.16;
                half3 normal = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 lighting = max(SampleSH(normal),half3(0.22,0.22,0.22));
                lighting += light.color * saturate(dot(normal,light.direction)) * light.distanceAttenuation * light.shadowAttenuation;
                return half4(MixFog(colour*lighting,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
