using UnityEngine;
//using KSP_PostProcessing.Effects;

/*

namespace KSP_PostProcessing.Effects
{
    public sealed class FineControl : IAttachable
    {
        public struct Settings
        {
            #region CameraSettings
            public bool isDeferred;
            public bool allowHDR;
            public bool allowMSAA;
            public float fov;
            public float nearClipPlane;
            #endregion

            #region QualitySettings
            public float shadowDistance;
            public ShadowQuality shadowQuality;
            public ShadowResolution shadowResolution;
            #endregion

            public Settings(Camera cam)
            {
                isDeferred = cam.actualRenderingPath == RenderingPath.DeferredShading || cam.actualRenderingPath == RenderingPath.DeferredLighting;
                fov = cam.fieldOfView;
                allowHDR = cam.allowHDR;
                allowMSAA = cam.allowMSAA;
                nearClipPlane = cam.nearClipPlane;
                shadowDistance = QualitySettings.shadowDistance;
                shadowQuality = QualitySettings.shadows;
                shadowResolution = QualitySettings.shadowResolution;
            }
            public void Patch(Camera cam)
            {
                cam.renderingPath = (isDeferred) ? RenderingPath.DeferredShading : RenderingPath.Forward;
                cam.allowHDR = allowHDR;
                cam.allowMSAA = allowMSAA;
                cam.fieldOfView = fov;
                cam.nearClipPlane = nearClipPlane;
                QualitySettings.shadowDistance = shadowDistance;
                QualitySettings.shadows = shadowQuality;
                QualitySettings.shadowResolution = shadowResolution;
            }
        }

        public Settings defaultSettings;
        public Settings targetSettings;

        public Camera cam = null;

        public void OnAdd()
        {
            //cam = KS3P.Instance.TargetCamera;
            if(cam) { targetSettings.Patch(cam); }
        }
        public void OnRemove()
        {
            if(cam) { defaultSettings.Patch(cam); }
        }
    }
}

namespace KSP_PostProcessing.Parsers
{
    public sealed class FineControlParser : Parser<FineControl>
    {
        protected override FineControl Default()
        {
            //Camera target = KS3P.Instance.TargetCamera;
            var settings = new FineControl.Settings(target);
            return new FineControl()
            {
                defaultSettings = settings,
                targetSettings = settings
            };
        }

        // create parser!
        protected override FineControl Parse(ConfigNode.ValueList values)
        {
            FineControl control = Default();
            FineControl.Settings settings = control.targetSettings;
            bool calcBool;
            float calcFloat;
            string[] data =
            {
                "hdr",              // 0
                "msaa",             // 1
                "fov",              // 2
                "forcedeferred",    // 3
                "nearclipplane",    // 4
                "shadowdistance",   // 5
                "shadowquality",    // 6
                "shadowresolution", // 7
                "enabled"           // 8
            };
            ProcessStream(values, ref data);
            if(data[0] != null && bool.TryParse(data[0], out calcBool))
            {
                settings.allowHDR = calcBool;
            }
            if(data[1] != null && bool.TryParse(data[1], out calcBool))
            {
                settings.allowMSAA = calcBool;
            }
            if(data[2] != null && float.TryParse(data[2], out calcFloat))
            {
                settings.fov = calcFloat;
            }
            if(data[3] != null && bool.TryParse(data[3], out calcBool))
            {
                settings.isDeferred = calcBool;
            }
            if(data[4] != null && float.TryParse(data[4], out calcFloat))
            {
                settings.nearClipPlane = calcFloat;
            }
            if(data[5] != null && float.TryParse(data[5], out calcFloat))
            {
                settings.shadowDistance = calcFloat;
            }
            if(data[6] != null)
            {
                ShadowQuality quality;
                if(TryParseEnum(data[6], out quality))
                {
                    settings.shadowQuality = quality;
                }
            }
            if(data[7] != null)
            {
                ShadowResolution resolution;
                if(TryParseEnum(data[7], out resolution))
                {
                    settings.shadowResolution = resolution;
                }
            }
            if(data[8] == null || !bool.TryParse(data[8], out calcBool))
            {
                calcBool = true;
            }

            control.targetSettings = settings;
            return control;
        }
    }
}

*/