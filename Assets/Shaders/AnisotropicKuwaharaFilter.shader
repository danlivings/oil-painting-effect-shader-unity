Shader "Hidden/Oil Painting/Anisotropic Kuwahara Filter"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        _StructureTensorTex ("Structure Tensor", 2D) = "white" {}

        [IntRange] _FilterKernelSectors ("Filter Kernel Sectors", Range(3, 8)) = 8
        _FilterKernelTex ("Filter Kernel Texture", 2D) = "black" {}

        _FilterRadius ("Filter Radius", Range(2, 12)) = 4
        _FilterSharpness ("Filter Sharpness", Range(2, 16)) = 8
        _Eccentricity ("Eccentricity", Range(0.125, 32)) = 1
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

            #define MAX_KERNEL_SECTORS 8
    
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_StructureTensorTex);
            SAMPLER(sampler_StructureTensorTex);

            int _FilterKernelSectors;

            TEXTURE2D(_FilterKernelTex);
            SAMPLER(sampler_FilterKernelTex);
            float4 _FilterKernelTex_TexelSize;

            float _FilterRadius;
            float _FilterSharpness;
            float _Eccentricity;
            
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
    
            float3 SampleMain(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
            }
            
            float4 SampleStructureTensor(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_StructureTensorTex, sampler_StructureTensorTex, uv);
            }

            float SampleFilterKernel(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_FilterKernelTex, sampler_FilterKernelTex, uv).r;
			}

            float2x2 GetScaleMatrix(float anisotropy, float eccentricity)
            {
                float2x2 s;
                s._m00 = clamp(eccentricity / (eccentricity + anisotropy), 0.1, 2.0);
                s._m11 = clamp((eccentricity + anisotropy) / eccentricity, 0.1, 2.0);
                s._m01 = 0;
                s._m10 = 0;

                return s;
			}

            float2x2 GetRotationMatrix(float phi)
            {
                float cosPhi = cos(phi);
                float sinPhi = sin(phi);

                float2x2 r;
                r._m00 = cosPhi;
                r._m11 = cosPhi;
                r._m01 = -sinPhi;
                r._m10 = sinPhi;

                return r;
			}

            half4 frag(Varyings input) : SV_Target
            {
                float4 tensor = SampleStructureTensor(input.uv);

                float2 v = tensor.xy;
                float phiBase = tensor.z;
                float a = tensor.w;

                float2x2 s = GetScaleMatrix(a, _Eccentricity) * _FilterRadius;

                float3 weightedAverages[MAX_KERNEL_SECTORS];
                float3 standardDeviations[MAX_KERNEL_SECTORS];

                [unroll]
                for (int i = 0; i < _FilterKernelSectors; i++)
                {
                    weightedAverages[i] = float3(0,0,0);
                    standardDeviations[i] = float3(0,0,0);

                    float phi = phiBase + (2.0f * PI * i / _FilterKernelSectors);
                    float2x2 r = GetRotationMatrix(phi);

                    float2x2 sr = mul(s, r);

                    float norm = 0;
                    for (int x = -ceil(_FilterRadius); x <= ceil(_FilterRadius); x++)
                    {
                        for (int y = -ceil(_FilterRadius); y <= ceil(_FilterRadius); y++)
                        {
                            float offsetSqMagnitude = x*x + y*y;
                            if (offsetSqMagnitude / _FilterRadius <= 0.25)
                            {
                                float2 offset = mul(sr, float2(PIXEL_X * x, PIXEL_Y * y));

                                float2 sampleUV = input.uv + offset;
                                float3 sample = SampleMain(sampleUV);

                                float wu = 0.5f + 0.5f * (x / ceil(_FilterRadius));
                                float wv = 0.5f + 0.5f * (y / ceil(_FilterRadius));
                            
                                float2 weightUV = float2(wu, wv);
                                float weight = SampleFilterKernel(weightUV);

                                weightedAverages[i] += sample * weight;
                                standardDeviations[i] += sample * sample * weight;

                                norm += weight;
                            }
						}
					}

                    if (norm > 0)
                    {
                        weightedAverages[i] /= norm;
                        standardDeviations[i] /= norm;
                        standardDeviations[i] -= weightedAverages[i] * weightedAverages[i];
                        standardDeviations[i] = abs(standardDeviations[i]);
                    }
				}

                float sumAlpha = 0;
                float3 sumAlphaWeights = 0;
                for (i = 0; i < _FilterKernelSectors; i++)
                {
                    float sigmaSq = abs(standardDeviations[i].r + standardDeviations[i].g + standardDeviations[i].b);
                    float alpha = 1 / (1 + pow(255 * sigmaSq, 0.5f * _FilterSharpness));

                    sumAlpha += alpha;
                    sumAlphaWeights += alpha * weightedAverages[i];
				}
                
                return half4(sumAlphaWeights / sumAlpha, 0);
            }
    
            #pragma vertex vert
            #pragma fragment frag

            ENDHLSL
        }
    }
    FallBack "Diffuse"
}
