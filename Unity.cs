using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityObject = UnityEngine.Object;

//Credit for all of the below code goes to Unity Technologies (with minor edits made by The White Guardian to make everything work in this new configuration)
namespace KSP_PostProcessing
{
    #region Runtime

    #region Attributes
    //GetSetAttribute.cs in Runtime/Attributes
    public sealed class GetSetAttribute : PropertyAttribute
    {
        public readonly string name;
        public bool dirty;

        public GetSetAttribute(string name)
        {
            this.name = name;
        }
    }
    //MinAttribute.cs in Runtime/Attributes
    public sealed class MinAttribute : PropertyAttribute
    {
        public readonly float min;

        public MinAttribute(float min)
        {
            this.min = min;
        }
    }
    //TrackballAttribute.cs in Runtime/Attributes
    public sealed class TrackballAttribute : PropertyAttribute
    {
        public readonly string method;

        public TrackballAttribute(string method)
        {
            this.method = method;
        }
    }
    //TrackballGroupAttribute.cs in Runtime/Attributes
    public sealed class TrackballGroupAttribute : PropertyAttribute
    {
    }
    //Maybe this one isn't that necessary...
    #endregion

    #region Components
    public sealed class AmbientOcclusionComponent : PostProcessingComponentCommandBuffer<AmbientOcclusionModel>
    {
        static class Uniforms
        {
            internal static readonly int _Intensity = Shader.PropertyToID("_Intensity");
            internal static readonly int _Radius = Shader.PropertyToID("_Radius");
            internal static readonly int _FogParams = Shader.PropertyToID("_FogParams");
            internal static readonly int _Downsample = Shader.PropertyToID("_Downsample");
            internal static readonly int _SampleCount = Shader.PropertyToID("_SampleCount");
            internal static readonly int _OcclusionTexture1 = Shader.PropertyToID("_OcclusionTexture1");
            internal static readonly int _OcclusionTexture2 = Shader.PropertyToID("_OcclusionTexture2");
            internal static readonly int _OcclusionTexture = Shader.PropertyToID("_OcclusionTexture");
            internal static readonly int _MainTex = Shader.PropertyToID("_MainTex");
            internal static readonly int _TempRT = Shader.PropertyToID("_TempRT");
        }

        const string k_BlitShaderString = "Hidden/Post FX/Blit";
        const string k_ShaderString = "Hidden/Post FX/Ambient Occlusion";

        readonly RenderTargetIdentifier[] m_MRT =
        {
            BuiltinRenderTextureType.GBuffer0, // Albedo, Occ
            BuiltinRenderTextureType.CameraTarget // Ambient
        };

        enum OcclusionSource
        {
            DepthTexture,
            DepthNormalsTexture,
            GBuffer
        }

        OcclusionSource occlusionSource
        {
            get
            {
                if (context.isGBufferAvailable && !model.settings.forceForwardCompatibility)
                    return OcclusionSource.GBuffer;

                if (model.settings.highPrecision && (!context.isGBufferAvailable || model.settings.forceForwardCompatibility))
                    return OcclusionSource.DepthTexture;

                return OcclusionSource.DepthNormalsTexture;
            }
        }

        bool ambientOnlySupported
        {
            get { return context.isHdr && model.settings.ambientOnly && context.isGBufferAvailable && !model.settings.forceForwardCompatibility; }
        }

        public override bool active
        {
            get
            {
                return model.enabled
                       && model.settings.intensity > 0f
                       && !context.interrupted;
            }
        }

        public override DepthTextureMode GetCameraFlags()
        {
            var flags = DepthTextureMode.None;

            if (occlusionSource == OcclusionSource.DepthTexture)
                flags |= DepthTextureMode.Depth;

            if (occlusionSource != OcclusionSource.GBuffer)
                flags |= DepthTextureMode.DepthNormals;

            return flags;
        }

        public override string GetName()
        {
            return "Ambient Occlusion";
        }

        public override CameraEvent GetCameraEvent()
        {
            return ambientOnlySupported && !context.profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.AmbientOcclusion)
                   ? CameraEvent.BeforeReflections
                   : CameraEvent.BeforeImageEffectsOpaque;
        }

        public override void PopulateCommandBuffer(CommandBuffer cb)
        {
            var settings = model.settings;

            // Material setup
            var blitMaterial = context.materialFactory.Get(k_BlitShaderString);

            var material = context.materialFactory.Get(k_ShaderString);
            material.shaderKeywords = null;
            material.SetFloat(Uniforms._Intensity, settings.intensity);
            material.SetFloat(Uniforms._Radius, settings.radius);
            material.SetFloat(Uniforms._Downsample, settings.downsampling ? 0.5f : 1f);
            material.SetInt(Uniforms._SampleCount, (int)settings.sampleCount);

            if (!context.isGBufferAvailable && RenderSettings.fog)
            {
                material.SetVector(Uniforms._FogParams, new Vector3(RenderSettings.fogDensity, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance));

                switch (RenderSettings.fogMode)
                {
                    case FogMode.Linear:
                        material.EnableKeyword("FOG_LINEAR");
                        break;
                    case FogMode.Exponential:
                        material.EnableKeyword("FOG_EXP");
                        break;
                    case FogMode.ExponentialSquared:
                        material.EnableKeyword("FOG_EXP2");
                        break;
                }
            }
            else
            {
                material.EnableKeyword("FOG_OFF");
            }

            int tw = context.width;
            int th = context.height;
            int ts = settings.downsampling ? 2 : 1;
            const RenderTextureFormat kFormat = RenderTextureFormat.ARGB32;
            const RenderTextureReadWrite kRWMode = RenderTextureReadWrite.Linear;
            const FilterMode kFilter = FilterMode.Bilinear;

            // AO buffer
            var rtMask = Uniforms._OcclusionTexture1;
            cb.GetTemporaryRT(rtMask, tw / ts, th / ts, 0, kFilter, kFormat, kRWMode);

            // AO estimation
            cb.Blit((Texture)null, rtMask, material, (int)occlusionSource);

            // Blur buffer
            var rtBlur = Uniforms._OcclusionTexture2;

            // Separable blur (horizontal pass)
            cb.GetTemporaryRT(rtBlur, tw, th, 0, kFilter, kFormat, kRWMode);
            cb.SetGlobalTexture(Uniforms._MainTex, rtMask);
            cb.Blit(rtMask, rtBlur, material, occlusionSource == OcclusionSource.GBuffer ? 4 : 3);
            cb.ReleaseTemporaryRT(rtMask);

            // Separable blur (vertical pass)
            rtMask = Uniforms._OcclusionTexture;
            cb.GetTemporaryRT(rtMask, tw, th, 0, kFilter, kFormat, kRWMode);
            cb.SetGlobalTexture(Uniforms._MainTex, rtBlur);
            cb.Blit(rtBlur, rtMask, material, 5);
            cb.ReleaseTemporaryRT(rtBlur);

            if (context.profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.AmbientOcclusion))
            {
                cb.SetGlobalTexture(Uniforms._MainTex, rtMask);
                cb.Blit(rtMask, BuiltinRenderTextureType.CameraTarget, material, 8);
                context.Interrupt();
            }
            else if (ambientOnlySupported)
            {
                cb.SetRenderTarget(m_MRT, BuiltinRenderTextureType.CameraTarget);
                cb.DrawMesh(GraphicsUtils.quad, Matrix4x4.identity, material, 0, 7);
            }
            else
            {
                var fbFormat = context.isHdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;

                int tempRT = Uniforms._TempRT;
                cb.GetTemporaryRT(tempRT, context.width, context.height, 0, FilterMode.Bilinear, fbFormat);
                cb.Blit(BuiltinRenderTextureType.CameraTarget, tempRT, blitMaterial, 0);
                cb.SetGlobalTexture(Uniforms._MainTex, tempRT);
                cb.Blit(tempRT, BuiltinRenderTextureType.CameraTarget, material, 6);
                cb.ReleaseTemporaryRT(tempRT);
            }

