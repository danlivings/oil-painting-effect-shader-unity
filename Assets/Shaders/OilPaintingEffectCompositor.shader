Shader "Hidden/Oil Painting/Compositor"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _EdgeFlowTex ("Edge Flow", 2D) = "white" {}
        _DepthTex ("Depth", 2D) = "white" {}

        _EdgeContribution ("Edge Contribution", Range(0, 4)) = 1
        _FlowContribution ("Flow Contribution", Range(0, 4)) = 1
        _DepthContribution ("Depth Contribution", Range(0, 4)) = 1

        _BumpPower ("Bump Power", Range(0.25, 1)) = 0.8
        _BumpIntensity("Bump Intensity", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            
            #define PIXEL_X (_ScreenParams.z - 1)
            #define PIXEL_Y (_ScreenParams.w - 1)

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_EdgeFlowTex);
            SAMPLER(sampler_EdgeFlowTex);

            TEXTURE2D(_DepthTex);
            SAMPLER(sampler_DepthTex);

            float _EdgeContribution;
            float _FlowContribution;
            float _DepthContribution;

            float _BumpPower;
            float _BumpIntensity;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };
    
            struct Varyings
            {
                float2 uv     : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
    
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.vertex = vertexInput.positionCS;
                output.uv = input.uv;
    
                return output;
            }

            float SampleDepth(float2 uv)
            {
                return SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
            }

            float3 SampleMain(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
			}

            float SampleEdgeFlow(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_EdgeFlowTex, sampler_EdgeFlowTex, uv).r;     
			}

            float3 SobelU(float2 uv)
            {
                return (
                    -1.0f * SampleMain(uv + float2(-PIXEL_X, -PIXEL_Y)) +
                    -2.0f * SampleMain(uv + float2(-PIXEL_X, 0)) +
                    -1.0f * SampleMain(uv + float2(-PIXEL_X, PIXEL_Y)) +

                    1.0f * SampleMain(uv + float2(PIXEL_X, -PIXEL_Y)) +
                    2.0f * SampleMain(uv + float2(PIXEL_X, 0)) +
                    1.0f * SampleMain(uv + float2(PIXEL_X, PIXEL_Y))
				) / 4.0;     
			}

            float3 SobelV(float2 uv)
            {
                return (
                    -1.0f * SampleMain(uv + float2(-PIXEL_X, -PIXEL_Y)) +
                    -2.0f * SampleMain(uv + float2(0, -PIXEL_Y)) +
                    -1.0f * SampleMain(uv + float2(PIXEL_X, -PIXEL_Y)) +

                    1.0f * SampleMain(uv + float2(-PIXEL_X, PIXEL_Y)) +
                    2.0f * SampleMain(uv + float2(0, PIXEL_Y)) +
                    1.0f * SampleMain(uv + float2(PIXEL_X, PIXEL_Y))
				) / 4.0;     
			}

            float GetHeight(float2 uv)
            {
                float3 edgeU = SobelU(uv);
                float3 edgeV = SobelV(uv);
                float edgeFlow = SampleEdgeFlow(uv);
                float depth = SampleDepth(uv);
                
                return _EdgeContribution * (length(edgeU) + length(edgeV)) +
                    _FlowContribution * edgeFlow +
                    _DepthContribution * depth;
			}

            half4 frag(Varyings input) : SV_Target
            {               
                float3 baseColor = SampleMain(input.uv);

                float bumpAbove = GetHeight(input.uv + float2(0, PIXEL_Y));
                float bump = GetHeight(input.uv);

                float diff = bump - bumpAbove;
                diff = sign(diff) * pow(saturate(abs(diff)), _BumpPower);

                return half4(baseColor + baseColor * diff * _BumpIntensity, 0);
            }

            #pragma vertex vert
            #pragma fragment frag

            ENDHLSL
        }
    }
}
