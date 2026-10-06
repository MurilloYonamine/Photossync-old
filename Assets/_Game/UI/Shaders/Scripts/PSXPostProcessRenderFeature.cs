using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FifthSemester.Core.Services;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Game.UI.Shaders
{
    public class PSXPostProcessRenderFeature : ScriptableRendererFeature
    {
        private const string TAG = "<color=yellow><b>[PSXPostProcessRenderFeature]</b></color>";

        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            public Shader customShader;
        }

        public Settings settings = new Settings();

        private PSXRenderPass _pass;

        public override void Create()
        {
            if (settings.customShader == null)
            {
                settings.customShader = Shader.Find("PSX/CRT_Composite");
            }

            _pass = new PSXRenderPass(settings.customShader, settings.renderPassEvent);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || settings.customShader == null)
                return;

            if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
                return;

            renderer.EnqueuePass(_pass);
        }

        class PSXRenderPass : ScriptableRenderPass
        {
            private Material _material;

            public PSXRenderPass(Shader shader, RenderPassEvent passEvent)
            {
                this.renderPassEvent = passEvent;
                if (shader != null)
                {
                    _material = CoreUtils.CreateEngineMaterial(shader);
                }
            }

            private void UpdateMaterialProperties(PSXVolume psxVolume)
            {
                ServiceLocator.TryGet<ISettingsService>(out var settingsService);

                _material.SetFloat("_EnablePixelate", (settingsService?.Pixelation ?? psxVolume.enablePixelation.value) ? 1.0f : 0.0f);
                _material.SetFloat("_PixelResolutionX", psxVolume.pixelResolutionX.value);
                _material.SetFloat("_PixelResolutionY", psxVolume.pixelResolutionY.value);

                _material.SetFloat("_EnableBarrel", (settingsService?.BarrelDistortion ?? psxVolume.enableBarrelDistortion.value) ? 1.0f : 0.0f);
                _material.SetFloat("_BarrelStrength", psxVolume.barrelStrength.value);
                _material.SetFloat("_BarrelTightness", psxVolume.barrelTightness.value);
                _material.SetFloat("_BarrelZoom", psxVolume.barrelZoom.value);
                _material.SetFloat("_Vignette", psxVolume.cornerVignette.value);

                _material.SetFloat("_EnableDither", (settingsService?.Dithering ?? psxVolume.enableDithering.value) ? 1.0f : 0.0f);
                _material.SetFloat("_DitherSpread", psxVolume.ditherSpread.value);
                _material.SetFloat("_DitherStrength", psxVolume.ditherStrength.value);

                _material.SetFloat("_EnableChromaBleed", psxVolume.enableChromaBleed.value ? 1.0f : 0.0f);
                _material.SetFloat("_BleedAmount", psxVolume.bleedSpread.value);

                _material.SetFloat("_EnableScanlines", (settingsService?.Scanlines ?? psxVolume.enableScanlines.value) ? 1.0f : 0.0f);
                _material.SetFloat("_EnableRollingBands", (settingsService?.RollingBands ?? psxVolume.enableScanlines.value) ? 1.0f : 0.0f);
                _material.SetFloat("_ScanlineCount", psxVolume.scanlineCount.value);
                _material.SetFloat("_ScanlineIntensity", psxVolume.scanlineIntensity.value);
                _material.SetFloat("_RollingBandSpeed", psxVolume.rollingBandSpeed.value);
                _material.SetFloat("_RollingBandIntensity", psxVolume.rollingBandIntensity.value);

                _material.SetFloat("_EnableGlitch", (settingsService?.VHSEffect ?? psxVolume.enableGlitch.value) ? 1.0f : 0.0f);
                _material.SetFloat("_GlitchAmount", psxVolume.glitchAmount.value);
                _material.SetFloat("_VhsGrain", psxVolume.vhsTapeGrain.value);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ConfigureInput(ScriptableRenderPassInput.Color);
            }

            #if UNITY_6000_0_OR_NEWER
            private class PassData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle destination;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView)
                    return;

                var stack = VolumeManager.instance.stack;
                var psxVolume = stack.GetComponent<PSXVolume>();
                if (psxVolume == null || !psxVolume.IsActive()) return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle source = resourceData.activeColorTexture;

                if (!source.IsValid() || _material == null) return;

                UpdateMaterialProperties(psxVolume);

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_PSXTempTexture", true);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("PSX Post Process Pass", out var passData))
                {
                    passData.material = _material;
                    passData.source = source;
                    passData.destination = destination;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                    });
                }

                resourceData.cameraColor = destination;
            }
            #endif

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null) return;

                var stack = VolumeManager.instance.stack;
                var psxVolume = stack.GetComponent<PSXVolume>();

                if (psxVolume == null || !psxVolume.IsActive()) return;

                UpdateMaterialProperties(psxVolume);

                CommandBuffer cmd = CommandBufferPool.Get("PSX Post Process Pass");

                int tempTextureId = Shader.PropertyToID("_TempPSXTexture");
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;

                cmd.GetTemporaryRT(tempTextureId, desc);

                #if UNITY_2022_1_OR_NEWER
                RTHandle cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                Blitter.BlitCameraTexture(cmd, cameraTarget, cameraTarget, _material, 0);
                #else
                RenderTargetIdentifier cameraTarget = renderingData.cameraData.renderer.cameraColorTarget;
                cmd.Blit(cameraTarget, tempTextureId, _material, 0);
                cmd.Blit(tempTextureId, cameraTarget);
                #endif

                cmd.ReleaseTemporaryRT(tempTextureId);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