            cb.ReleaseTemporaryRT(rtMask);
        }
    }

    public sealed class BloomComponent : PostProcessingComponentRenderTexture<BloomModel>
    {
        static class Uniforms
        {
            internal static readonly int _AutoExposure = Shader.PropertyToID("_AutoExposure");
            internal static readonly int _Threshold = Shader.PropertyToID("_Threshold");
            internal static readonly int _Curve = Shader.PropertyToID("_Curve");
            internal static readonly int _PrefilterOffs = Shader.PropertyToID("_PrefilterOffs");
            internal static readonly int _SampleScale = Shader.PropertyToID("_SampleScale");
            internal static readonly int _BaseTex = Shader.PropertyToID("_BaseTex");
            internal static readonly int _BloomTex = Shader.PropertyToID("_BloomTex");
            internal static readonly int _Bloom_Settings = Shader.PropertyToID("_Bloom_Settings");
            internal static readonly int _Bloom_DirtTex = Shader.PropertyToID("_Bloom_DirtTex");
            internal static readonly int _Bloom_DirtIntensity = Shader.PropertyToID("_Bloom_DirtIntensity");
        }

        const int k_MaxPyramidBlurLevel = 16;
        readonly RenderTexture[] m_BlurBuffer1 = new RenderTexture[k_MaxPyramidBlurLevel];
        readonly RenderTexture[] m_BlurBuffer2 = new RenderTexture[k_MaxPyramidBlurLevel];

        public override bool active
        {
            get
            {
                return model.enabled
                       && model.settings.bloom.intensity > 0f
                       && !context.interrupted;
            }
        }

        public void Prepare(RenderTexture source, Material uberMaterial, Texture autoExposure)
        {
            var bloom = model.settings.bloom;
            var lensDirt = model.settings.lensDirt;
            var material = context.materialFactory.Get("Hidden/Post FX/Bloom");
            material.shaderKeywords = null;

            // Apply auto exposure before the prefiltering pass
            material.SetTexture(Uniforms._AutoExposure, autoExposure);

            // Do bloom on a half-res buffer, full-res doesn't bring much and kills performances on
            // fillrate limited platforms
            var tw = context.width / 2;
            var th = context.height / 2;

            // Blur buffer format
            // TODO: Extend the use of RGBM to the whole chain for mobile platforms
            var useRGBM = Application.isMobilePlatform;
            var rtFormat = useRGBM
                ? RenderTextureFormat.Default
                : RenderTextureFormat.DefaultHDR;

            // Determine the iteration count
            float logh = Mathf.Log(th, 2f) + bloom.radius - 8f;
            int logh_i = (int)logh;
            int iterations = Mathf.Clamp(logh_i, 1, k_MaxPyramidBlurLevel);

            // Uupdate the shader properties
            float lthresh = bloom.thresholdLinear;
            material.SetFloat(Uniforms._Threshold, lthresh);

            float knee = lthresh * bloom.softKnee + 1e-5f;
            var curve = new Vector3(lthresh - knee, knee * 2f, 0.25f / knee);
            material.SetVector(Uniforms._Curve, curve);

            material.SetFloat(Uniforms._PrefilterOffs, bloom.antiFlicker ? -0.5f : 0f);

            float sampleScale = 0.5f + logh - logh_i;
            material.SetFloat(Uniforms._SampleScale, sampleScale);

            // TODO: Probably can disable antiFlicker if TAA is enabled - need to do some testing
            if (bloom.antiFlicker)
                material.EnableKeyword("ANTI_FLICKER");

            // Prefilter pass
            var prefiltered = context.renderTextureFactory.Get(tw, th, 0, rtFormat);
            KS3PUtil.Blit(source, prefiltered, material, 0);

            // Construct a mip pyramid
            var last = prefiltered;

            for (int level = 0; level < iterations; level++)
            {
                m_BlurBuffer1[level] = context.renderTextureFactory.Get(
                        last.width / 2, last.height / 2, 0, rtFormat
                        );

                int pass = (level == 0) ? 1 : 2;
                KS3PUtil.Blit(last, m_BlurBuffer1[level], material, pass);

                last = m_BlurBuffer1[level];
            }

            // Upsample and combine loop
            for (int level = iterations - 2; level >= 0; level--)
            {
                var baseTex = m_BlurBuffer1[level];
                material.SetTexture(Uniforms._BaseTex, baseTex);

                m_BlurBuffer2[level] = context.renderTextureFactory.Get(
                        baseTex.width, baseTex.height, 0, rtFormat
                        );

                KS3PUtil.Blit(last, m_BlurBuffer2[level], material, 3);
                last = m_BlurBuffer2[level];
            }

            var bloomTex = last;

            // Release the temporary buffers
            for (int i = 0; i < k_MaxPyramidBlurLevel; i++)
            {
                if (m_BlurBuffer1[i] != null)
                    context.renderTextureFactory.Release(m_BlurBuffer1[i]);

                if (m_BlurBuffer2[i] != null && m_BlurBuffer2[i] != bloomTex)
                    context.renderTextureFactory.Release(m_BlurBuffer2[i]);

                m_BlurBuffer1[i] = null;
                m_BlurBuffer2[i] = null;
            }

            context.renderTextureFactory.Release(prefiltered);

            // Push everything to the uber material
            uberMaterial.SetTexture(Uniforms._BloomTex, bloomTex);
            uberMaterial.SetVector(Uniforms._Bloom_Settings, new Vector2(sampleScale, bloom.intensity));

            if (lensDirt.intensity > 0f && lensDirt.texture != null)
            {
                uberMaterial.SetTexture(Uniforms._Bloom_DirtTex, lensDirt.texture);
                uberMaterial.SetFloat(Uniforms._Bloom_DirtIntensity, lensDirt.intensity);
                uberMaterial.EnableKeyword("BLOOM_LENS_DIRT");
            }
            else
            {
                uberMaterial.EnableKeyword("BLOOM");
            }
        }
    }

    public sealed class BuiltinDebugViewsComponent : PostProcessingComponentCommandBuffer<BuiltinDebugViewsModel>
    {
        static class Uniforms
        {
            internal static readonly int _DepthScale = Shader.PropertyToID("_DepthScale");
            internal static readonly int _TempRT = Shader.PropertyToID("_TempRT");
            internal static readonly int _Opacity = Shader.PropertyToID("_Opacity");
            internal static readonly int _MainTex = Shader.PropertyToID("_MainTex");
            internal static readonly int _TempRT2 = Shader.PropertyToID("_TempRT2");
            internal static readonly int _Amplitude = Shader.PropertyToID("_Amplitude");
            internal static readonly int _Scale = Shader.PropertyToID("_Scale");
        }

        const string k_ShaderString = "Hidden/Post FX/Builtin Debug Views";

        enum Pass
        {
            Depth,
            Normals,
            MovecOpacity,
            MovecImaging,
            MovecArrows
        }

        ArrowArray m_Arrows;

        class ArrowArray
        {
            public Mesh mesh { get; private set; }

            public int columnCount { get; private set; }
            public int rowCount { get; private set; }

            public void BuildMesh(int columns, int rows)
            {
                // Base shape
                var arrow = new Vector3[6]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(-1f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 1f, 0f)
                };

                // make the vertex array
                int vcount = 6 * columns * rows;
                var vertices = new List<Vector3>(vcount);
                var uvs = new List<Vector2>(vcount);

                for (int iy = 0; iy < rows; iy++)
                {
                    for (int ix = 0; ix < columns; ix++)
                    {
                        var uv = new Vector2(
                                (0.5f + ix) / columns,
                                (0.5f + iy) / rows
                                );

                        for (int i = 0; i < 6; i++)
                        {
                            vertices.Add(arrow[i]);
                            uvs.Add(uv);
                        }
                    }
                }

                // make the index array
                var indices = new int[vcount];

                for (int i = 0; i < vcount; i++)
                    indices[i] = i;

                // initialize the mesh object
                mesh = new Mesh { hideFlags = HideFlags.DontSave };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetIndices(indices, MeshTopology.Lines, 0);
                mesh.UploadMeshData(true);

                // update the properties
                columnCount = columns;
                rowCount = rows;
            }

            public void Release()
            {
                GraphicsUtils.Destroy(mesh);
                mesh = null;
            }
        }

        public override bool active
        {
            get
            {
                return model.IsModeActive(BuiltinDebugViewsModel.Mode.Depth)
                       || model.IsModeActive(BuiltinDebugViewsModel.Mode.Normals)
                       || model.IsModeActive(BuiltinDebugViewsModel.Mode.MotionVectors);
            }
        }

        public override DepthTextureMode GetCameraFlags()
        {
            var mode = model.settings.mode;
            var flags = DepthTextureMode.None;

            switch (mode)
            {
                case BuiltinDebugViewsModel.Mode.Normals:
                    flags |= DepthTextureMode.DepthNormals;
                    break;
                case BuiltinDebugViewsModel.Mode.MotionVectors:
                    flags |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
                    break;
                case BuiltinDebugViewsModel.Mode.Depth:
                    flags |= DepthTextureMode.Depth;
                    break;
            }

            return flags;
        }

        public override CameraEvent GetCameraEvent()
        {
            return model.settings.mode == BuiltinDebugViewsModel.Mode.MotionVectors
                   ? CameraEvent.BeforeImageEffects
                   : CameraEvent.BeforeImageEffectsOpaque;
        }

        public override string GetName()
        {
            return "Builtin Debug Views";
        }

        public override void PopulateCommandBuffer(CommandBuffer cb)
        {
            var settings = model.settings;
            var material = context.materialFactory.Get(k_ShaderString);
            material.shaderKeywords = null;

            if (context.isGBufferAvailable)
                material.EnableKeyword("SOURCE_GBUFFER");

            switch (settings.mode)
            {
                case BuiltinDebugViewsModel.Mode.Depth:
                    DepthPass(cb);
                    break;
                case BuiltinDebugViewsModel.Mode.Normals:
                    DepthNormalsPass(cb);
                    break;
                case BuiltinDebugViewsModel.Mode.MotionVectors:
                    MotionVectorsPass(cb);
                    break;
            }

            context.Interrupt();
        }

        void DepthPass(CommandBuffer cb)
        {
            var material = context.materialFactory.Get(k_ShaderString);
            var settings = model.settings.depth;

            cb.SetGlobalFloat(Uniforms._DepthScale, 1f / settings.scale);
            cb.Blit((Texture)null, BuiltinRenderTextureType.CameraTarget, material, (int)Pass.Depth);
        }

        void DepthNormalsPass(CommandBuffer cb)
        {
            var material = context.materialFactory.Get(k_ShaderString);
            cb.Blit((Texture)null, BuiltinRenderTextureType.CameraTarget, material, (int)Pass.Normals);
        }

        void MotionVectorsPass(CommandBuffer cb)
        {
#if UNITY_EDITOR
            // Don't render motion vectors preview when the editor is not playing as it can in some
            // cases results in ugly artifacts (i.e. when resizing the game view).
            if (!Application.isPlaying)
                return;
#endif

            var material = context.materialFactory.Get(k_ShaderString);
            var settings = model.settings.motionVectors;

            // Blit the original source image
            int tempRT = Uniforms._TempRT;
            cb.GetTemporaryRT(tempRT, context.width, context.height, 0, FilterMode.Bilinear);
            cb.SetGlobalFloat(Uniforms._Opacity, settings.sourceOpacity);
            cb.SetGlobalTexture(Uniforms._MainTex, BuiltinRenderTextureType.CameraTarget);
            cb.Blit(BuiltinRenderTextureType.CameraTarget, tempRT, material, (int)Pass.MovecOpacity);

            // Motion vectors (imaging)
            if (settings.motionImageOpacity > 0f && settings.motionImageAmplitude > 0f)
            {
                int tempRT2 = Uniforms._TempRT2;
                cb.GetTemporaryRT(tempRT2, context.width, context.height, 0, FilterMode.Bilinear);
                cb.SetGlobalFloat(Uniforms._Opacity, settings.motionImageOpacity);
                cb.SetGlobalFloat(Uniforms._Amplitude, settings.motionImageAmplitude);
                cb.SetGlobalTexture(Uniforms._MainTex, tempRT);
                cb.Blit(tempRT, tempRT2, material, (int)Pass.MovecImaging);
                cb.ReleaseTemporaryRT(tempRT);
                tempRT = tempRT2;
            }

            // Motion vectors (arrows)
            if (settings.motionVectorsOpacity > 0f && settings.motionVectorsAmplitude > 0f)
            {
                PrepareArrows();

                float sy = 1f / settings.motionVectorsResolution;
                float sx = sy * context.height / context.width;

                cb.SetGlobalVector(Uniforms._Scale, new Vector2(sx, sy));
                cb.SetGlobalFloat(Uniforms._Opacity, settings.motionVectorsOpacity);
                cb.SetGlobalFloat(Uniforms._Amplitude, settings.motionVectorsAmplitude);
                cb.DrawMesh(m_Arrows.mesh, Matrix4x4.identity, material, 0, (int)Pass.MovecArrows);
            }

            cb.SetGlobalTexture(Uniforms._MainTex, tempRT);
            cb.Blit(tempRT, BuiltinRenderTextureType.CameraTarget);
            cb.ReleaseTemporaryRT(tempRT);
        }

        void PrepareArrows()
        {
            int row = model.settings.motionVectors.motionVectorsResolution;
            int col = row * Screen.width / Screen.height;

            if (m_Arrows == null)
                m_Arrows = new ArrowArray();

            if (m_Arrows.columnCount != col || m_Arrows.rowCount != row)
            {
                m_Arrows.Release();
                m_Arrows.BuildMesh(col, row);
            }
        }

        public override void OnDisable()
        {
            if (m_Arrows != null)
                m_Arrows.Release();

            m_Arrows = null;
        }
    }

    public sealed class ChromaticAberrationComponent : PostProcessingComponentRenderTexture<ChromaticAberrationModel>
    {
        static class Uniforms
        {
            internal static readonly int _ChromaticAberration_Amount = Shader.PropertyToID("_ChromaticAberration_Amount");
            internal static readonly int _ChromaticAberration_Spectrum = Shader.PropertyToID("_ChromaticAberration_Spectrum");
        }

        Texture2D m_SpectrumLut;

        public override bool active
        {
            get
            {
                return model.enabled
                       && model.settings.intensity > 0f
                       && !context.interrupted;
            }
        }

        public override void OnDisable()
        {
            GraphicsUtils.Destroy(m_SpectrumLut);
            m_SpectrumLut = null;
        }

        public override void Prepare(Material uberMaterial)
        {
            var settings = model.settings;
            var spectralLut = settings.spectralTexture;

            if (spectralLut == null)
            {
                if (m_SpectrumLut == null)
                {
                    m_SpectrumLut = new Texture2D(3, 1, TextureFormat.RGB24, false)
                    {
                        name = "Chromatic Aberration Spectrum Lookup",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        anisoLevel = 0,
                        hideFlags = HideFlags.DontSave
                    };

                    var pixels = new Color[3];
                    pixels[0] = new Color(1f, 0f, 0f);
                    pixels[1] = new Color(0f, 1f, 0f);
                    pixels[2] = new Color(0f, 0f, 1f);
                    m_SpectrumLut.SetPixels(pixels);
                    m_SpectrumLut.Apply();
                }

                spectralLut = m_SpectrumLut;
            }

            uberMaterial.EnableKeyword("CHROMATIC_ABERRATION");
            uberMaterial.SetFloat(Uniforms._ChromaticAberration_Amount, settings.intensity * 0.03f);
            uberMaterial.SetTexture(Uniforms._ChromaticAberration_Spectrum, spectralLut);
        }
    }

    public sealed class ColorGradingComponent : PostProcessingComponentRenderTexture<ColorGradingModel>
    {
        static class Uniforms
        {
            internal static readonly int _LutParams = Shader.PropertyToID("_LutParams");
            internal static readonly int _NeutralTonemapperParams1 = Shader.PropertyToID("_NeutralTonemapperParams1");
            internal static readonly int _NeutralTonemapperParams2 = Shader.PropertyToID("_NeutralTonemapperParams2");
            internal static readonly int _HueShift = Shader.PropertyToID("_HueShift");
            internal static readonly int _Saturation = Shader.PropertyToID("_Saturation");
            internal static readonly int _Contrast = Shader.PropertyToID("_Contrast");
            internal static readonly int _Balance = Shader.PropertyToID("_Balance");
            internal static readonly int _Lift = Shader.PropertyToID("_Lift");
            internal static readonly int _InvGamma = Shader.PropertyToID("_InvGamma");
            internal static readonly int _Gain = Shader.PropertyToID("_Gain");
            internal static readonly int _Slope = Shader.PropertyToID("_Slope");
            internal static readonly int _Power = Shader.PropertyToID("_Power");
            internal static readonly int _Offset = Shader.PropertyToID("_Offset");
            internal static readonly int _ChannelMixerRed = Shader.PropertyToID("_ChannelMixerRed");
            internal static readonly int _ChannelMixerGreen = Shader.PropertyToID("_ChannelMixerGreen");
            internal static readonly int _ChannelMixerBlue = Shader.PropertyToID("_ChannelMixerBlue");
            internal static readonly int _Curves = Shader.PropertyToID("_Curves");
            internal static readonly int _LogLut = Shader.PropertyToID("_LogLut");
            internal static readonly int _LogLut_Params = Shader.PropertyToID("_LogLut_Params");
            internal static readonly int _ExposureEV = Shader.PropertyToID("_ExposureEV");
        }

        const int k_InternalLogLutSize = 32;
        const int k_CurvePrecision = 128;
        const float k_CurveStep = 1f / k_CurvePrecision;

        Texture2D m_GradingCurves;
        Color[] m_pixels = new Color[k_CurvePrecision * 2];

        public override bool active
        {
            get
            {
                return model.enabled
                       && !context.interrupted;
            }
        }

        // An analytical model of chromaticity of the standard illuminant, by Judd et al.
        // http://en.wikipedia.org/wiki/Standard_illuminant#Illuminant_series_D
        // Slightly modifed to adjust it with the D65 white point (x=0.31271, y=0.32902).
        float StandardIlluminantY(float x)
        {
            return 2.87f * x - 3f * x * x - 0.27509507f;
        }

        // CIE xy chromaticity to CAT02 LMS.
        // http://en.wikipedia.org/wiki/LMS_color_space#CAT02
        Vector3 CIExyToLMS(float x, float y)
        {
            float Y = 1f;
            float X = Y * x / y;
            float Z = Y * (1f - x - y) / y;

            float L = 0.7328f * X + 0.4296f * Y - 0.1624f * Z;
            float M = -0.7036f * X + 1.6975f * Y + 0.0061f * Z;
            float S = 0.0030f * X + 0.0136f * Y + 0.9834f * Z;

            return new Vector3(L, M, S);
        }

        Vector3 CalculateColorBalance(float temperature, float tint)
        {
            // Range ~[-1.8;1.8] ; using higher ranges is unsafe
            float t1 = temperature / 55f;
            float t2 = tint / 55f;

            // Get the CIE xy chromaticity of the reference white point.
            // Note: 0.31271 = x value on the D65 white point
            float x = 0.31271f - t1 * (t1 < 0f ? 0.1f : 0.05f);
            float y = StandardIlluminantY(x) + t2 * 0.05f;

            // Calculate the coefficients in the LMS space.
            var w1 = new Vector3(0.949237f, 1.03542f, 1.08728f); // D65 white point
            var w2 = CIExyToLMS(x, y);
            return new Vector3(w1.x / w2.x, w1.y / w2.y, w1.z / w2.z);
        }

        static Color NormalizeColor(Color c)
        {
            float sum = (c.r + c.g + c.b) / 3f;

            if (Mathf.Approximately(sum, 0f))
                return new Color(1f, 1f, 1f, c.a);

            return new Color
            {
                r = c.r / sum,
                g = c.g / sum,
                b = c.b / sum,
                a = c.a
            };
        }

        static Vector3 ClampVector(Vector3 v, float min, float max)
        {
            return new Vector3(
                Mathf.Clamp(v.x, min, max),
                Mathf.Clamp(v.y, min, max),
                Mathf.Clamp(v.z, min, max)
                );
        }

        public static Vector3 GetLiftValue(Color lift)
        {
            const float kLiftScale = 0.1f;

            var nLift = NormalizeColor(lift);
            float avgLift = (nLift.r + nLift.g + nLift.b) / 3f;

            // Getting some artifacts when going into the negatives using a very low offset (lift.a) with non ACES-tonemapping
            float liftR = (nLift.r - avgLift) * kLiftScale + lift.a;
            float liftG = (nLift.g - avgLift) * kLiftScale + lift.a;
            float liftB = (nLift.b - avgLift) * kLiftScale + lift.a;

            return ClampVector(new Vector3(liftR, liftG, liftB), -1f, 1f);
        }

        public static Vector3 GetGammaValue(Color gamma)
        {
            const float kGammaScale = 0.5f;
            const float kMinGamma = 0.01f;

            var nGamma = NormalizeColor(gamma);
            float avgGamma = (nGamma.r + nGamma.g + nGamma.b) / 3f;

            gamma.a *= gamma.a < 0f ? 0.8f : 5f;
            float gammaR = Mathf.Pow(2f, (nGamma.r - avgGamma) * kGammaScale) + gamma.a;
            float gammaG = Mathf.Pow(2f, (nGamma.g - avgGamma) * kGammaScale) + gamma.a;
            float gammaB = Mathf.Pow(2f, (nGamma.b - avgGamma) * kGammaScale) + gamma.a;

            float invGammaR = 1f / Mathf.Max(kMinGamma, gammaR);
            float invGammaG = 1f / Mathf.Max(kMinGamma, gammaG);
            float invGammaB = 1f / Mathf.Max(kMinGamma, gammaB);

            return ClampVector(new Vector3(invGammaR, invGammaG, invGammaB), 0f, 5f);
        }

        public static Vector3 GetGainValue(Color gain)
        {
            const float kGainScale = 0.5f;

            var nGain = NormalizeColor(gain);
            float avgGain = (nGain.r + nGain.g + nGain.b) / 3f;

            gain.a *= gain.a > 0f ? 3f : 1f;
            float gainR = Mathf.Pow(2f, (nGain.r - avgGain) * kGainScale) + gain.a;
            float gainG = Mathf.Pow(2f, (nGain.g - avgGain) * kGainScale) + gain.a;
            float gainB = Mathf.Pow(2f, (nGain.b - avgGain) * kGainScale) + gain.a;

            return ClampVector(new Vector3(gainR, gainG, gainB), 0f, 4f);
        }

        public static void CalculateLiftGammaGain(Color lift, Color gamma, Color gain, out Vector3 outLift, out Vector3 outGamma, out Vector3 outGain)
        {
            outLift = GetLiftValue(lift);
            outGamma = GetGammaValue(gamma);
            outGain = GetGainValue(gain);
        }

        public static Vector3 GetSlopeValue(Color slope)
        {
            const float kSlopeScale = 0.1f;

            var nSlope = NormalizeColor(slope);
            float avgSlope = (nSlope.r + nSlope.g + nSlope.b) / 3f;

            slope.a *= 0.5f;
            float slopeR = (nSlope.r - avgSlope) * kSlopeScale + slope.a + 1f;
            float slopeG = (nSlope.g - avgSlope) * kSlopeScale + slope.a + 1f;
            float slopeB = (nSlope.b - avgSlope) * kSlopeScale + slope.a + 1f;

            return ClampVector(new Vector3(slopeR, slopeG, slopeB), 0f, 2f);
        }

        public static Vector3 GetPowerValue(Color power)
        {
            const float kPowerScale = 0.1f;
            const float minPower = 0.01f;

            var nPower = NormalizeColor(power);
            float avgPower = (nPower.r + nPower.g + nPower.b) / 3f;

            power.a *= 0.5f;
            float powerR = (nPower.r - avgPower) * kPowerScale + power.a + 1f;
            float powerG = (nPower.g - avgPower) * kPowerScale + power.a + 1f;
            float powerB = (nPower.b - avgPower) * kPowerScale + power.a + 1f;

            float invPowerR = 1f / Mathf.Max(minPower, powerR);
            float invPowerG = 1f / Mathf.Max(minPower, powerG);
            float invPowerB = 1f / Mathf.Max(minPower, powerB);

            return ClampVector(new Vector3(invPowerR, invPowerG, invPowerB), 0.5f, 2.5f);
        }

        public static Vector3 GetOffsetValue(Color offset)
        {
            const float kOffsetScale = 0.05f;

            var nOffset = NormalizeColor(offset);
            float avgOffset = (nOffset.r + nOffset.g + nOffset.b) / 3f;

            offset.a *= 0.5f;
            float offsetR = (nOffset.r - avgOffset) * kOffsetScale + offset.a;
            float offsetG = (nOffset.g - avgOffset) * kOffsetScale + offset.a;
            float offsetB = (nOffset.b - avgOffset) * kOffsetScale + offset.a;

            return ClampVector(new Vector3(offsetR, offsetG, offsetB), -0.8f, 0.8f);
        }

        public static void CalculateSlopePowerOffset(Color slope, Color power, Color offset, out Vector3 outSlope, out Vector3 outPower, out Vector3 outOffset)
        {
            outSlope = GetSlopeValue(slope);
            outPower = GetPowerValue(power);
            outOffset = GetOffsetValue(offset);
        }

        TextureFormat GetCurveFormat()
        {
            if (SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
                return TextureFormat.RGBAHalf;

            return TextureFormat.RGBA32;
        }

        Texture2D GetCurveTexture()
        {
            if (m_GradingCurves == null)
            {
                m_GradingCurves = new Texture2D(k_CurvePrecision, 2, GetCurveFormat(), false, true)
                {
                    name = "Internal Curves Texture",
                    hideFlags = HideFlags.DontSave,
                    anisoLevel = 0,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            var curves = model.settings.curves;
            curves.hueVShue.Cache();
            curves.hueVSsat.Cache();

            for (int i = 0; i < k_CurvePrecision; i++)
            {
                float t = i * k_CurveStep;

                // HSL
                float x = curves.hueVShue.Evaluate(t);
                float y = curves.hueVSsat.Evaluate(t);
                float z = curves.satVSsat.Evaluate(t);
                float w = curves.lumVSsat.Evaluate(t);
                m_pixels[i] = new Color(x, y, z, w);

                // YRGB
                float m = curves.master.Evaluate(t);
                float r = curves.red.Evaluate(t);
                float g = curves.green.Evaluate(t);
                float b = curves.blue.Evaluate(t);
                m_pixels[i + k_CurvePrecision] = new Color(r, g, b, m);
            }

            m_GradingCurves.SetPixels(m_pixels);
            m_GradingCurves.Apply(false, false);

            return m_GradingCurves;
        }

        bool IsLogLutValid(RenderTexture lut)
        {
            return lut != null && lut.IsCreated() && lut.height == k_InternalLogLutSize;
        }

        RenderTextureFormat GetLutFormat()
        {
            if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                return RenderTextureFormat.ARGBHalf;

            return RenderTextureFormat.ARGB32;
        }

        void GenerateLut()
        {
            var settings = model.settings;

            if (!IsLogLutValid(model.bakedLut))
            {
                GraphicsUtils.Destroy(model.bakedLut);

                model.bakedLut = new RenderTexture(k_InternalLogLutSize * k_InternalLogLutSize, k_InternalLogLutSize, 0, GetLutFormat())
                {
                    name = "Color Grading Log LUT",
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };
            }

            var lutMaterial = context.materialFactory.Get("Hidden/Post FX/Lut Generator");
            lutMaterial.SetVector(Uniforms._LutParams, new Vector4(
                    k_InternalLogLutSize,
                    0.5f / (k_InternalLogLutSize * k_InternalLogLutSize),
                    0.5f / k_InternalLogLutSize,
                    k_InternalLogLutSize / (k_InternalLogLutSize - 1f))
                );

            // Tonemapping
            lutMaterial.shaderKeywords = null;

            var tonemapping = settings.tonemapping;
            switch (tonemapping.tonemapper)
            {
                case ColorGradingModel.Tonemapper.Neutral:
                    {
                        lutMaterial.EnableKeyword("TONEMAPPING_NEUTRAL");

                        const float scaleFactor = 20f;
                        const float scaleFactorHalf = scaleFactor * 0.5f;

                        float inBlack = tonemapping.neutralBlackIn * scaleFactor + 1f;
                        float outBlack = tonemapping.neutralBlackOut * scaleFactorHalf + 1f;
                        float inWhite = tonemapping.neutralWhiteIn / scaleFactor;
                        float outWhite = 1f - tonemapping.neutralWhiteOut / scaleFactor;
                        float blackRatio = inBlack / outBlack;
                        float whiteRatio = inWhite / outWhite;

                        const float a = 0.2f;
                        float b = Mathf.Max(0f, Mathf.LerpUnclamped(0.57f, 0.37f, blackRatio));
                        float c = Mathf.LerpUnclamped(0.01f, 0.24f, whiteRatio);
                        float d = Mathf.Max(0f, Mathf.LerpUnclamped(0.02f, 0.20f, blackRatio));
                        const float e = 0.02f;
                        const float f = 0.30f;

                        lutMaterial.SetVector(Uniforms._NeutralTonemapperParams1, new Vector4(a, b, c, d));
                        lutMaterial.SetVector(Uniforms._NeutralTonemapperParams2, new Vector4(e, f, tonemapping.neutralWhiteLevel, tonemapping.neutralWhiteClip / scaleFactorHalf));
                        break;
                    }

                case ColorGradingModel.Tonemapper.ACES:
                    {
                        lutMaterial.EnableKeyword("TONEMAPPING_FILMIC");
                        break;
                    }
            }

            // Color balance & basic grading settings
            lutMaterial.SetFloat(Uniforms._HueShift, settings.basic.hueShift / 360f);
            lutMaterial.SetFloat(Uniforms._Saturation, settings.basic.saturation);
            lutMaterial.SetFloat(Uniforms._Contrast, settings.basic.contrast);
            lutMaterial.SetVector(Uniforms._Balance, CalculateColorBalance(settings.basic.temperature, settings.basic.tint));

            // Lift / Gamma / Gain
            Vector3 lift, gamma, gain;
            CalculateLiftGammaGain(
                settings.colorWheels.linear.lift,
                settings.colorWheels.linear.gamma,
                settings.colorWheels.linear.gain,
                out lift, out gamma, out gain
                );

            lutMaterial.SetVector(Uniforms._Lift, lift);
            lutMaterial.SetVector(Uniforms._InvGamma, gamma);
            lutMaterial.SetVector(Uniforms._Gain, gain);

            // Slope / Power / Offset
            Vector3 slope, power, offset;
            CalculateSlopePowerOffset(
                settings.colorWheels.log.slope,
                settings.colorWheels.log.power,
                settings.colorWheels.log.offset,
                out slope, out power, out offset
                );

            lutMaterial.SetVector(Uniforms._Slope, slope);
            lutMaterial.SetVector(Uniforms._Power, power);
            lutMaterial.SetVector(Uniforms._Offset, offset);

            // Channel mixer
            lutMaterial.SetVector(Uniforms._ChannelMixerRed, settings.channelMixer.red);
            lutMaterial.SetVector(Uniforms._ChannelMixerGreen, settings.channelMixer.green);
            lutMaterial.SetVector(Uniforms._ChannelMixerBlue, settings.channelMixer.blue);

            // Selective grading & YRGB curves
            lutMaterial.SetTexture(Uniforms._Curves, GetCurveTexture());

            // Generate the lut
            KS3PUtil.Blit(null, model.bakedLut, lutMaterial, 0);
        }

        public override void Prepare(Material uberMaterial)
        {
            if (model.isDirty || !IsLogLutValid(model.bakedLut))
            {
                GenerateLut();
                model.isDirty = false;
            }

            uberMaterial.EnableKeyword(
                context.profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.PreGradingLog)
                ? "COLOR_GRADING_LOG_VIEW"
                : "COLOR_GRADING"
                );

            var bakedLut = model.bakedLut;
            uberMaterial.SetTexture(Uniforms._LogLut, bakedLut);
            uberMaterial.SetVector(Uniforms._LogLut_Params, new Vector3(1f / bakedLut.width, 1f / bakedLut.height, bakedLut.height - 1f));

            float ev = Mathf.Exp(model.settings.basic.postExposure * 0.69314718055994530941723212145818f);
            uberMaterial.SetFloat(Uniforms._ExposureEV, ev);
        }

        public void OnGUI()
        {
            var bakedLut = model.bakedLut;
            var rect = new Rect(context.viewport.x * Screen.width + 8f, 8f, bakedLut.width, bakedLut.height);
            GUI.DrawTexture(rect, bakedLut);
        }

        public override void OnDisable()
        {
            GraphicsUtils.Destroy(m_GradingCurves);
            GraphicsUtils.Destroy(model.bakedLut);
            m_GradingCurves = null;
            model.bakedLut = null;
        }
    }

    public sealed class DepthOfFieldComponent : PostProcessingComponentRenderTexture<DepthOfFieldModel>
    {
        static class Uniforms
        {
            internal static readonly int _DepthOfFieldTex = Shader.PropertyToID("_DepthOfFieldTex");
            internal static readonly int _DepthOfFieldCoCTex = Shader.PropertyToID("_DepthOfFieldCoCTex");
            internal static readonly int _Distance = Shader.PropertyToID("_Distance");
            internal static readonly int _LensCoeff = Shader.PropertyToID("_LensCoeff");
            internal static readonly int _MaxCoC = Shader.PropertyToID("_MaxCoC");
            internal static readonly int _RcpMaxCoC = Shader.PropertyToID("_RcpMaxCoC");
            internal static readonly int _RcpAspect = Shader.PropertyToID("_RcpAspect");
            internal static readonly int _MainTex = Shader.PropertyToID("_MainTex");
            internal static readonly int _CoCTex = Shader.PropertyToID("_CoCTex");
            internal static readonly int _TaaParams = Shader.PropertyToID("_TaaParams");
            internal static readonly int _DepthOfFieldParams = Shader.PropertyToID("_DepthOfFieldParams");
        }

        const string k_ShaderString = "Hidden/Post FX/Depth Of Field";

        public override bool active
        {
            get
            {
                return model.enabled
                       && !context.interrupted;
            }
        }

        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        RenderTexture m_CoCHistory;

        // Height of the 35mm full-frame format (36mm x 24mm)
        const float k_FilmHeight = 0.024f;

        float CalculateFocalLength()
        {
            var settings = model.settings;

            if (!settings.useCameraFov)
                return settings.focalLength / 1000f;

            float fov = context.camera.fieldOfView * Mathf.Deg2Rad;
            return 0.5f * k_FilmHeight / Mathf.Tan(0.5f * fov);
        }

        float CalculateMaxCoCRadius(int screenHeight)
        {
            // Estimate the allowable maximum radius of CoC from the kernel
            // size (the equation below was empirically derived).
            float radiusInPixels = (float)model.settings.kernelSize * 4f + 6f;

            // Applying a 5% limit to the CoC radius to keep the size of
            // TileMax/NeighborMax small enough.
            return Mathf.Min(0.05f, radiusInPixels / screenHeight);
        }

        bool CheckHistory(int width, int height)
        {
            return m_CoCHistory != null && m_CoCHistory.IsCreated() &&
                m_CoCHistory.width == width && m_CoCHistory.height == height;
        }

        RenderTextureFormat SelectFormat(RenderTextureFormat primary, RenderTextureFormat secondary)
        {
            if (SystemInfo.SupportsRenderTextureFormat(primary)) return primary;
            if (SystemInfo.SupportsRenderTextureFormat(secondary)) return secondary;
            return RenderTextureFormat.Default;
        }

        public void Prepare(RenderTexture source, Material uberMaterial, bool antialiasCoC, Vector2 taaJitter, float taaBlending)
        {
            var settings = model.settings;
            var colorFormat = RenderTextureFormat.DefaultHDR;
            var cocFormat = SelectFormat(RenderTextureFormat.R8, RenderTextureFormat.RHalf);

            // Avoid using R8 on OSX with Metal. #896121, https://goo.gl/MgKqu6
#if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX) && !UNITY_2017_1_OR_NEWER
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal)
                cocFormat = SelectFormat(RenderTextureFormat.RHalf, RenderTextureFormat.Default);
#endif

            // Material setup
            var f = CalculateFocalLength();
            var s1 = Mathf.Max(settings.focusDistance, f);
            var aspect = (float)source.width / source.height;
            var coeff = f * f / (settings.aperture * (s1 - f) * k_FilmHeight * 2);
            var maxCoC = CalculateMaxCoCRadius(source.height);

            var material = context.materialFactory.Get(k_ShaderString);
            material.SetFloat(Uniforms._Distance, s1);
            material.SetFloat(Uniforms._LensCoeff, coeff);
            material.SetFloat(Uniforms._MaxCoC, maxCoC);
            material.SetFloat(Uniforms._RcpMaxCoC, 1f / maxCoC);
            material.SetFloat(Uniforms._RcpAspect, 1f / aspect);

            // CoC calculation pass
            var rtCoC = context.renderTextureFactory.Get(context.width, context.height, 0, cocFormat, RenderTextureReadWrite.Linear);
            KS3PUtil.Blit(null, rtCoC, material, 0);

            if (antialiasCoC)
            {
                // CoC temporal filter pass
                material.SetTexture(Uniforms._CoCTex, rtCoC);

                var blend = CheckHistory(context.width, context.height) ? taaBlending : 0f;
                material.SetVector(Uniforms._TaaParams, new Vector3(taaJitter.x, taaJitter.y, blend));

                var rtFiltered = RenderTexture.GetTemporary(context.width, context.height, 0, cocFormat);
                KS3PUtil.Blit(m_CoCHistory, rtFiltered, material, 1);

                context.renderTextureFactory.Release(rtCoC);
                if (m_CoCHistory != null) RenderTexture.ReleaseTemporary(m_CoCHistory);

                m_CoCHistory = rtCoC = rtFiltered;
            }

            // Downsampling and prefiltering pass
            var rt1 = context.renderTextureFactory.Get(context.width / 2, context.height / 2, 0, colorFormat);
            material.SetTexture(Uniforms._CoCTex, rtCoC);
            KS3PUtil.Blit(source, rt1, material, 2);

            // Bokeh simulation pass
            var rt2 = context.renderTextureFactory.Get(context.width / 2, context.height / 2, 0, colorFormat);
            KS3PUtil.Blit(rt1, rt2, material, 3 + (int)settings.kernelSize);

            // Postfilter pass
            KS3PUtil.Blit(rt2, rt1, material, 7);

            // Give the results to the uber shader.
            uberMaterial.SetVector(Uniforms._DepthOfFieldParams, new Vector3(s1, coeff, maxCoC));

            if (context.profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.FocusPlane))
            {
                uberMaterial.EnableKeyword("DEPTH_OF_FIELD_COC_VIEW");
                context.Interrupt();
            }
            else
            {
                uberMaterial.SetTexture(Uniforms._DepthOfFieldTex, rt1);
                uberMaterial.SetTexture(Uniforms._DepthOfFieldCoCTex, rtCoC);
                uberMaterial.EnableKeyword("DEPTH_OF_FIELD");
            }

            context.renderTextureFactory.Release(rt2);
        }

        public override void OnDisable()
        {
            if (m_CoCHistory != null)
                RenderTexture.ReleaseTemporary(m_CoCHistory);

            m_CoCHistory = null;
        }
    }

    public sealed class DitheringComponent : PostProcessingComponentRenderTexture<DitheringModel>
    {
        static class Uniforms
        {
            internal static readonly int _DitheringTex = Shader.PropertyToID("_DitheringTex");
            internal static readonly int _DitheringCoords = Shader.PropertyToID("_DitheringCoords");
        }

        public override bool active
        {
            get
            {
                return model.enabled
                       && !context.interrupted;
            }
        }

        // Holds 64 64x64 Alpha8 textures (256kb total)
        Texture2D[] noiseTextures;
        int textureIndex = 0;

        const int k_TextureCount = 64;

        public override void OnDisable()
        {
            noiseTextures = null;
        }

        void LoadNoiseTextures()
        {
            noiseTextures = new Texture2D[k_TextureCount];

            GameDatabase database = GameDatabase.Instance;

            for (int i = 0; i < k_TextureCount; i++)
                //noiseTextures[i] = Resources.Load<Texture2D>("Bluenoise64/LDR_LLL1_" + i);
                noiseTextures[i] = database.GetTexture("KS3P/Textures/NoiseTextures/LDR_LLL1_" + i, false);
        }

        public override void Prepare(Material uberMaterial)
        {
            float rndOffsetX;
            float rndOffsetY;

#if POSTFX_DEBUG_STATIC_DITHERING
            textureIndex = 0;
            rndOffsetX = 0f;
            rndOffsetY = 0f;
#else
            if (++textureIndex >= k_TextureCount)
                textureIndex = 0;

            rndOffsetX = UnityEngine.Random.value;
            rndOffsetY = UnityEngine.Random.value;
#endif

            if (noiseTextures == null)
                LoadNoiseTextures();

            var noiseTex = noiseTextures[textureIndex];

            uberMaterial.EnableKeyword("DITHERING");
            uberMaterial.SetTexture(Uniforms._DitheringTex, noiseTex);
            uberMaterial.SetVector(Uniforms._DitheringCoords, new Vector4(
                (float)context.width / (float)noiseTex.width,
                (float)context.height / (float)noiseTex.height,
                rndOffsetX,
                rndOffsetY
            ));
        }
    }

    public sealed class EyeAdaptationComponent : PostProcessingComponentRenderTexture<EyeAdaptationModel>
    {
        static class Uniforms
        {
            internal static readonly int _Params = Shader.PropertyToID("_Params");
            internal static readonly int _Speed = Shader.PropertyToID("_Speed");
            internal static readonly int _ScaleOffsetRes = Shader.PropertyToID("_ScaleOffsetRes");
            internal static readonly int _ExposureCompensation = Shader.PropertyToID("_ExposureCompensation");
            internal static readonly int _AutoExposure = Shader.PropertyToID("_AutoExposure");
            internal static readonly int _DebugWidth = Shader.PropertyToID("_DebugWidth");
        }

        ComputeShader m_EyeCompute;
        ComputeBuffer m_HistogramBuffer;

        readonly RenderTexture[] m_AutoExposurePool = new RenderTexture[2];
        int m_AutoExposurePingPing;
        RenderTexture m_CurrentAutoExposure;

        RenderTexture m_DebugHistogram;

        static uint[] s_EmptyHistogramBuffer;

        bool m_FirstFrame = true;

        // Don't forget to update 'EyeAdaptation.cginc' if you change these values !
        const int k_HistogramBins = 64;
        const int k_HistogramThreadX = 16;
        const int k_HistogramThreadY = 16;

        public override bool active
        {
            get
            {
                return model.enabled
                       && SystemInfo.supportsComputeShaders
                       && !context.interrupted;
            }
        }

        public void ResetHistory()
        {
            m_FirstFrame = true;
        }

        public override void OnEnable()
        {
            m_FirstFrame = true;
        }

        public override void OnDisable()
        {
            foreach (var rt in m_AutoExposurePool)
                GraphicsUtils.Destroy(rt);

            if (m_HistogramBuffer != null)
                m_HistogramBuffer.Release();

            m_HistogramBuffer = null;

            if (m_DebugHistogram != null)
                m_DebugHistogram.Release();

            m_DebugHistogram = null;
        }

        Vector4 GetHistogramScaleOffsetRes()
        {
            var settings = model.settings;
            float diff = settings.logMax - settings.logMin;
            float scale = 1f / diff;
            float offset = -settings.logMin * scale;
            return new Vector4(scale, offset, Mathf.Floor(context.width / 2f), Mathf.Floor(context.height / 2f));
        }

        public Texture Prepare(RenderTexture source, Material uberMaterial)
        {
            var settings = model.settings;

            // Setup compute
            if (m_EyeCompute == null)
                // m_EyeCompute = Resources.Load<ComputeShader>("Shaders/EyeHistogram");
                m_EyeCompute = ShaderLoader.GetComputeShader("EyeHistogram");

            var material = context.materialFactory.Get("Hidden/Post FX/Eye Adaptation");
            material.shaderKeywords = null;

            if (m_HistogramBuffer == null)
                m_HistogramBuffer = new ComputeBuffer(k_HistogramBins, sizeof(uint));

            if (s_EmptyHistogramBuffer == null)
                s_EmptyHistogramBuffer = new uint[k_HistogramBins];

            // Downscale the framebuffer, we don't need an absolute precision for auto exposure and it
            // helps making it more stable
            var scaleOffsetRes = GetHistogramScaleOffsetRes();

            var rt = context.renderTextureFactory.Get((int)scaleOffsetRes.z, (int)scaleOffsetRes.w, 0, source.format);
            KS3PUtil.Blit(source, rt);

            if (m_AutoExposurePool[0] == null || !m_AutoExposurePool[0].IsCreated())
                m_AutoExposurePool[0] = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);

            if (m_AutoExposurePool[1] == null || !m_AutoExposurePool[1].IsCreated())
                m_AutoExposurePool[1] = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);

            // Clears the buffer on every frame as we use it to accumulate luminance values on each frame
            m_HistogramBuffer.SetData(s_EmptyHistogramBuffer);

            // Gets a log histogram
            int kernel = m_EyeCompute.FindKernel("KEyeHistogram");
            m_EyeCompute.SetBuffer(kernel, "_Histogram", m_HistogramBuffer);
            m_EyeCompute.SetTexture(kernel, "_Source", rt);
            m_EyeCompute.SetVector("_ScaleOffsetRes", scaleOffsetRes);
            m_EyeCompute.Dispatch(kernel, Mathf.CeilToInt(rt.width / (float)k_HistogramThreadX), Mathf.CeilToInt(rt.height / (float)k_HistogramThreadY), 1);

            // Cleanup
            context.renderTextureFactory.Release(rt);

            // Make sure filtering values are correct to avoid apocalyptic consequences
            const float minDelta = 1e-2f;
            settings.highPercent = Mathf.Clamp(settings.highPercent, 1f + minDelta, 99f);
            settings.lowPercent = Mathf.Clamp(settings.lowPercent, 1f, settings.highPercent - minDelta);

            // Compute auto exposure
            material.SetBuffer("_Histogram", m_HistogramBuffer); // No (int, buffer) overload for SetBuffer ?
            material.SetVector(Uniforms._Params, new Vector4(settings.lowPercent * 0.01f, settings.highPercent * 0.01f, Mathf.Exp(settings.minLuminance * 0.69314718055994530941723212145818f), Mathf.Exp(settings.maxLuminance * 0.69314718055994530941723212145818f)));
            material.SetVector(Uniforms._Speed, new Vector2(settings.speedDown, settings.speedUp));
            material.SetVector(Uniforms._ScaleOffsetRes, scaleOffsetRes);
            material.SetFloat(Uniforms._ExposureCompensation, settings.keyValue);

            if (settings.dynamicKeyValue)
                material.EnableKeyword("AUTO_KEY_VALUE");

            if (m_FirstFrame || !Application.isPlaying)
            {
                // We don't want eye adaptation when not in play mode because the GameView isn't
                // animated, thus making it harder to tweak. Just use the final audo exposure value.
                m_CurrentAutoExposure = m_AutoExposurePool[0];
                KS3PUtil.Blit(null, m_CurrentAutoExposure, material, (int)EyeAdaptationModel.EyeAdaptationType.Fixed);

                // Copy current exposure to the other pingpong target to avoid adapting from black
                KS3PUtil.Blit(m_AutoExposurePool[0], m_AutoExposurePool[1]);
            }
            else
            {
                int pp = m_AutoExposurePingPing;
                var src = m_AutoExposurePool[++pp % 2];
                var dst = m_AutoExposurePool[++pp % 2];
                KS3PUtil.Blit(src, dst, material, (int)settings.adaptationType);
                m_AutoExposurePingPing = ++pp % 2;
                m_CurrentAutoExposure = dst;
            }

            // Generate debug histogram
            if (context.profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.EyeAdaptation))
            {
                if (m_DebugHistogram == null || !m_DebugHistogram.IsCreated())
                {
                    m_DebugHistogram = new RenderTexture(256, 128, 0, RenderTextureFormat.ARGB32)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    };
                }

                material.SetFloat(Uniforms._DebugWidth, m_DebugHistogram.width);
                KS3PUtil.Blit(null, m_DebugHistogram, material, 2);
            }

            m_FirstFrame = false;
            return m_CurrentAutoExposure;
        }

        public void OnGUI()
        {
            if (m_DebugHistogram == null || !m_DebugHistogram.IsCreated())
                return;

            var rect = new Rect(context.viewport.x * Screen.width + 8f, 8f, m_DebugHistogram.width, m_DebugHistogram.height);
            GUI.DrawTexture(rect, m_DebugHistogram);
        }
    }

    public sealed class FogComponent : PostProcessingComponentCommandBuffer<FogModel>
    {
        static class Uniforms
        {
            internal static readonly int _FogColor = Shader.PropertyToID("_FogColor");
            internal static readonly int _Density = Shader.PropertyToID("_Density");
            internal static readonly int _Start = Shader.PropertyToID("_Start");
            internal static readonly int _End = Shader.PropertyToID("_End");
            internal static readonly int _TempRT = Shader.PropertyToID("_TempRT");
        }

        const string k_ShaderString = "Hidden/Post FX/Fog";

        public override bool active
        {
            get
            {
                return model.enabled
                       && context.isGBufferAvailable // In forward fog is already done at shader level
                       && RenderSettings.fog
                       && !context.interrupted;
            }
        }

        public override string GetName()
        {
            return "Fog";
        }

        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override CameraEvent GetCameraEvent()
        {
            return CameraEvent.AfterImageEffectsOpaque;
        }

        public override void PopulateCommandBuffer(CommandBuffer cb)
        {
            var settings = model.settings;

            var material = context.materialFactory.Get(k_ShaderString);
            material.shaderKeywords = null;
            var fogColor = GraphicsUtils.isLinearColorSpace ? RenderSettings.fogColor.linear : RenderSettings.fogColor;
            material.SetColor(Uniforms._FogColor, fogColor);
            material.SetFloat(Uniforms._Density, RenderSettings.fogDensity);
            material.SetFloat(Uniforms._Start, RenderSettings.fogStartDistance);
            material.SetFloat(Uniforms._End, RenderSettings.fogEndDistance);

            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear:
                    material.EnableKeyword("FOG_LINEAR");
                    break;
                case FogMode.Exponential:
                    material.EnableKeyword("FOG_EXP");
                    break;
                case FogMode.ExponentialSquared:
                    material.EnableKeyword("FOG_EXP2");
                    break;
            }

            var fbFormat = context.isHdr
                ? RenderTextureFormat.DefaultHDR
                : RenderTextureFormat.Default;

            cb.GetTemporaryRT(Uniforms._TempRT, context.width, context.height, 24, FilterMode.Bilinear, fbFormat);
            cb.Blit(BuiltinRenderTextureType.CameraTarget, Uniforms._TempRT);
            cb.Blit(Uniforms._TempRT, BuiltinRenderTextureType.CameraTarget, material, settings.excludeSkybox ? 1 : 0);
            cb.ReleaseTemporaryRT(Uniforms._TempRT);
        }
    }

    public sealed class FxaaComponent : PostProcessingComponentRenderTexture<AntialiasingModel>
    {
        static class Uniforms
        {
            internal static readonly int _QualitySettings = Shader.PropertyToID("_QualitySettings");
            internal static readonly int _ConsoleSettings = Shader.PropertyToID("_ConsoleSettings");
        }

        public override bool active
        {
            get
            {
                return model.enabled
                       && model.settings.method == AntialiasingModel.Method.Fxaa
                       && !context.interrupted;
            }
        }

        public void Render(RenderTexture source, RenderTexture destination)
        {
            var settings = model.settings.fxaaSettings;
            var material = context.materialFactory.Get("Hidden/Post FX/FXAA");
            var qualitySettings = AntialiasingModel.FxaaQualitySettings.presets[(int)settings.preset];
            var consoleSettings = AntialiasingModel.FxaaConsoleSettings.presets[(int)settings.preset];

            material.SetVector(Uniforms._QualitySettings,
                new Vector3(
                    qualitySettings.subpixelAliasingRemovalAmount,
                    qualitySettings.edgeDetectionThreshold,
                    qualitySettings.minimumRequiredLuminance
                    )
                );

            material.SetVector(Uniforms._ConsoleSettings,
                new Vector4(
                    consoleSettings.subpixelSpreadAmount,
                    consoleSettings.edgeSharpnessAmount,
                    consoleSettings.edgeDetectionThreshold,
                    consoleSettings.minimumRequiredLuminance
                    )
                );

            KS3PUtil.Blit(source, destination, material, 0);
        }
    }

    public sealed class GrainComponent : PostProcessingComponentRenderTexture<GrainModel>
    {
        static class Uniforms
        {
            internal static readonly int _Grain_Params1 = Shader.PropertyToID("_Grain_Params1");
            internal static readonly int _Grain_Params2 = Shader.PropertyToID("_Grain_Params2");
            internal static readonly int _GrainTex = Shader.PropertyToID("_GrainTex");
            internal static readonly int _Phase = Shader.PropertyToID("_Phase");
        }

        public override bool active
        {
            get
            {
                return model.enabled
                       && model.settings.intensity > 0f
                       && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                       && !context.interrupted;
            }
        }

        RenderTexture m_GrainLookupRT;

        public override void OnDisable()
        {
            GraphicsUtils.Destroy(m_GrainLookupRT);
            m_GrainLookupRT = null;
        }

        public override void Prepare(Material uberMaterial)
        {
            var settings = model.settings;

            uberMaterial.EnableKeyword("GRAIN");

            float rndOffsetX;
            float rndOffsetY;

#if POSTFX_DEBUG_STATIC_GRAIN
            // Chosen by a fair dice roll
            float time = 4f;
            rndOffsetX = 0f;
            rndOffsetY = 0f;
#else
            float time = Time.realtimeSinceStartup;
            rndOffsetX = UnityEngine.Random.value;
            rndOffsetY = UnityEngine.Random.value;
#endif

            // Generate the grain lut for the current frame first
            if (m_GrainLookupRT == null || !m_GrainLookupRT.IsCreated())
            {
                GraphicsUtils.Destroy(m_GrainLookupRT);

                m_GrainLookupRT = new RenderTexture(192, 192, 0, RenderTextureFormat.ARGBHalf)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 0,
                    name = "Grain Lookup Texture"
                };

                m_GrainLookupRT.Create();
            }

            var grainMaterial = context.materialFactory.Get("Hidden/Post FX/Grain Generator");
            grainMaterial.SetFloat(Uniforms._Phase, time / 20f);

            KS3PUtil.Blit((Texture)null, m_GrainLookupRT, grainMaterial, settings.colored ? 1 : 0);

            // Send everything to the uber shader
            uberMaterial.SetTexture(Uniforms._GrainTex, m_GrainLookupRT);
            uberMaterial.SetVector(Uniforms._Grain_Params1, new Vector2(settings.luminanceContribution, settings.intensity * 20f));
            uberMaterial.SetVector(Uniforms._Grain_Params2, new Vector4((float)context.width / (float)m_GrainLookupRT.width / settings.size, (float)context.height / (float)m_GrainLookupRT.height / settings.size, rndOffsetX, rndOffsetY));
        }
    }

    public sealed class MotionBlurComponent : PostProcessingComponentCommandBuffer<MotionBlurModel>
    {
        static class Uniforms
        {
            internal static readonly int _VelocityScale = Shader.PropertyToID("_VelocityScale");
            internal static readonly int _MaxBlurRadius = Shader.PropertyToID("_MaxBlurRadius");
            internal static readonly int _RcpMaxBlurRadius = Shader.PropertyToID("_RcpMaxBlurRadius");
            internal static readonly int _VelocityTex = Shader.PropertyToID("_VelocityTex");
            internal static readonly int _MainTex = Shader.PropertyToID("_MainTex");
            internal static readonly int _Tile2RT = Shader.PropertyToID("_Tile2RT");
            internal static readonly int _Tile4RT = Shader.PropertyToID("_Tile4RT");
            internal static readonly int _Tile8RT = Shader.PropertyToID("_Tile8RT");
            internal static readonly int _TileMaxOffs = Shader.PropertyToID("_TileMaxOffs");
            internal static readonly int _TileMaxLoop = Shader.PropertyToID("_TileMaxLoop");
            internal static readonly int _TileVRT = Shader.PropertyToID("_TileVRT");
            internal static readonly int _NeighborMaxTex = Shader.PropertyToID("_NeighborMaxTex");
            internal static readonly int _LoopCount = Shader.PropertyToID("_LoopCount");
            internal static readonly int _TempRT = Shader.PropertyToID("_TempRT");

            internal static readonly int _History1LumaTex = Shader.PropertyToID("_History1LumaTex");
            internal static readonly int _History2LumaTex = Shader.PropertyToID("_History2LumaTex");
            internal static readonly int _History3LumaTex = Shader.PropertyToID("_History3LumaTex");
            internal static readonly int _History4LumaTex = Shader.PropertyToID("_History4LumaTex");

            internal static readonly int _History1ChromaTex = Shader.PropertyToID("_History1ChromaTex");
            internal static readonly int _History2ChromaTex = Shader.PropertyToID("_History2ChromaTex");
            internal static readonly int _History3ChromaTex = Shader.PropertyToID("_History3ChromaTex");
            internal static readonly int _History4ChromaTex = Shader.PropertyToID("_History4ChromaTex");

            internal static readonly int _History1Weight = Shader.PropertyToID("_History1Weight");
            internal static readonly int _History2Weight = Shader.PropertyToID("_History2Weight");
            internal static readonly int _History3Weight = Shader.PropertyToID("_History3Weight");
            internal static readonly int _History4Weight = Shader.PropertyToID("_History4Weight");
        }

        enum Pass
        {
            VelocitySetup,
            TileMax1,
            TileMax2,
            TileMaxV,
            NeighborMax,
            Reconstruction,
            FrameCompression,
            FrameBlendingChroma,
            FrameBlendingRaw
        }

        public class ReconstructionFilter
        {
            // Texture format for storing 2D vectors.
            RenderTextureFormat m_VectorRTFormat = RenderTextureFormat.RGHalf;

            // Texture format for storing packed velocity/depth.
            RenderTextureFormat m_PackedRTFormat = RenderTextureFormat.ARGB2101010;

            public ReconstructionFilter()
            {
                CheckTextureFormatSupport();
            }

            void CheckTextureFormatSupport()
            {
                // If 2:10:10:10 isn't supported, use ARGB32 instead.
                if (!SystemInfo.SupportsRenderTextureFormat(m_PackedRTFormat))
                    m_PackedRTFormat = RenderTextureFormat.ARGB32;
            }

            public bool IsSupported()
            {
                return SystemInfo.supportsMotionVectors;
            }

            public void ProcessImage(PostProcessingContext context, CommandBuffer cb, ref MotionBlurModel.Settings settings, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material)
            {
                const float kMaxBlurRadius = 5f;

                // Calculate the maximum blur radius in pixels.
                int maxBlurPixels = (int)(kMaxBlurRadius * context.height / 100);

                // Calculate the TileMax size.
                // It should be a multiple of 8 and larger than maxBlur.
                int tileSize = ((maxBlurPixels - 1) / 8 + 1) * 8;

                // Pass 1 - Velocity/depth packing
                var velocityScale = settings.shutterAngle / 360f;
                cb.SetGlobalFloat(Uniforms._VelocityScale, velocityScale);
                cb.SetGlobalFloat(Uniforms._MaxBlurRadius, maxBlurPixels);
                cb.SetGlobalFloat(Uniforms._RcpMaxBlurRadius, 1f / maxBlurPixels);

                int vbuffer = Uniforms._VelocityTex;
                cb.GetTemporaryRT(vbuffer, context.width, context.height, 0, FilterMode.Point, m_PackedRTFormat, RenderTextureReadWrite.Linear);
                cb.Blit((Texture)null, vbuffer, material, (int)Pass.VelocitySetup);

                // Pass 2 - First TileMax filter (1/2 downsize)
                int tile2 = Uniforms._Tile2RT;
                cb.GetTemporaryRT(tile2, context.width / 2, context.height / 2, 0, FilterMode.Point, m_VectorRTFormat, RenderTextureReadWrite.Linear);
                cb.SetGlobalTexture(Uniforms._MainTex, vbuffer);
                cb.Blit(vbuffer, tile2, material, (int)Pass.TileMax1);

                // Pass 3 - Second TileMax filter (1/2 downsize)
                int tile4 = Uniforms._Tile4RT;
                cb.GetTemporaryRT(tile4, context.width / 4, context.height / 4, 0, FilterMode.Point, m_VectorRTFormat, RenderTextureReadWrite.Linear);
                cb.SetGlobalTexture(Uniforms._MainTex, tile2);
                cb.Blit(tile2, tile4, material, (int)Pass.TileMax2);
                cb.ReleaseTemporaryRT(tile2);

                // Pass 4 - Third TileMax filter (1/2 downsize)
                int tile8 = Uniforms._Tile8RT;
                cb.GetTemporaryRT(tile8, context.width / 8, context.height / 8, 0, FilterMode.Point, m_VectorRTFormat, RenderTextureReadWrite.Linear);
                cb.SetGlobalTexture(Uniforms._MainTex, tile4);
                cb.Blit(tile4, tile8, material, (int)Pass.TileMax2);
                cb.ReleaseTemporaryRT(tile4);

                // Pass 5 - Fourth TileMax filter (reduce to tileSize)
                var tileMaxOffs = Vector2.one * (tileSize / 8f - 1f) * -0.5f;
                cb.SetGlobalVector(Uniforms._TileMaxOffs, tileMaxOffs);
                cb.SetGlobalFloat(Uniforms._TileMaxLoop, (int)(tileSize / 8f));

                int tile = Uniforms._TileVRT;
                cb.GetTemporaryRT(tile, context.width / tileSize, context.height / tileSize, 0, FilterMode.Point, m_VectorRTFormat, RenderTextureReadWrite.Linear);
                cb.SetGlobalTexture(Uniforms._MainTex, tile8);
                cb.Blit(tile8, tile, material, (int)Pass.TileMaxV);
                cb.ReleaseTemporaryRT(tile8);

                // Pass 6 - NeighborMax filter
                int neighborMax = Uniforms._NeighborMaxTex;
                int neighborMaxWidth = context.width / tileSize;
                int neighborMaxHeight = context.height / tileSize;
                cb.GetTemporaryRT(neighborMax, neighborMaxWidth, neighborMaxHeight, 0, FilterMode.Point, m_VectorRTFormat, RenderTextureReadWrite.Linear);
                cb.SetGlobalTexture(Uniforms._MainTex, tile);
                cb.Blit(tile, neighborMax, material, (int)Pass.NeighborMax);
                cb.ReleaseTemporaryRT(tile);

                // Pass 7 - Reconstruction pass
                cb.SetGlobalFloat(Uniforms._LoopCount, Mathf.Clamp(settings.sampleCount / 2, 1, 64));
                cb.SetGlobalTexture(Uniforms._MainTex, source);

                cb.Blit(source, destination, material, (int)Pass.Reconstruction);

                cb.ReleaseTemporaryRT(vbuffer);
                cb.ReleaseTemporaryRT(neighborMax);
            }
        }

        public class FrameBlendingFilter
        {
            struct Frame
            {
                public RenderTexture lumaTexture;
                public RenderTexture chromaTexture;

                float m_Time;
                RenderTargetIdentifier[] m_MRT;

                public float CalculateWeight(float strength, float currentTime)
                {
                    if (Mathf.Approximately(m_Time, 0f))
                        return 0f;

                    var coeff = Mathf.Lerp(80f, 16f, strength);
                    return Mathf.Exp((m_Time - currentTime) * coeff);
                }

                public void Release()
                {
                    if (lumaTexture != null)
                        RenderTexture.ReleaseTemporary(lumaTexture);

                    if (chromaTexture != null)
                        RenderTexture.ReleaseTemporary(chromaTexture);

                    lumaTexture = null;
                    chromaTexture = null;
                }

                public void MakeRecord(CommandBuffer cb, RenderTargetIdentifier source, int width, int height, Material material)
                {
                    Release();

                    lumaTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                    chromaTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);

                    lumaTexture.filterMode = FilterMode.Point;
                    chromaTexture.filterMode = FilterMode.Point;

                    if (m_MRT == null)
                        m_MRT = new RenderTargetIdentifier[2];

                    m_MRT[0] = lumaTexture;
                    m_MRT[1] = chromaTexture;

                    cb.SetGlobalTexture(Uniforms._MainTex, source);
                    cb.SetRenderTarget(m_MRT, lumaTexture);
                    cb.DrawMesh(GraphicsUtils.quad, Matrix4x4.identity, material, 0, (int)Pass.FrameCompression);

                    m_Time = Time.time;
                }

                public void MakeRecordRaw(CommandBuffer cb, RenderTargetIdentifier source, int width, int height, RenderTextureFormat format)
                {
                    Release();

                    lumaTexture = RenderTexture.GetTemporary(width, height, 0, format);
                    lumaTexture.filterMode = FilterMode.Point;

                    cb.SetGlobalTexture(Uniforms._MainTex, source);
                    cb.Blit(source, lumaTexture);

                    m_Time = Time.time;
                }
            }

            bool m_UseCompression;
            RenderTextureFormat m_RawTextureFormat;

            Frame[] m_FrameList;
            int m_LastFrameCount;

            public FrameBlendingFilter()
            {
                m_UseCompression = CheckSupportCompression();
                m_RawTextureFormat = GetPreferredRenderTextureFormat();
                m_FrameList = new Frame[4];
            }

            public void Dispose()
            {
                foreach (var frame in m_FrameList)
                    frame.Release();
            }

            public void PushFrame(CommandBuffer cb, RenderTargetIdentifier source, int width, int height, Material material)
            {
                // Push only when actual update (do nothing while pausing)
                var frameCount = Time.frameCount;
                if (frameCount == m_LastFrameCount) return;

                // Update the frame record.
                var index = frameCount % m_FrameList.Length;

                if (m_UseCompression)
                    m_FrameList[index].MakeRecord(cb, source, width, height, material);
                else
                    m_FrameList[index].MakeRecordRaw(cb, source, width, height, m_RawTextureFormat);

                m_LastFrameCount = frameCount;
            }

            public void BlendFrames(CommandBuffer cb, float strength, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material)
            {
                var t = Time.time;

                var f1 = GetFrameRelative(-1);
                var f2 = GetFrameRelative(-2);
                var f3 = GetFrameRelative(-3);
                var f4 = GetFrameRelative(-4);

                cb.SetGlobalTexture(Uniforms._History1LumaTex, f1.lumaTexture);
                cb.SetGlobalTexture(Uniforms._History2LumaTex, f2.lumaTexture);
                cb.SetGlobalTexture(Uniforms._History3LumaTex, f3.lumaTexture);
                cb.SetGlobalTexture(Uniforms._History4LumaTex, f4.lumaTexture);

                cb.SetGlobalTexture(Uniforms._History1ChromaTex, f1.chromaTexture);
                cb.SetGlobalTexture(Uniforms._History2ChromaTex, f2.chromaTexture);
                cb.SetGlobalTexture(Uniforms._History3ChromaTex, f3.chromaTexture);
                cb.SetGlobalTexture(Uniforms._History4ChromaTex, f4.chromaTexture);

                cb.SetGlobalFloat(Uniforms._History1Weight, f1.CalculateWeight(strength, t));
                cb.SetGlobalFloat(Uniforms._History2Weight, f2.CalculateWeight(strength, t));
                cb.SetGlobalFloat(Uniforms._History3Weight, f3.CalculateWeight(strength, t));
                cb.SetGlobalFloat(Uniforms._History4Weight, f4.CalculateWeight(strength, t));

                cb.SetGlobalTexture(Uniforms._MainTex, source);
                cb.Blit(source, destination, material, m_UseCompression ? (int)Pass.FrameBlendingChroma : (int)Pass.FrameBlendingRaw);
            }

            // Check if the platform has the capability of compression.
            static bool CheckSupportCompression()
            {
                return
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) &&
                    SystemInfo.supportedRenderTargetCount > 1;
            }

            // Determine which 16-bit render texture format is available.
            static RenderTextureFormat GetPreferredRenderTextureFormat()
            {
                RenderTextureFormat[] formats =
                {
                    RenderTextureFormat.RGB565,
                    RenderTextureFormat.ARGB1555,
                    RenderTextureFormat.ARGB4444
                };

                foreach (var f in formats)
                    if (SystemInfo.SupportsRenderTextureFormat(f)) return f;

                return RenderTextureFormat.Default;
            }

            // Retrieve a frame record with relative indexing.
            // Use a negative index to refer to previous frames.
            Frame GetFrameRelative(int offset)
            {
                var index = (Time.frameCount + m_FrameList.Length + offset) % m_FrameList.Length;
                return m_FrameList[index];
            }
        }

        ReconstructionFilter m_ReconstructionFilter;
        public ReconstructionFilter reconstructionFilter
        {
            get
            {
                if (m_ReconstructionFilter == null)
                    m_ReconstructionFilter = new ReconstructionFilter();

                return m_ReconstructionFilter;
            }
        }

        FrameBlendingFilter m_FrameBlendingFilter;
        public FrameBlendingFilter frameBlendingFilter
        {
            get
            {
                if (m_FrameBlendingFilter == null)
                    m_FrameBlendingFilter = new FrameBlendingFilter();

                return m_FrameBlendingFilter;
            }
        }

        bool m_FirstFrame = true;

        public override bool active
        {
            get
            {
                var settings = model.settings;
                return model.enabled
                       && ((settings.shutterAngle > 0f && reconstructionFilter.IsSupported()) || settings.frameBlending > 0f)
                       && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2 // No movecs on GLES2 platforms
                       && !context.interrupted;
            }
        }

        public override string GetName()
        {
            return "Motion Blur";
        }

        public void ResetHistory()
        {
            if (m_FrameBlendingFilter != null)
                m_FrameBlendingFilter.Dispose();

            m_FrameBlendingFilter = null;
        }

        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        }

        public override CameraEvent GetCameraEvent()
        {
            return CameraEvent.BeforeImageEffects;
        }

        public override void OnEnable()
        {
            m_FirstFrame = true;
        }

        public override void PopulateCommandBuffer(CommandBuffer cb)
        {
#if UNITY_EDITOR
            // Don't render motion blur preview when the editor is not playing as it can in some
            // cases results in ugly artifacts (i.e. when resizing the game view).
            if (!Application.isPlaying)
                return;
#endif

            // Skip rendering in the first frame as motion vectors won't be abvailable until the
            // next one
            if (m_FirstFrame)
            {
                m_FirstFrame = false;
                return;
            }

            var material = context.materialFactory.Get("Hidden/Post FX/Motion Blur");
            var blitMaterial = context.materialFactory.Get("Hidden/Post FX/Blit");
            var settings = model.settings;

            var fbFormat = context.isHdr
                ? RenderTextureFormat.DefaultHDR
                : RenderTextureFormat.Default;

            int tempRT = Uniforms._TempRT;
            cb.GetTemporaryRT(tempRT, context.width, context.height, 0, FilterMode.Point, fbFormat);

            if (settings.shutterAngle > 0f && settings.frameBlending > 0f)
            {
                // Motion blur + frame blending
                reconstructionFilter.ProcessImage(context, cb, ref settings, BuiltinRenderTextureType.CameraTarget, tempRT, material);
                frameBlendingFilter.BlendFrames(cb, settings.frameBlending, tempRT, BuiltinRenderTextureType.CameraTarget, material);
                frameBlendingFilter.PushFrame(cb, tempRT, context.width, context.height, material);
            }
            else if (settings.shutterAngle > 0f)
            {
                // No frame blending
                cb.SetGlobalTexture(Uniforms._MainTex, BuiltinRenderTextureType.CameraTarget);
                cb.Blit(BuiltinRenderTextureType.CameraTarget, tempRT, blitMaterial, 0);
                reconstructionFilter.ProcessImage(context, cb, ref settings, tempRT, BuiltinRenderTextureType.CameraTarget, material);
            }
            else if (settings.frameBlending > 0f)
            {
                // Frame blending only
                cb.SetGlobalTexture(Uniforms._MainTex, BuiltinRenderTextureType.CameraTarget);
                cb.Blit(BuiltinRenderTextureType.CameraTarget, tempRT, blitMaterial, 0);
                frameBlendingFilter.BlendFrames(cb, settings.frameBlending, tempRT, BuiltinRenderTextureType.CameraTarget, material);
                frameBlendingFilter.PushFrame(cb, tempRT, context.width, context.height, material);
            }

            // Cleaning up
            cb.ReleaseTemporaryRT(tempRT);
        }

        public override void OnDisable()
        {
            if (m_FrameBlendingFilter != null)
                m_FrameBlendingFilter.Dispose();
        }
    }

    public sealed class TaaComponent : PostProcessingComponentRenderTexture<AntialiasingModel>
    {
        static class Uniforms
        {
            internal static int _Jitter = Shader.PropertyToID("_Jitter");
            internal static int _SharpenParameters = Shader.PropertyToID("_SharpenParameters");
            internal static int _FinalBlendParameters = Shader.PropertyToID("_FinalBlendParameters");
            internal static int _HistoryTex = Shader.PropertyToID("_HistoryTex");
            internal static int _MainTex = Shader.PropertyToID("_MainTex");
        }

        const string k_ShaderString = "Hidden/Post FX/Temporal Anti-aliasing";
        const int k_SampleCount = 8;

        readonly RenderBuffer[] m_MRT = new RenderBuffer[2];

        int m_SampleIndex = 0;
        bool m_ResetHistory = true;

        RenderTexture m_HistoryTexture;

        public override bool active
        {
            get
            {
                return model.enabled
                       && model.settings.method == AntialiasingModel.Method.Taa
                       && SystemInfo.supportsMotionVectors
                       && SystemInfo.supportedRenderTargetCount >= 2
                       && !context.interrupted;
            }
        }

        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        }

        public Vector2 jitterVector { get; private set; }

        public void ResetHistory()
        {
            m_ResetHistory = true;
        }

        public void SetProjectionMatrix(Func<Vector2, Matrix4x4> jitteredFunc)
        {
            var settings = model.settings.taaSettings;

            var jitter = GenerateRandomOffset();
            jitter *= settings.jitterSpread;

            context.camera.nonJitteredProjectionMatrix = context.camera.projectionMatrix;

            if (jitteredFunc != null)
            {
                context.camera.projectionMatrix = jitteredFunc(jitter);
            }
            else
            {
                context.camera.projectionMatrix = context.camera.orthographic
                    ? GetOrthographicProjectionMatrix(jitter)
                    : GetPerspectiveProjectionMatrix(jitter);
            }

#if UNITY_5_5_OR_NEWER
            context.camera.useJitteredProjectionMatrixForTransparentRendering = false;
#endif

            jitter.x /= context.width;
            jitter.y /= context.height;

            var material = context.materialFactory.Get(k_ShaderString);
            material.SetVector(Uniforms._Jitter, jitter);

            jitterVector = jitter;
        }

        public void Render(RenderTexture source, RenderTexture destination)
        {
            var material = context.materialFactory.Get(k_ShaderString);
            material.shaderKeywords = null;

            var settings = model.settings.taaSettings;

            if (m_ResetHistory || m_HistoryTexture == null || m_HistoryTexture.width != source.width || m_HistoryTexture.height != source.height)
            {
                if (m_HistoryTexture)
                    RenderTexture.ReleaseTemporary(m_HistoryTexture);

                m_HistoryTexture = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                m_HistoryTexture.name = "TAA History";

                KS3PUtil.Blit(source, m_HistoryTexture, material, 2);
            }

            const float kMotionAmplification = 100f * 60f;
            material.SetVector(Uniforms._SharpenParameters, new Vector4(settings.sharpen, 0f, 0f, 0f));
            material.SetVector(Uniforms._FinalBlendParameters, new Vector4(settings.stationaryBlending, settings.motionBlending, kMotionAmplification, 0f));
            material.SetTexture(Uniforms._MainTex, source);
            material.SetTexture(Uniforms._HistoryTex, m_HistoryTexture);

            var tempHistory = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
            tempHistory.name = "TAA History";

            m_MRT[0] = destination.colorBuffer;
            m_MRT[1] = tempHistory.colorBuffer;

            Graphics.SetRenderTarget(m_MRT, source.depthBuffer);
            GraphicsUtils.Blit(material, context.camera.orthographic ? 1 : 0);

            RenderTexture.ReleaseTemporary(m_HistoryTexture);
            m_HistoryTexture = tempHistory;

            m_ResetHistory = false;
        }

        float GetHaltonValue(int index, int radix)
        {
            float result = 0f;
            float fraction = 1f / (float)radix;

            while (index > 0)
            {
                result += (float)(index % radix) * fraction;

                index /= radix;
                fraction /= (float)radix;
            }

            return result;
        }

        Vector2 GenerateRandomOffset()
        {
            var offset = new Vector2(
                    GetHaltonValue(m_SampleIndex & 1023, 2),
                    GetHaltonValue(m_SampleIndex & 1023, 3));

            if (++m_SampleIndex >= k_SampleCount)
                m_SampleIndex = 0;

            return offset;
        }

        // Adapted heavily from PlayDead's TAA code
        // https://github.com/playdeadgames/temporal/blob/master/Assets/Scripts/Extensions.cs
        Matrix4x4 GetPerspectiveProjectionMatrix(Vector2 offset)
        {
            float vertical = Mathf.Tan(0.5f * Mathf.Deg2Rad * context.camera.fieldOfView);
            float horizontal = vertical * context.camera.aspect;

            offset.x *= horizontal / (0.5f * context.width);
            offset.y *= vertical / (0.5f * context.height);

            float left = (offset.x - horizontal) * context.camera.nearClipPlane;
            float right = (offset.x + horizontal) * context.camera.nearClipPlane;
            float top = (offset.y + vertical) * context.camera.nearClipPlane;
            float bottom = (offset.y - vertical) * context.camera.nearClipPlane;

            var matrix = new Matrix4x4();

            matrix[0, 0] = (2f * context.camera.nearClipPlane) / (right - left);
            matrix[0, 1] = 0f;
            matrix[0, 2] = (right + left) / (right - left);
            matrix[0, 3] = 0f;

            matrix[1, 0] = 0f;
            matrix[1, 1] = (2f * context.camera.nearClipPlane) / (top - bottom);
            matrix[1, 2] = (top + bottom) / (top - bottom);
            matrix[1, 3] = 0f;

            matrix[2, 0] = 0f;
            matrix[2, 1] = 0f;
            matrix[2, 2] = -(context.camera.farClipPlane + context.camera.nearClipPlane) / (context.camera.farClipPlane - context.camera.nearClipPlane);
            matrix[2, 3] = -(2f * context.camera.farClipPlane * context.camera.nearClipPlane) / (context.camera.farClipPlane - context.camera.nearClipPlane);

            matrix[3, 0] = 0f;
            matrix[3, 1] = 0f;
            matrix[3, 2] = -1f;
            matrix[3, 3] = 0f;

            return matrix;
        }

        Matrix4x4 GetOrthographicProjectionMatrix(Vector2 offset)
        {
            float vertical = context.camera.orthographicSize;
            float horizontal = vertical * context.camera.aspect;

            offset.x *= horizontal / (0.5f * context.width);
            offset.y *= vertical / (0.5f * context.height);

            float left = offset.x - horizontal;
            float right = offset.x + horizontal;
            float top = offset.y + vertical;
            float bottom = offset.y - vertical;

            return Matrix4x4.Ortho(left, right, bottom, top, context.camera.nearClipPlane, context.camera.farClipPlane);
        }

        public override void OnDisable()
        {
            if (m_HistoryTexture != null)
                RenderTexture.ReleaseTemporary(m_HistoryTexture);

            m_HistoryTexture = null;
            m_SampleIndex = 0;
            ResetHistory();
        }
    }

    public sealed class ScreenSpaceReflectionComponent : PostProcessingComponentCommandBuffer<ScreenSpaceReflectionModel>
    {
        static class Uniforms
        {
            internal static readonly int _RayStepSize = Shader.PropertyToID("_RayStepSize");
            internal static readonly int _AdditiveReflection = Shader.PropertyToID("_AdditiveReflection");
            internal static readonly int _BilateralUpsampling = Shader.PropertyToID("_BilateralUpsampling");
            internal static readonly int _TreatBackfaceHitAsMiss = Shader.PropertyToID("_TreatBackfaceHitAsMiss");
            internal static readonly int _AllowBackwardsRays = Shader.PropertyToID("_AllowBackwardsRays");
            internal static readonly int _TraceBehindObjects = Shader.PropertyToID("_TraceBehindObjects");
            internal static readonly int _MaxSteps = Shader.PropertyToID("_MaxSteps");
            internal static readonly int _FullResolutionFiltering = Shader.PropertyToID("_FullResolutionFiltering");
            internal static readonly int _HalfResolution = Shader.PropertyToID("_HalfResolution");
            internal static readonly int _HighlightSuppression = Shader.PropertyToID("_HighlightSuppression");
            internal static readonly int _PixelsPerMeterAtOneMeter = Shader.PropertyToID("_PixelsPerMeterAtOneMeter");
            internal static readonly int _ScreenEdgeFading = Shader.PropertyToID("_ScreenEdgeFading");
            internal static readonly int _ReflectionBlur = Shader.PropertyToID("_ReflectionBlur");
            internal static readonly int _MaxRayTraceDistance = Shader.PropertyToID("_MaxRayTraceDistance");
            internal static readonly int _FadeDistance = Shader.PropertyToID("_FadeDistance");
            internal static readonly int _LayerThickness = Shader.PropertyToID("_LayerThickness");
            internal static readonly int _SSRMultiplier = Shader.PropertyToID("_SSRMultiplier");
            internal static readonly int _FresnelFade = Shader.PropertyToID("_FresnelFade");
            internal static readonly int _FresnelFadePower = Shader.PropertyToID("_FresnelFadePower");
            internal static readonly int _ReflectionBufferSize = Shader.PropertyToID("_ReflectionBufferSize");
            internal static readonly int _ScreenSize = Shader.PropertyToID("_ScreenSize");
            internal static readonly int _InvScreenSize = Shader.PropertyToID("_InvScreenSize");
            internal static readonly int _ProjInfo = Shader.PropertyToID("_ProjInfo");
            internal static readonly int _CameraClipInfo = Shader.PropertyToID("_CameraClipInfo");
            internal static readonly int _ProjectToPixelMatrix = Shader.PropertyToID("_ProjectToPixelMatrix");
            internal static readonly int _WorldToCameraMatrix = Shader.PropertyToID("_WorldToCameraMatrix");
            internal static readonly int _CameraToWorldMatrix = Shader.PropertyToID("_CameraToWorldMatrix");
            internal static readonly int _Axis = Shader.PropertyToID("_Axis");
            internal static readonly int _CurrentMipLevel = Shader.PropertyToID("_CurrentMipLevel");
            internal static readonly int _NormalAndRoughnessTexture = Shader.PropertyToID("_NormalAndRoughnessTexture");
            internal static readonly int _HitPointTexture = Shader.PropertyToID("_HitPointTexture");
            internal static readonly int _BlurTexture = Shader.PropertyToID("_BlurTexture");
            internal static readonly int _FilteredReflections = Shader.PropertyToID("_FilteredReflections");
            internal static readonly int _FinalReflectionTexture = Shader.PropertyToID("_FinalReflectionTexture");
            internal static readonly int _TempTexture = Shader.PropertyToID("_TempTexture");
        }

        // Unexposed variables
        bool k_HighlightSuppression = false;
        bool k_TraceBehindObjects = true;
        bool k_TreatBackfaceHitAsMiss = false;
        bool k_BilateralUpsample = true;

        enum PassIndex
        {
            RayTraceStep = 0,
            CompositeFinal = 1,
            Blur = 2,
            CompositeSSR = 3,
            MinMipGeneration = 4,
            HitPointToReflections = 5,
            BilateralKeyPack = 6,
            BlitDepthAsCSZ = 7,
            PoissonBlur = 8,
        }

        readonly int[] m_ReflectionTextures = new int[5];

        // Not really needed as SSR only works in deferred right now
        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override bool active
        {
            get
            {
                return model.enabled
                       && context.isGBufferAvailable
                       && !context.interrupted;
            }
        }

        public override void OnEnable()
        {
            m_ReflectionTextures[0] = Shader.PropertyToID("_ReflectionTexture0");
            m_ReflectionTextures[1] = Shader.PropertyToID("_ReflectionTexture1");
            m_ReflectionTextures[2] = Shader.PropertyToID("_ReflectionTexture2");
            m_ReflectionTextures[3] = Shader.PropertyToID("_ReflectionTexture3");
            m_ReflectionTextures[4] = Shader.PropertyToID("_ReflectionTexture4");
        }

        public override string GetName()
        {
            return "Screen Space Reflection";
        }

        public override CameraEvent GetCameraEvent()
        {
            return CameraEvent.AfterFinalPass;
        }

        public override void PopulateCommandBuffer(CommandBuffer cb)
        {
            var settings = model.settings;
            var camera = context.camera;

            // Material setup
            int downsampleAmount = (settings.reflection.reflectionQuality == ScreenSpaceReflectionModel.SSRResolution.High) ? 1 : 2;

            var rtW = context.width / downsampleAmount;
            var rtH = context.height / downsampleAmount;

            float sWidth = context.width;
            float sHeight = context.height;

            float sx = sWidth / 2f;
            float sy = sHeight / 2f;

            var material = context.materialFactory.Get("Hidden/Post FX/Screen Space Reflection");

            material.SetInt(Uniforms._RayStepSize, settings.reflection.stepSize);
            material.SetInt(Uniforms._AdditiveReflection, settings.reflection.blendType == ScreenSpaceReflectionModel.SSRReflectionBlendType.Additive ? 1 : 0);
            material.SetInt(Uniforms._BilateralUpsampling, k_BilateralUpsample ? 1 : 0);
            material.SetInt(Uniforms._TreatBackfaceHitAsMiss, k_TreatBackfaceHitAsMiss ? 1 : 0);
            material.SetInt(Uniforms._AllowBackwardsRays, settings.reflection.reflectBackfaces ? 1 : 0);
            material.SetInt(Uniforms._TraceBehindObjects, k_TraceBehindObjects ? 1 : 0);
            material.SetInt(Uniforms._MaxSteps, settings.reflection.iterationCount);
            material.SetInt(Uniforms._FullResolutionFiltering, 0);
            material.SetInt(Uniforms._HalfResolution, (settings.reflection.reflectionQuality != ScreenSpaceReflectionModel.SSRResolution.High) ? 1 : 0);
            material.SetInt(Uniforms._HighlightSuppression, k_HighlightSuppression ? 1 : 0);

            // The height in pixels of a 1m object if viewed from 1m away.
            float pixelsPerMeterAtOneMeter = sWidth / (-2f * Mathf.Tan(camera.fieldOfView / 180f * Mathf.PI * 0.5f));

            material.SetFloat(Uniforms._PixelsPerMeterAtOneMeter, pixelsPerMeterAtOneMeter);
            material.SetFloat(Uniforms._ScreenEdgeFading, settings.screenEdgeMask.intensity);
            material.SetFloat(Uniforms._ReflectionBlur, settings.reflection.reflectionBlur);
            material.SetFloat(Uniforms._MaxRayTraceDistance, settings.reflection.maxDistance);
            material.SetFloat(Uniforms._FadeDistance, settings.intensity.fadeDistance);
            material.SetFloat(Uniforms._LayerThickness, settings.reflection.widthModifier);
            material.SetFloat(Uniforms._SSRMultiplier, settings.intensity.reflectionMultiplier);
            material.SetFloat(Uniforms._FresnelFade, settings.intensity.fresnelFade);
            material.SetFloat(Uniforms._FresnelFadePower, settings.intensity.fresnelFadePower);

            var P = camera.projectionMatrix;
            var projInfo = new Vector4(
                    -2f / (sWidth * P[0]),
                    -2f / (sHeight * P[5]),
                    (1f - P[2]) / P[0],
                    (1f + P[6]) / P[5]
                    );

            var cameraClipInfo = float.IsPositiveInfinity(camera.farClipPlane) ?
                new Vector3(camera.nearClipPlane, -1f, 1f) :
                new Vector3(camera.nearClipPlane * camera.farClipPlane, camera.nearClipPlane - camera.farClipPlane, camera.farClipPlane);

            material.SetVector(Uniforms._ReflectionBufferSize, new Vector2(rtW, rtH));
            material.SetVector(Uniforms._ScreenSize, new Vector2(sWidth, sHeight));
            material.SetVector(Uniforms._InvScreenSize, new Vector2(1f / sWidth, 1f / sHeight));
            material.SetVector(Uniforms._ProjInfo, projInfo); // used for unprojection

            material.SetVector(Uniforms._CameraClipInfo, cameraClipInfo);

            var warpToScreenSpaceMatrix = new Matrix4x4();
            warpToScreenSpaceMatrix.SetRow(0, new Vector4(sx, 0f, 0f, sx));
            warpToScreenSpaceMatrix.SetRow(1, new Vector4(0f, sy, 0f, sy));
            warpToScreenSpaceMatrix.SetRow(2, new Vector4(0f, 0f, 1f, 0f));
            warpToScreenSpaceMatrix.SetRow(3, new Vector4(0f, 0f, 0f, 1f));

            var projectToPixelMatrix = warpToScreenSpaceMatrix * P;

            material.SetMatrix(Uniforms._ProjectToPixelMatrix, projectToPixelMatrix);
            material.SetMatrix(Uniforms._WorldToCameraMatrix, camera.worldToCameraMatrix);
            material.SetMatrix(Uniforms._CameraToWorldMatrix, camera.worldToCameraMatrix.inverse);

            // Command buffer setup
            var intermediateFormat = context.isHdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            const int maxMip = 5;

            var kNormalAndRoughnessTexture = Uniforms._NormalAndRoughnessTexture;
            var kHitPointTexture = Uniforms._HitPointTexture;
            var kBlurTexture = Uniforms._BlurTexture;
            var kFilteredReflections = Uniforms._FilteredReflections;
            var kFinalReflectionTexture = Uniforms._FinalReflectionTexture;
            var kTempTexture = Uniforms._TempTexture;

            // RGB: Normals, A: Roughness.
            // Has the nice benefit of allowing us to control the filtering mode as well.
            cb.GetTemporaryRT(kNormalAndRoughnessTexture, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            cb.GetTemporaryRT(kHitPointTexture, rtW, rtH, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

            for (int i = 0; i < maxMip; ++i)
            {
                // We explicitly interpolate during bilateral upsampling.
                cb.GetTemporaryRT(m_ReflectionTextures[i], rtW >> i, rtH >> i, 0, FilterMode.Bilinear, intermediateFormat);
            }

            cb.GetTemporaryRT(kFilteredReflections, rtW, rtH, 0, k_BilateralUpsample ? FilterMode.Point : FilterMode.Bilinear, intermediateFormat);
            cb.GetTemporaryRT(kFinalReflectionTexture, rtW, rtH, 0, FilterMode.Point, intermediateFormat);

            cb.Blit(BuiltinRenderTextureType.CameraTarget, kNormalAndRoughnessTexture, material, (int)PassIndex.BilateralKeyPack);
            cb.Blit(BuiltinRenderTextureType.CameraTarget, kHitPointTexture, material, (int)PassIndex.RayTraceStep);
            cb.Blit(BuiltinRenderTextureType.CameraTarget, kFilteredReflections, material, (int)PassIndex.HitPointToReflections);
            cb.Blit(kFilteredReflections, m_ReflectionTextures[0], material, (int)PassIndex.PoissonBlur);

            for (int i = 1; i < maxMip; ++i)
            {
                int inputTex = m_ReflectionTextures[i - 1];

                int lowMip = i;

                cb.GetTemporaryRT(kBlurTexture, rtW >> lowMip, rtH >> lowMip, 0, FilterMode.Bilinear, intermediateFormat);
                cb.SetGlobalVector(Uniforms._Axis, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                cb.SetGlobalFloat(Uniforms._CurrentMipLevel, i - 1.0f);

                cb.Blit(inputTex, kBlurTexture, material, (int)PassIndex.Blur);

                cb.SetGlobalVector(Uniforms._Axis, new Vector4(0.0f, 1.0f, 0.0f, 0.0f));

                inputTex = m_ReflectionTextures[i];
                cb.Blit(kBlurTexture, inputTex, material, (int)PassIndex.Blur);
                cb.ReleaseTemporaryRT(kBlurTexture);
            }

            cb.Blit(m_ReflectionTextures[0], kFinalReflectionTexture, material, (int)PassIndex.CompositeSSR);

            cb.GetTemporaryRT(kTempTexture, camera.pixelWidth, camera.pixelHeight, 0, FilterMode.Bilinear, intermediateFormat);

            cb.Blit(BuiltinRenderTextureType.CameraTarget, kTempTexture, material, (int)PassIndex.CompositeFinal);
            cb.Blit(kTempTexture, BuiltinRenderTextureType.CameraTarget);

            cb.ReleaseTemporaryRT(kTempTexture);
        }
    }

    public sealed class UserLutComponent : PostProcessingComponentRenderTexture<UserLutModel>
    {
        static class Uniforms
        {
            internal static readonly int _UserLut = Shader.PropertyToID("_UserLut");
            internal static readonly int _UserLut_Params = Shader.PropertyToID("_UserLut_Params");
        }

        public override bool active
        {
            get
            {
                var settings = model.settings;
                return model.enabled
                       && settings.lut != null
                       && settings.contribution > 0f
                       && settings.lut.height == (int)Mathf.Sqrt(settings.lut.width)
                       && !context.interrupted;
            }
        }

        public override void Prepare(Material uberMaterial)
        {
            var settings = model.settings;
            uberMaterial.EnableKeyword("USER_LUT");
            uberMaterial.SetTexture(Uniforms._UserLut, settings.lut);
            uberMaterial.SetVector(Uniforms._UserLut_Params, new Vector4(1f / settings.lut.width, 1f / settings.lut.height, settings.lut.height - 1f, settings.contribution));
        }

        public void OnGUI()
        {
            var settings = model.settings;
            var rect = new Rect(context.viewport.x * Screen.width + 8f, 8f, settings.lut.width, settings.lut.height);
            GUI.DrawTexture(rect, settings.lut);
        }
    }

    public sealed class VignetteComponent : PostProcessingComponentRenderTexture<VignetteModel>
    {
        static class Uniforms
        {
            internal static readonly int _Vignette_Color = Shader.PropertyToID("_Vignette_Color");
            internal static readonly int _Vignette_Center = Shader.PropertyToID("_Vignette_Center");
            internal static readonly int _Vignette_Settings = Shader.PropertyToID("_Vignette_Settings");
            internal static readonly int _Vignette_Mask = Shader.PropertyToID("_Vignette_Mask");
            internal static readonly int _Vignette_Opacity = Shader.PropertyToID("_Vignette_Opacity");
        }

        public override bool active
        {
            get
            {
                return model.enabled
                       && !context.interrupted;
            }
        }

        public override void Prepare(Material uberMaterial)
        {
            var settings = model.settings;
            uberMaterial.SetColor(Uniforms._Vignette_Color, settings.color);

            if (settings.mode == VignetteModel.Mode.Classic)
            {
                uberMaterial.SetVector(Uniforms._Vignette_Center, settings.center);
                uberMaterial.EnableKeyword("VIGNETTE_CLASSIC");
                float roundness = (1f - settings.roundness) * 6f + settings.roundness;
                uberMaterial.SetVector(Uniforms._Vignette_Settings, new Vector4(settings.intensity * 3f, settings.smoothness * 5f, roundness, settings.rounded ? 1f : 0f));
            }
            else if (settings.mode == VignetteModel.Mode.Masked)
            {
                if (settings.mask != null && settings.opacity > 0f)
                {
                    uberMaterial.EnableKeyword("VIGNETTE_MASKED");
                    uberMaterial.SetTexture(Uniforms._Vignette_Mask, settings.mask);
                    uberMaterial.SetFloat(Uniforms._Vignette_Opacity, settings.opacity);
                }
            }
        }
    }
    #endregion

    #region Models
    [Serializable]
    public class AmbientOcclusionModel : PostProcessingModel
    {
        public enum SampleCount
        {
            Lowest = 3,
            Low = 6,
            Medium = 10,
            High = 16
        }

        [Serializable]
        public struct Settings
        {
            [Range(0, 4), Tooltip("Degree of darkness produced by the effect.")]
            public float intensity;

            [Min(1e-4f), Tooltip("Radius of sample points, which affects extent of darkened areas.")]
            public float radius;

            [Tooltip("Number of sample points, which affects quality and performance.")]
            public SampleCount sampleCount;

            [Tooltip("Halves the resolution of the effect to increase performance at the cost of visual quality.")]
            public bool downsampling;

            [Tooltip("Forces compatibility with Forward rendered objects when working with the Deferred rendering path.")]
            public bool forceForwardCompatibility;

            [Tooltip("Enables the ambient-only mode in that the effect only affects ambient lighting. This mode is only available with the Deferred rendering path and HDR rendering.")]
            public bool ambientOnly;

            [Tooltip("Toggles the use of a higher precision depth texture with the forward rendering path (may impact performances). Has no effect with the deferred rendering path.")]
            public bool highPrecision;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        intensity = 1f,
                        radius = 0.3f,
                        sampleCount = SampleCount.Medium,
                        downsampling = true,
                        forceForwardCompatibility = false,
                        ambientOnly = false,
                        highPrecision = false
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class AntialiasingModel : PostProcessingModel
    {
        public enum Method
        {
            Fxaa,
            Taa
        }

        // Most settings aren't exposed to the user anymore, presets are enough. Still, I'm leaving
        // the tooltip attributes in case an user wants to customize each preset.

        #region FXAA Settings
        public enum FxaaPreset
        {
            ExtremePerformance,
            Performance,
            Default,
            Quality,
            ExtremeQuality
        }

        [Serializable]
        public struct FxaaQualitySettings
        {
            [Tooltip("The amount of desired sub-pixel aliasing removal. Effects the sharpeness of the output.")]
            [Range(0f, 1f)]
            public float subpixelAliasingRemovalAmount;

            [Tooltip("The minimum amount of local contrast required to qualify a region as containing an edge.")]
            [Range(0.063f, 0.333f)]
            public float edgeDetectionThreshold;

            [Tooltip("Local contrast adaptation value to disallow the algorithm from executing on the darker regions.")]
            [Range(0f, 0.0833f)]
            public float minimumRequiredLuminance;

            public static FxaaQualitySettings[] presets =
            {
                // ExtremePerformance
                new FxaaQualitySettings
                {
                    subpixelAliasingRemovalAmount = 0f,
                    edgeDetectionThreshold = 0.333f,
                    minimumRequiredLuminance = 0.0833f
                },

                // Performance
                new FxaaQualitySettings
                {
                    subpixelAliasingRemovalAmount = 0.25f,
                    edgeDetectionThreshold = 0.25f,
                    minimumRequiredLuminance = 0.0833f
                },

                // Default
                new FxaaQualitySettings
                {
                    subpixelAliasingRemovalAmount = 0.75f,
                    edgeDetectionThreshold = 0.166f,
                    minimumRequiredLuminance = 0.0833f
                },

                // Quality
                new FxaaQualitySettings
                {
                    subpixelAliasingRemovalAmount = 1f,
                    edgeDetectionThreshold = 0.125f,
                    minimumRequiredLuminance = 0.0625f
                },

                // ExtremeQuality
                new FxaaQualitySettings
                {
                    subpixelAliasingRemovalAmount = 1f,
                    edgeDetectionThreshold = 0.063f,
                    minimumRequiredLuminance = 0.0312f
                }
            };
        }

        [Serializable]
        public struct FxaaConsoleSettings
        {
            [Tooltip("The amount of spread applied to the sampling coordinates while sampling for subpixel information.")]
            [Range(0.33f, 0.5f)]
            public float subpixelSpreadAmount;

            [Tooltip("This value dictates how sharp the edges in the image are kept; a higher value implies sharper edges.")]
            [Range(2f, 8f)]
            public float edgeSharpnessAmount;

            [Tooltip("The minimum amount of local contrast required to qualify a region as containing an edge.")]
            [Range(0.125f, 0.25f)]
            public float edgeDetectionThreshold;

            [Tooltip("Local contrast adaptation value to disallow the algorithm from executing on the darker regions.")]
            [Range(0.04f, 0.06f)]
            public float minimumRequiredLuminance;

            public static FxaaConsoleSettings[] presets =
            {
                // ExtremePerformance
                new FxaaConsoleSettings
                {
                    subpixelSpreadAmount = 0.33f,
                    edgeSharpnessAmount = 8f,
                    edgeDetectionThreshold = 0.25f,
                    minimumRequiredLuminance = 0.06f
                },

                // Performance
                new FxaaConsoleSettings
                {
                    subpixelSpreadAmount = 0.33f,
                    edgeSharpnessAmount = 8f,
                    edgeDetectionThreshold = 0.125f,
                    minimumRequiredLuminance = 0.06f
                },

                // Default
                new FxaaConsoleSettings
                {
                    subpixelSpreadAmount = 0.5f,
                    edgeSharpnessAmount = 8f,
                    edgeDetectionThreshold = 0.125f,
                    minimumRequiredLuminance = 0.05f
                },

                // Quality
                new FxaaConsoleSettings
                {
                    subpixelSpreadAmount = 0.5f,
                    edgeSharpnessAmount = 4f,
                    edgeDetectionThreshold = 0.125f,
                    minimumRequiredLuminance = 0.04f
                },

                // ExtremeQuality
                new FxaaConsoleSettings
                {
                    subpixelSpreadAmount = 0.5f,
                    edgeSharpnessAmount = 2f,
                    edgeDetectionThreshold = 0.125f,
                    minimumRequiredLuminance = 0.04f
                }
            };
        }

        [Serializable]
        public struct FxaaSettings
        {
            public FxaaPreset preset;

            public static FxaaSettings defaultSettings
            {
                get
                {
                    return new FxaaSettings
                    {
                        preset = FxaaPreset.Default
                    };
                }
            }
        }
        #endregion

        #region TAA Settings
        [Serializable]
        public struct TaaSettings
        {
            [Tooltip("The diameter (in texels) inside which jitter samples are spread. Smaller values result in crisper but more aliased output, while larger values result in more stable but blurrier output.")]
            [Range(0.1f, 1f)]
            public float jitterSpread;

            [Tooltip("Controls the amount of sharpening applied to the color buffer.")]
            [Range(0f, 3f)]
            public float sharpen;

            [Tooltip("The blend coefficient for a stationary fragment. Controls the percentage of history sample blended into the final color.")]
            [Range(0f, 0.99f)]
            public float stationaryBlending;

            [Tooltip("The blend coefficient for a fragment with significant motion. Controls the percentage of history sample blended into the final color.")]
            [Range(0f, 0.99f)]
            public float motionBlending;

            public static TaaSettings defaultSettings
            {
                get
                {
                    return new TaaSettings
                    {
                        jitterSpread = 0.75f,
                        sharpen = 0.3f,
                        stationaryBlending = 0.95f,
                        motionBlending = 0.85f
                    };
                }
            }
        }
        #endregion

        [Serializable]
        public struct Settings
        {
            public Method method;
            public FxaaSettings fxaaSettings;
            public TaaSettings taaSettings;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        method = Method.Fxaa,
                        fxaaSettings = FxaaSettings.defaultSettings,
                        taaSettings = TaaSettings.defaultSettings
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class BloomModel : PostProcessingModel
    {
        [Serializable]
        public struct BloomSettings
        {
            [Min(0f), Tooltip("Strength of the bloom filter.")]
            public float intensity;

            [Min(0f), Tooltip("Filters out pixels under this level of brightness.")]
            public float threshold;

            public float thresholdLinear
            {
                set { threshold = Mathf.LinearToGammaSpace(value); }
                get { return Mathf.GammaToLinearSpace(threshold); }
            }

            [Range(0f, 1f), Tooltip("Makes transition between under/over-threshold gradual (0 = hard threshold, 1 = soft threshold).")]
            public float softKnee;

            [Range(1f, 7f), Tooltip("Changes extent of veiling effects in a screen resolution-independent fashion.")]
            public float radius;

            [Tooltip("Reduces flashing noise with an additional filter.")]
            public bool antiFlicker;

            public static BloomSettings defaultSettings
            {
                get
                {
                    return new BloomSettings
                    {
                        intensity = 0.5f,
                        threshold = 1.1f,
                        softKnee = 0.5f,
                        radius = 4f,
                        antiFlicker = false,
                    };
                }
            }
        }

        [Serializable]
        public struct LensDirtSettings
        {
            [Tooltip("Dirtiness texture to add smudges or dust to the lens.")]
            public Texture texture;

            [Min(0f), Tooltip("Amount of lens dirtiness.")]
            public float intensity;

            public static LensDirtSettings defaultSettings
            {
                get
                {
                    return new LensDirtSettings
                    {
                        texture = null,
                        intensity = 3f
                    };
                }
            }
        }

        [Serializable]
        public struct Settings
        {
            public BloomSettings bloom;
            public LensDirtSettings lensDirt;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        bloom = BloomSettings.defaultSettings,
                        lensDirt = LensDirtSettings.defaultSettings
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class BuiltinDebugViewsModel : PostProcessingModel
    {
        [Serializable]
        public struct DepthSettings
        {
            [Range(0f, 1f), Tooltip("Scales the camera far plane before displaying the depth map.")]
            public float scale;

            public static DepthSettings defaultSettings
            {
                get
                {
                    return new DepthSettings
                    {
                        scale = 1f
                    };
                }
            }
        }

        [Serializable]
        public struct MotionVectorsSettings
        {
            [Range(0f, 1f), Tooltip("Opacity of the source render.")]
            public float sourceOpacity;

            [Range(0f, 1f), Tooltip("Opacity of the per-pixel motion vector colors.")]
            public float motionImageOpacity;

            [Min(0f), Tooltip("Because motion vectors are mainly very small vectors, you can use this setting to make them more visible.")]
            public float motionImageAmplitude;

            [Range(0f, 1f), Tooltip("Opacity for the motion vector arrows.")]
            public float motionVectorsOpacity;

            [Range(8, 64), Tooltip("The arrow density on screen.")]
            public int motionVectorsResolution;

            [Min(0f), Tooltip("Tweaks the arrows length.")]
            public float motionVectorsAmplitude;

            public static MotionVectorsSettings defaultSettings
            {
                get
                {
                    return new MotionVectorsSettings
                    {
                        sourceOpacity = 1f,

                        motionImageOpacity = 0f,
                        motionImageAmplitude = 16f,

                        motionVectorsOpacity = 1f,
                        motionVectorsResolution = 24,
                        motionVectorsAmplitude = 64f
                    };
                }
            }
        }

        public enum Mode
        {
            None,

            Depth,
            Normals,
            MotionVectors,

            AmbientOcclusion,
            EyeAdaptation,
            FocusPlane,
            PreGradingLog,
            LogLut,
            UserLut
        }

        [Serializable]
        public struct Settings
        {
            public Mode mode;
            public DepthSettings depth;
            public MotionVectorsSettings motionVectors;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        mode = Mode.None,
                        depth = DepthSettings.defaultSettings,
                        motionVectors = MotionVectorsSettings.defaultSettings
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public bool willInterrupt
        {
            get
            {
                return !IsModeActive(Mode.None)
                       && !IsModeActive(Mode.EyeAdaptation)
                       && !IsModeActive(Mode.PreGradingLog)
                       && !IsModeActive(Mode.LogLut)
                       && !IsModeActive(Mode.UserLut);
            }
        }

        public override void Reset()
        {
            settings = Settings.defaultSettings;
        }

        public bool IsModeActive(Mode mode)
        {
            return m_Settings.mode == mode;
        }
    }

    [Serializable]
    public class ChromaticAberrationModel : PostProcessingModel
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Shift the hue of chromatic aberrations.")]
            public Texture2D spectralTexture;

            [Range(0f, 1f), Tooltip("Amount of tangential distortion.")]
            public float intensity;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        spectralTexture = null,
                        intensity = 0.1f
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class ColorGradingModel : PostProcessingModel
    {
        public enum Tonemapper
        {
            None,

            /// <summary>
            /// ACES Filmic reference tonemapper.
            /// </summary>
            ACES,

            /// <summary>
            /// Neutral tonemapper (based off John Hable's & Jim Hejl's work).
            /// </summary>
            Neutral
        }

        [Serializable]
        public struct TonemappingSettings
        {
            [Tooltip("Tonemapping algorithm to use at the end of the color grading process. Use \"Neutral\" if you need a customizable tonemapper or \"Filmic\" to give a standard filmic look to your scenes.")]
            public Tonemapper tonemapper;

            // Neutral settings
            [Range(-0.1f, 0.1f)]
            public float neutralBlackIn;

            [Range(1f, 20f)]
            public float neutralWhiteIn;

            [Range(-0.09f, 0.1f)]
            public float neutralBlackOut;

            [Range(1f, 19f)]
            public float neutralWhiteOut;

            [Range(0.1f, 20f)]
            public float neutralWhiteLevel;

            [Range(1f, 10f)]
            public float neutralWhiteClip;

            public static TonemappingSettings defaultSettings
            {
                get
                {
                    return new TonemappingSettings
                    {
                        tonemapper = Tonemapper.Neutral,

                        neutralBlackIn = 0.02f,
                        neutralWhiteIn = 10f,
                        neutralBlackOut = 0f,
                        neutralWhiteOut = 10f,
                        neutralWhiteLevel = 5.3f,
                        neutralWhiteClip = 10f
                    };
                }
            }
        }

        [Serializable]
        public struct BasicSettings
        {
            [Tooltip("Adjusts the overall exposure of the scene in EV units. This is applied after HDR effect and right before tonemapping so it won't affect previous effects in the chain.")]
            public float postExposure;

            [Range(-100f, 100f), Tooltip("Sets the white balance to a custom color temperature.")]
            public float temperature;

            [Range(-100f, 100f), Tooltip("Sets the white balance to compensate for a green or magenta tint.")]
            public float tint;

            [Range(-180f, 180f), Tooltip("Shift the hue of all colors.")]
            public float hueShift;

            [Range(0f, 2f), Tooltip("Pushes the intensity of all colors.")]
            public float saturation;

            [Range(0f, 2f), Tooltip("Expands or shrinks the overall range of tonal values.")]
            public float contrast;

            public static BasicSettings defaultSettings
            {
                get
                {
                    return new BasicSettings
                    {
                        postExposure = 0f,

                        temperature = 0f,
                        tint = 0f,

                        hueShift = 0f,
                        saturation = 1f,
                        contrast = 1f,
                    };
                }
            }
        }

        [Serializable]
        public struct ChannelMixerSettings
        {
            public Vector3 red;
            public Vector3 green;
            public Vector3 blue;

            [HideInInspector]
            public int currentEditingChannel; // Used only in the editor

            public static ChannelMixerSettings defaultSettings
            {
                get
                {
                    return new ChannelMixerSettings
                    {
                        red = new Vector3(1f, 0f, 0f),
                        green = new Vector3(0f, 1f, 0f),
                        blue = new Vector3(0f, 0f, 1f),
                        currentEditingChannel = 0
                    };
                }
            }
        }

        [Serializable]
        public struct LogWheelsSettings
        {
            [Trackball("GetSlopeValue")]
            public Color slope;

            [Trackball("GetPowerValue")]
            public Color power;

            [Trackball("GetOffsetValue")]
            public Color offset;

            public static LogWheelsSettings defaultSettings
            {
                get
                {
                    return new LogWheelsSettings
                    {
                        slope = Color.clear,
                        power = Color.clear,
                        offset = Color.clear
                    };
                }
            }
        }

        [Serializable]
        public struct LinearWheelsSettings
        {
            [Trackball("GetLiftValue")]
            public Color lift;

            [Trackball("GetGammaValue")]
            public Color gamma;

            [Trackball("GetGainValue")]
            public Color gain;

            public static LinearWheelsSettings defaultSettings
            {
                get
                {
                    return new LinearWheelsSettings
                    {
                        lift = Color.clear,
                        gamma = Color.clear,
                        gain = Color.clear
                    };
                }
            }
        }

        public enum ColorWheelMode
        {
            Linear,
            Log
        }

        [Serializable]
        public struct ColorWheelsSettings
        {
            public ColorWheelMode mode;

            [TrackballGroup]
            public LogWheelsSettings log;

            [TrackballGroup]
            public LinearWheelsSettings linear;

            public static ColorWheelsSettings defaultSettings
            {
                get
                {
                    return new ColorWheelsSettings
                    {
                        mode = ColorWheelMode.Log,
                        log = LogWheelsSettings.defaultSettings,
                        linear = LinearWheelsSettings.defaultSettings
                    };
                }
            }
        }

        [Serializable]
        public struct CurvesSettings
        {
            public ColorGradingCurve master;
            public ColorGradingCurve red;
            public ColorGradingCurve green;
            public ColorGradingCurve blue;
            public ColorGradingCurve hueVShue;
            public ColorGradingCurve hueVSsat;
            public ColorGradingCurve satVSsat;
            public ColorGradingCurve lumVSsat;

            // Used only in the editor
            [HideInInspector]
            public int e_CurrentEditingCurve;
            [HideInInspector]
            public bool e_CurveY;
            [HideInInspector]
            public bool e_CurveR;
            [HideInInspector]
            public bool e_CurveG;
            [HideInInspector]
            public bool e_CurveB;

            public static CurvesSettings defaultSettings
            {
                get
                {
                    return new CurvesSettings
                    {
                        master = new ColorGradingCurve(new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f)), 0f, false, new Vector2(0f, 1f)),
                        red = new ColorGradingCurve(new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f)), 0f, false, new Vector2(0f, 1f)),
                        green = new ColorGradingCurve(new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f)), 0f, false, new Vector2(0f, 1f)),
                        blue = new ColorGradingCurve(new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f)), 0f, false, new Vector2(0f, 1f)),

                        hueVShue = new ColorGradingCurve(new AnimationCurve(), 0.5f, true, new Vector2(0f, 1f)),
                        hueVSsat = new ColorGradingCurve(new AnimationCurve(), 0.5f, true, new Vector2(0f, 1f)),
                        satVSsat = new ColorGradingCurve(new AnimationCurve(), 0.5f, false, new Vector2(0f, 1f)),
                        lumVSsat = new ColorGradingCurve(new AnimationCurve(), 0.5f, false, new Vector2(0f, 1f)),

                        e_CurrentEditingCurve = 0,
                        e_CurveY = true,
                        e_CurveR = false,
                        e_CurveG = false,
                        e_CurveB = false
                    };
                }
            }
        }

        [Serializable]
        public struct Settings
        {
            public TonemappingSettings tonemapping;
            public BasicSettings basic;
            public ChannelMixerSettings channelMixer;
            public ColorWheelsSettings colorWheels;
            public CurvesSettings curves;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        tonemapping = TonemappingSettings.defaultSettings,
                        basic = BasicSettings.defaultSettings,
                        channelMixer = ChannelMixerSettings.defaultSettings,
                        colorWheels = ColorWheelsSettings.defaultSettings,
                        curves = CurvesSettings.defaultSettings
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set
            {
                m_Settings = value;
                OnValidate();
            }
        }

        public bool isDirty { get; internal set; }
        public RenderTexture bakedLut { get; internal set; }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
            OnValidate();
        }

        public override void OnValidate()
        {
            isDirty = true;
        }
    }

    [Serializable]
    public class DepthOfFieldModel : PostProcessingModel
    {
        public enum KernelSize
        {
            Small,
            Medium,
            Large,
            VeryLarge
        }

        [Serializable]
        public struct Settings
        {
            [Min(0.1f), Tooltip("Distance to the point of focus.")]
            public float focusDistance;

            [Range(0.05f, 32f), Tooltip("Ratio of aperture (known as f-stop or f-number). The smaller the value is, the shallower the depth of field is.")]
            public float aperture;

            [Range(1f, 300f), Tooltip("Distance between the lens and the film. The larger the value is, the shallower the depth of field is.")]
            public float focalLength;

            [Tooltip("Calculate the focal length automatically from the field-of-view value set on the camera. Using this setting isn't recommended.")]
            public bool useCameraFov;

            [Tooltip("Convolution kernel size of the bokeh filter, which determines the maximum radius of bokeh. It also affects the performance (the larger the kernel is, the longer the GPU time is required).")]
            public KernelSize kernelSize;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        focusDistance = 10f,
                        aperture = 5.6f,
                        focalLength = 50f,
                        useCameraFov = false,
                        kernelSize = KernelSize.Medium
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class DitheringModel : PostProcessingModel
    {
        [Serializable]
        public struct Settings
        {
            public static Settings defaultSettings
            {
                get { return new Settings(); }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class EyeAdaptationModel : PostProcessingModel
    {
        public enum EyeAdaptationType
        {
            Progressive,
            Fixed
        }

        [Serializable]
        public struct Settings
        {
            [Range(1f, 99f), Tooltip("Filters the dark part of the histogram when computing the average luminance to avoid very dark pixels from contributing to the auto exposure. Unit is in percent.")]
            public float lowPercent;

            [Range(1f, 99f), Tooltip("Filters the bright part of the histogram when computing the average luminance to avoid very dark pixels from contributing to the auto exposure. Unit is in percent.")]
            public float highPercent;

            [Tooltip("Minimum average luminance to consider for auto exposure (in EV).")]
            public float minLuminance;

            [Tooltip("Maximum average luminance to consider for auto exposure (in EV).")]
            public float maxLuminance;

            [Min(0f), Tooltip("Exposure bias. Use this to offset the global exposure of the scene.")]
            public float keyValue;

            [Tooltip("Set this to true to let Unity handle the key value automatically based on average luminance.")]
            public bool dynamicKeyValue;

            [Tooltip("Use \"Progressive\" if you want the auto exposure to be animated. Use \"Fixed\" otherwise.")]
            public EyeAdaptationType adaptationType;

            [Min(0f), Tooltip("Adaptation speed from a dark to a light environment.")]
            public float speedUp;

            [Min(0f), Tooltip("Adaptation speed from a light to a dark environment.")]
            public float speedDown;

            [Range(-16, -1), Tooltip("Lower bound for the brightness range of the generated histogram (in EV). The bigger the spread between min & max, the lower the precision will be.")]
            public int logMin;

            [Range(1, 16), Tooltip("Upper bound for the brightness range of the generated histogram (in EV). The bigger the spread between min & max, the lower the precision will be.")]
            public int logMax;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        lowPercent = 45f,
                        highPercent = 95f,

                        minLuminance = -5f,
                        maxLuminance = 1f,
                        keyValue = 0.25f,
                        dynamicKeyValue = true,

                        adaptationType = EyeAdaptationType.Progressive,
                        speedUp = 2f,
                        speedDown = 1f,

                        logMin = -8,
                        logMax = 4
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class FogModel : PostProcessingModel
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Should the fog affect the skybox?")]
            public bool excludeSkybox;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        excludeSkybox = true
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class GrainModel : PostProcessingModel
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Enable the use of colored grain.")]
            public bool colored;

            [Range(0f, 1f), Tooltip("Grain strength. Higher means more visible grain.")]
            public float intensity;

            [Range(0.3f, 3f), Tooltip("Grain particle size.")]
            public float size;

            [Range(0f, 1f), Tooltip("Controls the noisiness response curve based on scene luminance. Lower values mean less noise in dark areas.")]
            public float luminanceContribution;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        colored = true,
                        intensity = 0.5f,
                        size = 1f,
                        luminanceContribution = 0.8f
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class MotionBlurModel : PostProcessingModel
    {
        [Serializable]
        public struct Settings
        {
            [Range(0f, 360f), Tooltip("The angle of rotary shutter. Larger values give longer exposure.")]
            public float shutterAngle;

            [Range(4, 32), Tooltip("The amount of sample points, which affects quality and performances.")]
            public int sampleCount;

            [Range(0f, 1f), Tooltip("The strength of multiple frame blending. The opacity of preceding frames are determined from this coefficient and time differences.")]
            public float frameBlending;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        shutterAngle = 270f,
                        sampleCount = 10,
                        frameBlending = 0f
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class ScreenSpaceReflectionModel : PostProcessingModel
    {
        public enum SSRResolution
        {
            High = 0,
            Low = 2
        }

        public enum SSRReflectionBlendType
        {
            PhysicallyBased,
            Additive
        }

        [Serializable]
        public struct IntensitySettings
        {
            [Tooltip("Nonphysical multiplier for the SSR reflections. 1.0 is physically based.")]
            [Range(0.0f, 2.0f)]
            public float reflectionMultiplier;

            [Tooltip("How far away from the maxDistance to begin fading SSR.")]
            [Range(0.0f, 1000.0f)]
            public float fadeDistance;

            [Tooltip("Amplify Fresnel fade out. Increase if floor reflections look good close to the surface and bad farther 'under' the floor.")]
            [Range(0.0f, 1.0f)]
            public float fresnelFade;

            [Tooltip("Higher values correspond to a faster Fresnel fade as the reflection changes from the grazing angle.")]
            [Range(0.1f, 10.0f)]
            public float fresnelFadePower;
        }

        [Serializable]
        public struct ReflectionSettings
        {
            // When enabled, we just add our reflections on top of the existing ones. This is physically incorrect, but several
            // popular demos and games have taken this approach, and it does hide some artifacts.
            [Tooltip("How the reflections are blended into the render.")]
            public SSRReflectionBlendType blendType;

            [Tooltip("Half resolution SSRR is much faster, but less accurate.")]
            public SSRResolution reflectionQuality;

            [Tooltip("Maximum reflection distance in world units.")]
            [Range(0.1f, 300.0f)]
            public float maxDistance;

            /// REFLECTIONS
            [Tooltip("Max raytracing length.")]
            [Range(16, 1024)]
            public int iterationCount;

            [Tooltip("Log base 2 of ray tracing coarse step size. Higher traces farther, lower gives better quality silhouettes.")]
            [Range(1, 16)]
            public int stepSize;

            [Tooltip("Typical thickness of columns, walls, furniture, and other objects that reflection rays might pass behind.")]
            [Range(0.01f, 10.0f)]
            public float widthModifier;

            [Tooltip("Blurriness of reflections.")]
            [Range(0.1f, 8.0f)]
            public float reflectionBlur;

            [Tooltip("Disable for a performance gain in scenes where most glossy objects are horizontal, like floors, water, and tables. Leave on for scenes with glossy vertical objects.")]
            public bool reflectBackfaces;
        }

        [Serializable]
        public struct ScreenEdgeMask
        {
            [Tooltip("Higher = fade out SSRR near the edge of the screen so that reflections don't pop under camera motion.")]
            [Range(0.0f, 1.0f)]
            public float intensity;
        }

        [Serializable]
        public struct Settings
        {
            public ReflectionSettings reflection;
            public IntensitySettings intensity;
            public ScreenEdgeMask screenEdgeMask;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        reflection = new ReflectionSettings
                        {
                            blendType = SSRReflectionBlendType.PhysicallyBased,
                            reflectionQuality = SSRResolution.Low,
                            maxDistance = 100f,
                            iterationCount = 256,
                            stepSize = 3,
                            widthModifier = 0.5f,
                            reflectionBlur = 1f,
                            reflectBackfaces = false
                        },

                        intensity = new IntensitySettings
                        {
                            reflectionMultiplier = 1f,
                            fadeDistance = 100f,

                            fresnelFade = 1f,
                            fresnelFadePower = 1f,
                        },

                        screenEdgeMask = new ScreenEdgeMask
                        {
                            intensity = 0.03f
                        }
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class UserLutModel : PostProcessingModel
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Custom lookup texture (strip format, e.g. 256x16).")]
            public Texture2D lut;

            [Range(0f, 1f), Tooltip("Blending factor.")]
            public float contribution;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        lut = null,
                        contribution = 1f
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }

    [Serializable]
    public class VignetteModel : PostProcessingModel
    {
        public enum Mode
        {
            Classic,
            Masked
        }

        [Serializable]
        public struct Settings
        {
            [Tooltip("Use the \"Classic\" mode for parametric controls. Use the \"Masked\" mode to use your own texture mask.")]
            public Mode mode;

            [ColorUsage(false)]
            [Tooltip("Vignette color. Use the alpha channel for transparency.")]
            public Color color;

            [Tooltip("Sets the vignette center point (screen center is [0.5,0.5]).")]
            public Vector2 center;

            [Range(0f, 1f), Tooltip("Amount of vignetting on screen.")]
            public float intensity;

            [Range(0.01f, 1f), Tooltip("Smoothness of the vignette borders.")]
            public float smoothness;

            [Range(0f, 1f), Tooltip("Lower values will make a square-ish vignette.")]
            public float roundness;

            [Tooltip("A black and white mask to use as a vignette.")]
            public Texture mask;

            [Range(0f, 1f), Tooltip("Mask opacity.")]
            public float opacity;

            [Tooltip("Should the vignette be perfectly round or be dependent on the current aspect ratio?")]
            public bool rounded;

            public static Settings defaultSettings
            {
                get
                {
                    return new Settings
                    {
                        mode = Mode.Classic,
                        color = new Color(0f, 0f, 0f, 1f),
                        center = new Vector2(0.5f, 0.5f),
                        intensity = 0.45f,
                        smoothness = 0.2f,
                        roundness = 1f,
                        mask = null,
                        opacity = 1f,
                        rounded = false
                    };
                }
            }
        }

        [SerializeField]
        Settings m_Settings = Settings.defaultSettings;
        public Settings settings
        {
            get { return m_Settings; }
            set { m_Settings = value; }
        }

        public override void Reset()
        {
            m_Settings = Settings.defaultSettings;
        }
    }
    #endregion

    #region Utils
    //Unity: Small wrapper on top of AnimationCurve to handle zero-key curves and keyframe looping

    [Serializable]
    public sealed class ColorGradingCurve
    {
        public AnimationCurve curve;

        [SerializeField]
        bool m_Loop;

        [SerializeField]
        float m_ZeroValue;

        [SerializeField]
        float m_Range;

        [SerializeField]
        Vector2 bounds;

        AnimationCurve m_InternalLoopingCurve;

        public ColorGradingCurve(AnimationCurve curve, float zeroValue, bool loop, Vector2 bounds)
        {
            this.curve = curve;
            m_ZeroValue = zeroValue;
            m_Loop = loop;
            m_Range = bounds.magnitude;
            this.bounds = bounds;
        }

        public bool IsLooped
        {
            get
            {
                return m_Loop;
            }
        }
        public float ZeroValue
        {
            get
            {
                return m_ZeroValue;
            }
        }
        public Vector2 Range
        {
            get
            {
                return bounds;
            }
        }
        public void Cache()
        {
            if (!m_Loop)
                return;

            var length = curve.length;

            if (length < 2)
                return;

            if (m_InternalLoopingCurve == null)
                m_InternalLoopingCurve = new AnimationCurve();

            var prev = curve[length - 1];
            prev.time -= m_Range;
            var next = curve[0];
            next.time += m_Range;
            m_InternalLoopingCurve.keys = curve.keys;
            m_InternalLoopingCurve.AddKey(prev);
            m_InternalLoopingCurve.AddKey(next);
        }

        public float Evaluate(float t)
        {
            if (curve.length == 0)
                return m_ZeroValue;

            if (!m_Loop || curve.length == 1)
                return curve.Evaluate(t);

            return m_InternalLoopingCurve.Evaluate(t);
        }
    }

    public static class GraphicsUtils
    {
        public static bool isLinearColorSpace
        {
            get { return QualitySettings.activeColorSpace == ColorSpace.Linear; }
        }

        public static bool supportsDX11
        {
#if UNITY_WEBGL
            get { return false; }
#else
            get { return SystemInfo.graphicsShaderLevel >= 50 && SystemInfo.supportsComputeShaders; }
#endif
        }

        static Texture2D s_WhiteTexture;
        public static Texture2D whiteTexture
        {
            get
            {
                if (s_WhiteTexture != null)
                    return s_WhiteTexture;

                s_WhiteTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                s_WhiteTexture.SetPixel(0, 0, new Color(1f, 1f, 1f, 1f));
                s_WhiteTexture.Apply();

                return s_WhiteTexture;
            }
        }

        static Mesh s_Quad;
        public static Mesh quad
        {
            get
            {
                if (s_Quad != null)
                    return s_Quad;

                var vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3( 1f,  1f, 0f),
                    new Vector3( 1f, -1f, 0f),
                    new Vector3(-1f,  1f, 0f)
                };

                var uvs = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f)
                };

                var indices = new[] { 0, 1, 2, 1, 0, 3 };

                s_Quad = new Mesh
                {
                    vertices = vertices,
                    uv = uvs,
                    triangles = indices
                };
                s_Quad.RecalculateNormals();
                s_Quad.RecalculateBounds();

                return s_Quad;
            }
        }

        // Useful when rendering to MRT
        public static void Blit(Material material, int pass)
        {
            GL.PushMatrix();
            {
                GL.LoadOrtho();

                material.SetPass(pass);

                GL.Begin(GL.TRIANGLE_STRIP);
                {
                    GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0.1f);
                    GL.TexCoord2(1f, 0f); GL.Vertex3(1f, 0f, 0.1f);
                    GL.TexCoord2(0f, 1f); GL.Vertex3(0f, 1f, 0.1f);
                    GL.TexCoord2(1f, 1f); GL.Vertex3(1f, 1f, 0.1f);
                }
                GL.End();
            }
            GL.PopMatrix();
        }

        public static void ClearAndBlit(Texture source, RenderTexture destination, Material material, int pass, bool clearColor = true, bool clearDepth = false)
        {
            var oldRT = RenderTexture.active;
            RenderTexture.active = destination;

            GL.Clear(false, clearColor, Color.clear);
            GL.PushMatrix();
            {
                GL.LoadOrtho();

                material.SetTexture("_MainTex", source);
                material.SetPass(pass);

                GL.Begin(GL.TRIANGLE_STRIP);
                {
                    GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0.1f);
                    GL.TexCoord2(1f, 0f); GL.Vertex3(1f, 0f, 0.1f);
                    GL.TexCoord2(0f, 1f); GL.Vertex3(0f, 1f, 0.1f);
                    GL.TexCoord2(1f, 1f); GL.Vertex3(1f, 1f, 0.1f);
                }
                GL.End();
            }
            GL.PopMatrix();

            RenderTexture.active = oldRT;
        }

        public static void Destroy(UnityObject obj)
        {
            if (obj != null)
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
                    UnityObject.Destroy(obj);
                else
                    UnityObject.DestroyImmediate(obj);
#else
                UnityObject.Destroy(obj);
#endif
            }
        }

        public static void Dispose()
        {
            Destroy(s_Quad);
        }
    }

    public sealed class MaterialFactory : IDisposable
    {
        Dictionary<string, Material> m_Materials;

        public MaterialFactory()
        {
            m_Materials = new Dictionary<string, Material>();
        }

        public Material Get(string shaderName)
        {
            Material material;

            if (!m_Materials.TryGetValue(shaderName, out material))
            {
                var shader = ShaderLoader.GetShader(shaderName);

                if (shader == null)
                    throw new ArgumentException(string.Format("Shader not found ({0})", shaderName));

                material = new Material(shader)
                {
                    name = string.Format("PostFX - {0}", shaderName.Substring(shaderName.LastIndexOf("/") + 1)),
                    hideFlags = HideFlags.DontSave
                };

                m_Materials.Add(shaderName, material);
            }

            return material;
        }

        public void Dispose()
        {
            var enumerator = m_Materials.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var material = enumerator.Current.Value;
                GraphicsUtils.Destroy(material);
            }

            m_Materials.Clear();
        }
    }

    public sealed class RenderTextureFactory : IDisposable
    {
        HashSet<RenderTexture> m_TemporaryRTs;

        public RenderTextureFactory()
        {
            m_TemporaryRTs = new HashSet<RenderTexture>();
        }

        public RenderTexture Get(RenderTexture baseRenderTexture)
        {
            return Get(
                baseRenderTexture.width,
                baseRenderTexture.height,
                baseRenderTexture.depth,
                baseRenderTexture.format,
                baseRenderTexture.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear,
                baseRenderTexture.filterMode,
                baseRenderTexture.wrapMode
                );
        }

        public RenderTexture Get(int width, int height, int depthBuffer = 0, RenderTextureFormat format = RenderTextureFormat.ARGBHalf, RenderTextureReadWrite rw = RenderTextureReadWrite.Default, FilterMode filterMode = FilterMode.Bilinear, TextureWrapMode wrapMode = TextureWrapMode.Clamp, string name = "FactoryTempTexture")
        {
            var rt = RenderTexture.GetTemporary(width, height, depthBuffer, format, rw); // add forgotten param rw
            rt.filterMode = filterMode;
            rt.wrapMode = wrapMode;
            rt.name = name;
            m_TemporaryRTs.Add(rt);
            return rt;
        }

        public void Release(RenderTexture rt)
        {
            if (rt == null)
                return;

            if (!m_TemporaryRTs.Contains(rt))
                throw new ArgumentException(string.Format("Attempting to remove a RenderTexture that was not allocated: {0}", rt));

            m_TemporaryRTs.Remove(rt);
            RenderTexture.ReleaseTemporary(rt);
        }

        public void ReleaseAll()
        {
            var enumerator = m_TemporaryRTs.GetEnumerator();
            while (enumerator.MoveNext())
                RenderTexture.ReleaseTemporary(enumerator.Current);

            m_TemporaryRTs.Clear();
        }

        public void Dispose()
        {
            ReleaseAll();
        }
    }
    #endregion


    public class PostProcessingProfile : ScriptableObject
    {
#pragma warning disable 0169 // "field x is never used"

        public BuiltinDebugViewsModel debugViews = new BuiltinDebugViewsModel();
        public FogModel fog = new FogModel();
        public AntialiasingModel antialiasing = new AntialiasingModel();
        public AmbientOcclusionModel ambientOcclusion = new AmbientOcclusionModel();
        public ScreenSpaceReflectionModel screenSpaceReflection = new ScreenSpaceReflectionModel();
        public DepthOfFieldModel depthOfField = new DepthOfFieldModel();
        public MotionBlurModel motionBlur = new MotionBlurModel();
        public EyeAdaptationModel eyeAdaptation = new EyeAdaptationModel();
        public BloomModel bloom = new BloomModel();
        public ColorGradingModel colorGrading = new ColorGradingModel();
        public UserLutModel userLut = new UserLutModel();
        public ChromaticAberrationModel chromaticAberration = new ChromaticAberrationModel();
        public GrainModel grain = new GrainModel();
        public VignetteModel vignette = new VignetteModel();
        public DitheringModel dithering = new DitheringModel();

#if UNITY_EDITOR
        // Monitor settings
        [Serializable]
        public class MonitorSettings
        {
            // Callback used in the editor to grab the rendered frame and sent it to monitors
            public Action<RenderTexture> onFrameEndEditorOnly;

            // Global
            public int currentMonitorID = 0;
            public bool refreshOnPlay = false;

            // Histogram
            public enum HistogramMode
            {
                Red = 0,
                Green = 1,
                Blue = 2,
                Luminance = 3,
                RGBMerged,
                RGBSplit
            }

            public HistogramMode histogramMode = HistogramMode.Luminance;

            // Waveform
            public float waveformExposure = 0.12f;
            public bool waveformY = false;
            public bool waveformR = true;
            public bool waveformG = true;
            public bool waveformB = true;

            // Parade
            public float paradeExposure = 0.12f;

            // Vectorscope
            public float vectorscopeExposure = 0.12f;
            public bool vectorscopeShowBackground = true;
        }

        public MonitorSettings monitors = new MonitorSettings();
#endif
    }

    [Serializable]
    public abstract class PostProcessingModel
    {
        [SerializeField, GetSet("enabled")]
        bool m_Enabled;
        public bool enabled
        {
            get { return m_Enabled; }
            set
            {
                m_Enabled = value;

                if (value)
                    OnValidate();
            }
        }

        public abstract void Reset();

        public virtual void OnValidate()
        { }
    }

    public class PostProcessingContext
    {
        public PostProcessingProfile profile;
        public Camera camera;

        public MaterialFactory materialFactory;
        public RenderTextureFactory renderTextureFactory;

        public bool interrupted { get; private set; }

        public void Interrupt()
        {
            interrupted = true;
        }

        public PostProcessingContext Reset()
        {
            profile = null;
            camera = null;
            materialFactory = null;
            renderTextureFactory = null;
            interrupted = false;
            return this;
        }

        #region Helpers
        public bool isGBufferAvailable
        {
            get { return camera.actualRenderingPath == RenderingPath.DeferredShading; }
        }

        public bool isHdr
        {
            // No UNITY_5_6_OR_NEWER defined in early betas of 5.6
#if UNITY_5_6 || UNITY_5_6_OR_NEWER
            get { return camera.allowHDR; }
#else
            get { return camera.hdr; }
#endif
        }

        public int width
        {
            get { return camera.pixelWidth; }
        }

        public int height
        {
            get { return camera.pixelHeight; }
        }

        public Rect viewport
        {
            get { return camera.rect; } // Normalized coordinates
        }
        #endregion
    }

    public abstract class PostProcessingComponentBase
    {
        public PostProcessingContext context;

        public virtual DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.None;
        }

        public abstract bool active { get; }

        public virtual void OnEnable()
        { }

        public virtual void OnDisable()
        { }

        public abstract PostProcessingModel GetModel();
    }

    public abstract class PostProcessingComponent<T> : PostProcessingComponentBase
        where T : PostProcessingModel
    {
        public T model { get; internal set; }

        public virtual void Init(PostProcessingContext pcontext, T pmodel)
        {
            context = pcontext;
            model = pmodel;
        }

        public override PostProcessingModel GetModel()
        {
            return model;
        }
    }

    public abstract class PostProcessingComponentCommandBuffer<T> : PostProcessingComponent<T>
        where T : PostProcessingModel
    {
        public abstract CameraEvent GetCameraEvent();

        public abstract string GetName();

        public abstract void PopulateCommandBuffer(CommandBuffer cb);
    }

    public abstract class PostProcessingComponentRenderTexture<T> : PostProcessingComponent<T>
        where T : PostProcessingModel
    {
        public virtual void Prepare(Material material)
        { }
    }

#if UNITY_5_4_OR_NEWER
    [ImageEffectAllowedInSceneView]
#endif
    [RequireComponent(typeof(Camera)), DisallowMultipleComponent, ExecuteInEditMode]
    [AddComponentMenu("Effects/Post-Processing Behaviour", -1)]
    public class PostProcessingBehaviour : MonoBehaviour
    {
        // Inspector fields
        public PostProcessingProfile profile;

        public Func<Vector2, Matrix4x4> jitteredMatrixFunc;

        // Internal helpers
        Dictionary<Type, KeyValuePair<CameraEvent, CommandBuffer>> m_CommandBuffers;
        List<PostProcessingComponentBase> m_Components;
        Dictionary<PostProcessingComponentBase, bool> m_ComponentStates;

        MaterialFactory m_MaterialFactory;
        RenderTextureFactory m_RenderTextureFactory;
        PostProcessingContext m_Context;
        Camera m_Camera;
        PostProcessingProfile m_PreviousProfile;

        bool m_RenderingInSceneView = false;

        // Effect components
        BuiltinDebugViewsComponent m_DebugViews;
        AmbientOcclusionComponent m_AmbientOcclusion;
        ScreenSpaceReflectionComponent m_ScreenSpaceReflection;
        FogComponent m_FogComponent;
        MotionBlurComponent m_MotionBlur;
        TaaComponent m_Taa;
        EyeAdaptationComponent m_EyeAdaptation;
        DepthOfFieldComponent m_DepthOfField;
        BloomComponent m_Bloom;
        ChromaticAberrationComponent m_ChromaticAberration;
        ColorGradingComponent m_ColorGrading;
        UserLutComponent m_UserLut;
        GrainComponent m_Grain;
        VignetteComponent m_Vignette;
        DitheringComponent m_Dithering;
        FxaaComponent m_Fxaa;

        void OnEnable()
        {
            m_CommandBuffers = new Dictionary<Type, KeyValuePair<CameraEvent, CommandBuffer>>();
            m_MaterialFactory = new MaterialFactory();
            m_RenderTextureFactory = new RenderTextureFactory();
            m_Context = new PostProcessingContext();

            // Keep a list of all post-fx for automation purposes
            m_Components = new List<PostProcessingComponentBase>();

            // Component list
            m_DebugViews = AddComponent(new BuiltinDebugViewsComponent());
            m_AmbientOcclusion = AddComponent(new AmbientOcclusionComponent());
            m_ScreenSpaceReflection = AddComponent(new ScreenSpaceReflectionComponent());
            m_FogComponent = AddComponent(new FogComponent());
            m_MotionBlur = AddComponent(new MotionBlurComponent());
            m_Taa = AddComponent(new TaaComponent());
            m_EyeAdaptation = AddComponent(new EyeAdaptationComponent());
            m_DepthOfField = AddComponent(new DepthOfFieldComponent());
            m_Bloom = AddComponent(new BloomComponent());
            m_ChromaticAberration = AddComponent(new ChromaticAberrationComponent());
            m_ColorGrading = AddComponent(new ColorGradingComponent());
            m_UserLut = AddComponent(new UserLutComponent());
            m_Grain = AddComponent(new GrainComponent());
            m_Vignette = AddComponent(new VignetteComponent());
            m_Dithering = AddComponent(new DitheringComponent());
            m_Fxaa = AddComponent(new FxaaComponent());

            // Prepare state observers
            m_ComponentStates = new Dictionary<PostProcessingComponentBase, bool>();

            foreach (var component in m_Components)
                m_ComponentStates.Add(component, false);

            useGUILayout = false;
        }

        void OnPreCull()
        {
            // All the per-frame initialization logic has to be done in OnPreCull instead of Update
            // because [ImageEffectAllowedInSceneView] doesn't trigger Update events...

            m_Camera = GetComponent<Camera>();

            if (profile == null || m_Camera == null)
                return;

#if UNITY_EDITOR
            // Track the scene view camera to disable some effects we don't want to see in the
            // scene view
            // Currently disabled effects :
            //  - Temporal Antialiasing
            //  - Depth of Field
            //  - Motion blur
            m_RenderingInSceneView = UnityEditor.SceneView.currentDrawingSceneView != null
                && UnityEditor.SceneView.currentDrawingSceneView.camera == m_Camera;
#endif

            // Prepare context
            var context = m_Context.Reset();
            context.profile = profile;
            context.renderTextureFactory = m_RenderTextureFactory;
            context.materialFactory = m_MaterialFactory;
            context.camera = m_Camera;

            // Prepare components
            m_DebugViews.Init(context, profile.debugViews);
            m_AmbientOcclusion.Init(context, profile.ambientOcclusion);
            m_ScreenSpaceReflection.Init(context, profile.screenSpaceReflection);
            m_FogComponent.Init(context, profile.fog);
            m_MotionBlur.Init(context, profile.motionBlur);
            m_Taa.Init(context, profile.antialiasing);
            m_EyeAdaptation.Init(context, profile.eyeAdaptation);
            m_DepthOfField.Init(context, profile.depthOfField);
            m_Bloom.Init(context, profile.bloom);
            m_ChromaticAberration.Init(context, profile.chromaticAberration);
            m_ColorGrading.Init(context, profile.colorGrading);
            m_UserLut.Init(context, profile.userLut);
            m_Grain.Init(context, profile.grain);
            m_Vignette.Init(context, profile.vignette);
            m_Dithering.Init(context, profile.dithering);
            m_Fxaa.Init(context, profile.antialiasing);

            // Handles profile change and 'enable' state observers
            if (m_PreviousProfile != profile)
            {
                DisableComponents();
                m_PreviousProfile = profile;
            }

            CheckObservers();

            // Find out which camera flags are needed before rendering begins
            // Note that motion vectors will only be available one frame after being enabled
            var flags = context.camera.depthTextureMode;
            foreach (var component in m_Components)
            {
                if (component.active)
                    flags |= component.GetCameraFlags();
            }

            context.camera.depthTextureMode = flags;

            // Temporal antialiasing jittering, needs to happen before culling
            if (!m_RenderingInSceneView && m_Taa.active && !profile.debugViews.willInterrupt)
                m_Taa.SetProjectionMatrix(jitteredMatrixFunc);
        }

        void OnPreRender()
        {
            if (profile == null)
                return;

            // Command buffer-based effects should be set-up here
            TryExecuteCommandBuffer(m_DebugViews);
            TryExecuteCommandBuffer(m_AmbientOcclusion);
            TryExecuteCommandBuffer(m_ScreenSpaceReflection);
            TryExecuteCommandBuffer(m_FogComponent);

            if (!m_RenderingInSceneView)
                TryExecuteCommandBuffer(m_MotionBlur);
        }

        void OnPostRender()
        {
            if (profile == null || m_Camera == null)
                return;

            if (!m_RenderingInSceneView && m_Taa.active && !profile.debugViews.willInterrupt)
                m_Context.camera.ResetProjectionMatrix();
        }

        // Classic render target pipeline for RT-based effects
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (profile == null || m_Camera == null)
            {
                KS3PUtil.Blit(source, destination);
                return;
            }

            // Uber shader setup
            bool uberActive = false;
            bool fxaaActive = m_Fxaa.active;
            bool taaActive = m_Taa.active && !m_RenderingInSceneView;
            bool dofActive = m_DepthOfField.active && !m_RenderingInSceneView;

            var uberMaterial = m_MaterialFactory.Get("Hidden/Post FX/Uber Shader");
            uberMaterial.shaderKeywords = null;

            var src = source;
            var dst = destination;

            if (taaActive)
            {
                var tempRT = m_RenderTextureFactory.Get(src);
                m_Taa.Render(src, tempRT);
                src = tempRT;
            }

#if UNITY_EDITOR
            // Render to a dedicated target when monitors are enabled so they can show information
            // about the final render.
            // At runtime the output will always be the backbuffer or whatever render target is
            // currently set on the camera.
            if (profile.monitors.onFrameEndEditorOnly != null)
                dst = m_RenderTextureFactory.Get(src);
#endif

            Texture autoExposure = GraphicsUtils.whiteTexture;
            if (m_EyeAdaptation.active)
            {
                uberActive = true;
                autoExposure = m_EyeAdaptation.Prepare(src, uberMaterial);
            }

            uberMaterial.SetTexture("_AutoExposure", autoExposure);

            if (dofActive)
            {
                uberActive = true;
                m_DepthOfField.Prepare(src, uberMaterial, taaActive, m_Taa.jitterVector, m_Taa.model.settings.taaSettings.motionBlending);
            }

            if (m_Bloom.active)
            {
                uberActive = true;
                m_Bloom.Prepare(src, uberMaterial, autoExposure);
            }

            uberActive |= TryPrepareUberImageEffect(m_ChromaticAberration, uberMaterial);
            uberActive |= TryPrepareUberImageEffect(m_ColorGrading, uberMaterial);
            uberActive |= TryPrepareUberImageEffect(m_Vignette, uberMaterial);
            uberActive |= TryPrepareUberImageEffect(m_UserLut, uberMaterial);

            var fxaaMaterial = fxaaActive
                ? m_MaterialFactory.Get("Hidden/Post FX/FXAA")
                : null;

            if (fxaaActive)
            {
                fxaaMaterial.shaderKeywords = null;
                TryPrepareUberImageEffect(m_Grain, fxaaMaterial);
                TryPrepareUberImageEffect(m_Dithering, fxaaMaterial);

                if (uberActive)
                {
                    var output = m_RenderTextureFactory.Get(src);
                    KS3PUtil.Blit(src, output, uberMaterial, 0);
                    src = output;
                }

                m_Fxaa.Render(src, dst);
            }
            else
            {
                uberActive |= TryPrepareUberImageEffect(m_Grain, uberMaterial);
                uberActive |= TryPrepareUberImageEffect(m_Dithering, uberMaterial);

                if (uberActive)
                {
                    if (!GraphicsUtils.isLinearColorSpace)
                        uberMaterial.EnableKeyword("UNITY_COLORSPACE_GAMMA");

                    KS3PUtil.Blit(src, dst, uberMaterial, 0);
                }
            }

            if (!uberActive && !fxaaActive)
                KS3PUtil.Blit(src, dst);

#if UNITY_EDITOR
            if (profile.monitors.onFrameEndEditorOnly != null)
            {
                KS3PUtil.Blit(dst, destination);

                var oldRt = RenderTexture.active;
                profile.monitors.onFrameEndEditorOnly(dst);
                RenderTexture.active = oldRt;
            }
#endif

            m_RenderTextureFactory.ReleaseAll();
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (profile == null || m_Camera == null)
                return;

            if (m_EyeAdaptation.active && profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.EyeAdaptation))
                m_EyeAdaptation.OnGUI();
            else if (m_ColorGrading.active && profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.LogLut))
                m_ColorGrading.OnGUI();
            else if (m_UserLut.active && profile.debugViews.IsModeActive(BuiltinDebugViewsModel.Mode.UserLut))
                m_UserLut.OnGUI();
        }

        void OnDisable()
        {
            // Clear command buffers
            foreach (var cb in m_CommandBuffers.Values)
            {
                m_Camera.RemoveCommandBuffer(cb.Key, cb.Value);
                cb.Value.Dispose();
            }

            m_CommandBuffers.Clear();

            // Clear components
            if (profile != null)
                DisableComponents();

            m_Components.Clear();

            // Factories
            m_MaterialFactory.Dispose();
            m_RenderTextureFactory.Dispose();
            GraphicsUtils.Dispose();
        }

        public void ResetTemporalEffects()
        {
            m_Taa.ResetHistory();
            m_MotionBlur.ResetHistory();
            m_EyeAdaptation.ResetHistory();
        }

        #region State management

        List<PostProcessingComponentBase> m_ComponentsToEnable = new List<PostProcessingComponentBase>();
        List<PostProcessingComponentBase> m_ComponentsToDisable = new List<PostProcessingComponentBase>();

        void CheckObservers()
        {
            foreach (var cs in m_ComponentStates)
            {
                var component = cs.Key;
                var state = component.GetModel().enabled;

                if (state != cs.Value)
                {
                    if (state) m_ComponentsToEnable.Add(component);
                    else m_ComponentsToDisable.Add(component);
                }
            }

            for (int i = 0; i < m_ComponentsToDisable.Count; i++)
            {
                var c = m_ComponentsToDisable[i];
                m_ComponentStates[c] = false;
                c.OnDisable();
            }

            for (int i = 0; i < m_ComponentsToEnable.Count; i++)
            {
                var c = m_ComponentsToEnable[i];
                m_ComponentStates[c] = true;
                c.OnEnable();
            }

            m_ComponentsToDisable.Clear();
            m_ComponentsToEnable.Clear();
        }

        void DisableComponents()
        {
            foreach (var component in m_Components)
            {
                var model = component.GetModel();
                if (model != null && model.enabled)
                    component.OnDisable();
            }
        }

        #endregion

        #region Command buffer handling & rendering helpers
        // Placeholders before the upcoming Scriptable Render Loop as command buffers will be
        // executed on the go so we won't need of all that stuff
        CommandBuffer AddCommandBuffer<T>(CameraEvent evt, string name)
            where T : PostProcessingModel
        {
            var cb = new CommandBuffer { name = name };
            var kvp = new KeyValuePair<CameraEvent, CommandBuffer>(evt, cb);
            m_CommandBuffers.Add(typeof(T), kvp);
            m_Camera.AddCommandBuffer(evt, kvp.Value);
            return kvp.Value;
        }

        void RemoveCommandBuffer<T>()
            where T : PostProcessingModel
        {
            KeyValuePair<CameraEvent, CommandBuffer> kvp;
            var type = typeof(T);

            if (!m_CommandBuffers.TryGetValue(type, out kvp))
                return;

            m_Camera.RemoveCommandBuffer(kvp.Key, kvp.Value);
            m_CommandBuffers.Remove(type);
            kvp.Value.Dispose();
        }

        CommandBuffer GetCommandBuffer<T>(CameraEvent evt, string name)
            where T : PostProcessingModel
        {
            CommandBuffer cb;
            KeyValuePair<CameraEvent, CommandBuffer> kvp;

            if (!m_CommandBuffers.TryGetValue(typeof(T), out kvp))
            {
                cb = AddCommandBuffer<T>(evt, name);
            }
            else if (kvp.Key != evt)
            {
                RemoveCommandBuffer<T>();
                cb = AddCommandBuffer<T>(evt, name);
            }
            else cb = kvp.Value;

            return cb;
        }

        void TryExecuteCommandBuffer<T>(PostProcessingComponentCommandBuffer<T> component)
            where T : PostProcessingModel
        {
            if (component.active)
            {
                var cb = GetCommandBuffer<T>(component.GetCameraEvent(), component.GetName());
                cb.Clear();
                component.PopulateCommandBuffer(cb);
            }
            else RemoveCommandBuffer<T>();
        }

        bool TryPrepareUberImageEffect<T>(PostProcessingComponentRenderTexture<T> component, Material material)
            where T : PostProcessingModel
        {
            if (!component.active)
                return false;

            component.Prepare(material);
            return true;
        }

        T AddComponent<T>(T component)
            where T : PostProcessingComponentBase
        {
            m_Components.Add(component);
            return component;
        }

        #endregion
    }
    #endregion
}