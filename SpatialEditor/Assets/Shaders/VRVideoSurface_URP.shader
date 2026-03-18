Shader "Custom/VRVideoSurface_URP"
{
    Properties
    {
        _BaseMap ("Video Texture", 2D) = "black" {}
        _StereoMode ("Stereo Mode", Float) = 0
        _FlipX ("Flip X", Float) = 0
        _FlipY ("Flip Y", Float) = 0
        _YawOffsetDegrees ("Yaw Offset Degrees", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
            "RenderPipeline"="UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _StereoMode;
                float _FlipX;
                float _FlipY;
                float _YawOffsetDegrees;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                return OUT;
            }

            float2 ApplyStereoLayout(float2 uv, int eyeIndex, float stereoMode)
            {
                // stereoMode:
                // 0 = Mono
                // 1 = SBS Left-Right   (left eye in left half, right eye in right half)
                // 2 = SBS Right-Left   (right eye in left half, left eye in right half)
                // 3 = Top-Bottom       (left eye top, right eye bottom)
                // 4 = Bottom-Top       (right eye top, left eye bottom)

                if (stereoMode < 0.5)
                {
                    return uv;
                }
                else if (stereoMode < 1.5)
                {
                    // SBS Left-Right
                    uv.x = (eyeIndex == 0) ? (uv.x * 0.5) : (0.5 + uv.x * 0.5);
                    return uv;
                }
                else if (stereoMode < 2.5)
                {
                    // SBS Right-Left
                    uv.x = (eyeIndex == 0) ? (0.5 + uv.x * 0.5) : (uv.x * 0.5);
                    return uv;
                }
                else if (stereoMode < 3.5)
                {
                    // Top-Bottom: left eye top, right eye bottom
                    uv.y = (eyeIndex == 0) ? (0.5 + uv.y * 0.5) : (uv.y * 0.5);
                    return uv;
                }
                else
                {
                    // Bottom-Top: right eye top, left eye bottom
                    uv.y = (eyeIndex == 0) ? (uv.y * 0.5) : (0.5 + uv.y * 0.5);
                    return uv;
                }
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 uv = IN.uv;

                // Optional yaw offset, useful for 180/360 sphere alignment
                uv.x = frac(uv.x + (_YawOffsetDegrees / 360.0));

                if (_FlipX > 0.5)
                    uv.x = 1.0 - uv.x;

                if (_FlipY > 0.5)
                    uv.y = 1.0 - uv.y;

                int eyeIndex = unity_StereoEyeIndex;
                uv = ApplyStereoLayout(uv, eyeIndex, _StereoMode);

                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
