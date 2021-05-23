using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OilPaintingEffectPass : ScriptableRenderPass
{
    private RenderTargetIdentifier source;
    private RenderTargetIdentifier destination;

    private RenderTexture structureTensorTex;
    private RenderTexture kuwaharaFilterTex;
    private RenderTexture edgeFlowTex;

    private readonly Material structureTensorMaterial;
    private readonly Material kuwaharaFilterMaterial;
    private readonly Material lineIntegralConvolutionMaterial;
    private readonly Material compositorMaterial;

    private int kuwaharaFilterIterations = 1;

    public OilPaintingEffectPass(Material structureTensorMaterial,
                                 Material kuwaharaFilterMaterial,
                                 Material lineIntegralConvolutionMaterial,
                                 Material compositorMaterial)
    {
        this.structureTensorMaterial = structureTensorMaterial;
        this.kuwaharaFilterMaterial = kuwaharaFilterMaterial;
        this.lineIntegralConvolutionMaterial = lineIntegralConvolutionMaterial;
        this.compositorMaterial = compositorMaterial;
    }

    public void Setup(OilPaintingEffect.Settings settings)
    {
        SetupKuwaharaFilter(settings.anisotropicKuwaharaFilterSettings);
        SetupLineIntegralConvolution(settings.edgeFlowSettings);
        SetupCompositor(settings.compositorSettings);
    }

    private void SetupKuwaharaFilter(OilPaintingEffect.AnisotropicKuwaharaFilterSettings kuwaharaFilterSettings)
    {
        kuwaharaFilterMaterial.SetInt("_FilterKernelSectors", kuwaharaFilterSettings.filterKernelSectors);
        kuwaharaFilterMaterial.SetTexture("_FilterKernelTex", kuwaharaFilterSettings.filterKernelTexture);
        kuwaharaFilterMaterial.SetFloat("_FilterRadius", kuwaharaFilterSettings.filterRadius);
        kuwaharaFilterMaterial.SetFloat("_FilterSharpness", kuwaharaFilterSettings.filterSharpness);
        kuwaharaFilterMaterial.SetFloat("_Eccentricity", kuwaharaFilterSettings.eccentricity);
        kuwaharaFilterIterations = kuwaharaFilterSettings.iterations;
    }

    private void SetupLineIntegralConvolution(OilPaintingEffect.EdgeFlowSettings edgeFlowSettings)
    {
        lineIntegralConvolutionMaterial.SetTexture("_NoiseTex", edgeFlowSettings.noiseTexture);
        lineIntegralConvolutionMaterial.SetInt("_StreamLineLength", edgeFlowSettings.streamLineLength);
        lineIntegralConvolutionMaterial.SetFloat("_StreamKernelStrength", edgeFlowSettings.streamKernelStrength);
    }

    private void SetupCompositor(OilPaintingEffect.CompositorSettings compositorSettings)
    {
        compositorMaterial.SetFloat("_EdgeContribution", compositorSettings.edgeContribution);
        compositorMaterial.SetFloat("_FlowContribution", compositorSettings.flowContribution);
        compositorMaterial.SetFloat("_DepthContribution", compositorSettings.depthContribution);
        compositorMaterial.SetFloat("_BumpPower", compositorSettings.bumpPower);
        compositorMaterial.SetFloat("_BumpIntensity", compositorSettings.bumpIntensity);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor blitTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        blitTargetDescriptor.depthBufferBits = 0;

        var renderer = renderingData.cameraData.renderer;

        source = renderer.cameraColorTarget;
        destination = renderer.cameraColorTarget;

        structureTensorTex = RenderTexture.GetTemporary(blitTargetDescriptor.width, blitTargetDescriptor.height, 0, RenderTextureFormat.ARGBFloat);
        kuwaharaFilterTex = RenderTexture.GetTemporary(blitTargetDescriptor);
        edgeFlowTex = RenderTexture.GetTemporary(blitTargetDescriptor.width, blitTargetDescriptor.height, 0, RenderTextureFormat.RFloat);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get("Oil Painting Effect");

        Blit(cmd, source, structureTensorTex, structureTensorMaterial, -1);

        kuwaharaFilterMaterial.SetTexture("_StructureTensorTex", structureTensorTex);

        Blit(cmd, source, kuwaharaFilterTex, kuwaharaFilterMaterial, -1);
        for (int i = 0; i < kuwaharaFilterIterations - 1; i++)
        {
            Blit(cmd, kuwaharaFilterTex, kuwaharaFilterTex, kuwaharaFilterMaterial, -1);
        }

        Blit(cmd, structureTensorTex, edgeFlowTex, lineIntegralConvolutionMaterial, -1);

        compositorMaterial.SetTexture("_EdgeFlowTex", edgeFlowTex);

        Blit(cmd, kuwaharaFilterTex, destination, compositorMaterial, -1);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void FrameCleanup(CommandBuffer cmd)
    {
        RenderTexture.ReleaseTemporary(structureTensorTex);
        RenderTexture.ReleaseTemporary(kuwaharaFilterTex);
        RenderTexture.ReleaseTemporary(edgeFlowTex);
    }
}
