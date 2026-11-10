// 루미아 서킷 툰 셰이더 (URP Forward)
// - 2단 음영 + 날카로운 그림자, 은은한 테두리 빛(rim)
// - 외곽선: 매끈한 노멀(TEXCOORD3, 모델 가져올 때 계산)을 따라 밀어낸 뒷면 (SRPDefaultUnlit 패스, 재질별로 끌 수 있음)
// - _VertexAO: 정점 색을 그늘로 곱한다 (바닥·담에 미리 구워 둔 구석 그늘)
// - _WaveAmp: 바다 물결 / _Spec: 물결 반짝임
// 광택(금속·반사)은 일부러 쓰지 않는다.
Shader "Lumia/Toon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _Rim ("Rim", Range(0, 1)) = 0.25
        _Spec ("Spec", Range(0, 1)) = 0
        _VertexAO ("Vertex AO", Range(0, 1)) = 0
        _WaveAmp ("Wave Amp", Float) = 0
        _OutlineColor ("Outline Color", Color) = (0.1, 0.08, 0.08, 1)
        _OutlineWidth ("Outline Width", Float) = 0.0055
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            half _Rim;
            half _Spec;
            half _VertexAO;
            float _WaveAmp;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END

        // 월드 좌표에 물결을 더하고 물결면의 노멀을 돌려준다
        float3 ApplyWave(float3 p, inout float3 n)
        {
            if (_WaveAmp <= 0) return p;
            float t = _Time.y;
            float a = p.x * 0.55 + t * 1.1;
            float b = p.z * 0.7 + t * 0.8;
            float c = (p.x + p.z) * 0.35 + t * 1.6;
            p.y += (sin(a) + sin(b) + 0.5 * sin(c)) * _WaveAmp;
            float dx = (0.55 * cos(a) + 0.175 * cos(c)) * _WaveAmp;
            float dz = (0.7 * cos(b) + 0.175 * cos(c)) * _WaveAmp;
            n = normalize(float3(-dx, 1, -dz));
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                float3 p = ApplyWave(TransformObjectToWorld(i.positionOS.xyz), n);
                o.positionWS = p;
                o.normalWS = n;
                o.positionCS = TransformWorldToHClip(p);
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));

                half ndl = dot(N, L.direction);
                half shadow = smoothstep(0.35, 0.65, L.shadowAttenuation);
                half lit = smoothstep(0.0, 0.06, ndl) * shadow;          // 빛 / 그늘 경계
                half mid = smoothstep(0.42, 0.48, ndl);                   // 빛 안의 밝은 단

                half3 base = _BaseColor.rgb * lerp(half3(1, 1, 1), i.color.rgb, _VertexAO);
                half3 ambient = SampleSH(N);
                half3 c = base * (ambient + L.color * lit * (0.78 + 0.22 * mid));

                half rim = smoothstep(0.6, 0.66, 1 - saturate(dot(N, V)));
                c += base * L.color * rim * _Rim * (0.35 + 0.65 * lit);

                float3 H = normalize(L.direction + V);
                c += L.color * smoothstep(0.975, 0.985, dot(N, H)) * _Spec * lit;

                #ifdef _ADDITIONAL_LIGHTS
                int count = GetAdditionalLightsCount();
                for (int li = 0; li < count; li++)
                {
                    Light a = GetAdditionalLight(li, i.positionWS);
                    half d = smoothstep(0.0, 0.12, dot(N, a.direction));
                    c += base * a.color * (a.distanceAttenuation * d);
                }
                #endif

                c += _EmissionColor.rgb;
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 smoothNormal : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = dot(i.smoothNormal, i.smoothNormal) > 0.01 ? i.smoothNormal : i.normalOS;
                float4 p = TransformObjectToHClip(i.positionOS.xyz);
                float2 nCS = mul((float3x3)GetWorldToHClipMatrix(), TransformObjectToWorldNormal(n)).xy;
                float len = length(nCS);
                if (len > 1e-4)
                {
                    nCS /= len;
                    nCS.x *= _ScreenParams.y / _ScreenParams.x;   // 가로세로 같은 두께
                    p.xy += nCS * _OutlineWidth * p.w;            // 거리와 상관없이 같은 화면 두께
                }
                o.positionCS = p;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                float3 p = ApplyWave(TransformObjectToWorld(i.positionOS.xyz), n);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - p);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, ld));
                #if UNITY_REVERSED_Z
                    c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = c;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyWave(TransformObjectToWorld(i.positionOS.xyz), n));
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
