using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

public class OilPaintingEffect : ScriptableRendererFeature
{
    private static readonly LayerMask AllLayers = ~0;

    private const int FilterKernelSize = 32;

    public Settings settings;

    private DepthOnlyPass depthOnlyPass;
    private OilPaintingEffectPass renderPass;

    private RenderTargetHandle depthTexture;

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (Application.isEditor && (renderingData.cameraData.camera.name == "SceneCamera" || renderingData.cameraData.camera.name == "Preview Scene Camera"))
        {
            return;
        }

        depthOnlyPass.Setup(renderingData.cameraData.cameraTargetDescriptor, depthTexture);
        renderer.EnqueuePass(depthOnlyPass);

        renderPass.Setup(settings);
        renderer.EnqueuePass(renderPass);
    }

    public override void Create()
    {
        var structureTensorMaterial = CoreUtils.CreateEngineMaterial("Hidden/Oil Painting/Structure Tensor");
        var kuwaharaFilterMaterial = CoreUtils.CreateEngineMaterial("Hidden/Oil Painting/Anisotropic Kuwahara Filter");
        var lineIntegralConvolutionMaterial = CoreUtils.CreateEngineMaterial("Hidden/Oil Painting/Line Integral Convolution");
        var compositorMaterial = CoreUtils.CreateEngineMaterial("Hidden/Oil Painting/Compositor");

        renderPass = new OilPaintingEffectPass(structureTensorMaterial,
                                               kuwaharaFilterMaterial,
                                               lineIntegralConvolutionMaterial,
                                               compositorMaterial);

        renderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        var texture = new Texture2D(FilterKernelSize, FilterKernelSize, TextureFormat.RFloat, true);
        InitializeFilterKernelTexture(texture,
                                      FilterKernelSize,
                                      settings.anisotropicKuwaharaFilterSettings.filterKernelSectors,
                                      settings.anisotropicKuwaharaFilterSettings.filterKernelSmoothness);

        settings.anisotropicKuwaharaFilterSettings.filterKernelTexture = texture;

        depthOnlyPass = new DepthOnlyPass(RenderPassEvent.BeforeRenderingPostProcessing,
                                          RenderQueueRange.all,
                                          AllLayers);

        depthTexture.Init("_CameraDepthTexture");
    }

    private static void InitializeFilterKernelTexture(Texture2D texture, int kernelSize, int sectorCount, float smoothing)
    {
        for (int j = 0; j < texture.height; j++)
        {
            for (int i = 0; i < texture.width; i++)
            {
                float x = i - 0.5f * texture.width + 0.5f;
                float y = j - 0.5f * texture.height + 0.5f;
                float r = Mathf.Sqrt(x * x + y * y);

                float a = 0.5f * Mathf.Atan2(y, x) / Mathf.PI;

                if (a > 0.5f)
                {
                    a -= 1f;
                }
                if (a < -0.5f)
                {
                    a += 1f;
                }

                if ((Mathf.Abs(a) <= 0.5f / sectorCount) && (r < 0.5f * kernelSize))
                {
                    texture.SetPixel(i, j, Color.red);
                }
                else
                {
                    texture.SetPixel(i, j, Color.black);
                }
            }
        }

        float sigma = 0.25f * (kernelSize - 1);

        GaussianBlur(texture, sigma * smoothing);

        float maxValue = 0f;
        for (int j = 0; j < texture.height; j++)
        {
            for (int i = 0; i < texture.width; i++)
            {
                var x = i - 0.5f * texture.width + 0.5f;
                var y = j - 0.5f * texture.height + 0.5f;
                var r = Mathf.Sqrt(x * x + y * y);

                var color = texture.GetPixel(i, j);
                color *= Mathf.Exp(-0.5f * r * r / sigma / sigma);
                texture.SetPixel(i, j, color);

                if (color.r > maxValue)
                {
                    maxValue = color.r;
                }
            }
        }

        for (int j = 0; j < texture.height; j++)
        {
            for (int i = 0; i < texture.width; i++)
            {
                var color = texture.GetPixel(i, j);
                color /= maxValue;
                texture.SetPixel(i, j, color);
            }
        }

        texture.Apply(true, true);
    }

    private static void GaussianBlur(Texture2D texture, float sigma)
    {
        float twiceSigmaSq = 2.0f * sigma * sigma;
        int halfWidth = Mathf.CeilToInt(2 * sigma);

        var colors = new Color[texture.width * texture.height];

        for (int y = 0; y < texture.height; y++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                int index = y * texture.width + x;

                float norm = 0;
                for (int i = -halfWidth; i <= halfWidth; i++)
                {
                    int xi = x + i;
                    if (xi < 0 || xi >= texture.width) continue;

                    for (int j = -halfWidth; j <= halfWidth; j++)
                    {
                        int yj = y + j;
                        if (yj < 0 || yj >= texture.height) continue;

                        float distance = Mathf.Sqrt(i * i + j * j);
                        float k = Mathf.Exp(-distance * distance / twiceSigmaSq);

                        colors[index] += texture.GetPixel(xi, yj) * k;
                        norm += k;
                    }
                }

                colors[index] /= norm;
            }
        }

        texture.SetPixels(colors);
    }

    [Serializable]
    public class Settings
    {
        public AnisotropicKuwaharaFilterSettings anisotropicKuwaharaFilterSettings;
        public EdgeFlowSettings edgeFlowSettings;
        public CompositorSettings compositorSettings;
    }

    [Serializable]
    public class AnisotropicKuwaharaFilterSettings
    {
        [Range(3, 8)]
        public int filterKernelSectors = 8;
        [Range(0f, 1f)]
        public float filterKernelSmoothness = 0.33f;
        [NonSerialized]
        public Texture2D filterKernelTexture;

        [Range(2f, 12f)]
        public float filterRadius = 4f;
        [Range(2f, 16f)]
        public float filterSharpness = 8f;
        [Range(0.125f, 8f)]
        public float eccentricity = 1f;

        [Range(1, 4)]
        public int iterations = 1;
    }

    [Serializable]
    public class EdgeFlowSettings
    {
        public Texture2D noiseTexture;

        [Range(1, 64)]
        public int streamLineLength = 10;
        [Range(0f, 2f)]
        public float streamKernelStrength = 0.5f;
    }

    [Serializable]
    public class CompositorSettings
    {
        [Range(0f, 4f)]
        public float edgeContribution = 1f;
        [Range(0f, 4f)]
        public float flowContribution = 1f;
        [Range(0f, 4f)]
        public float depthContribution = 1f;

        [Range(0.25f, 1f)]
        public float bumpPower = 0.8f;
        [Range(0f, 1f)]
        public float bumpIntensity = 0.4f;
    }
}
