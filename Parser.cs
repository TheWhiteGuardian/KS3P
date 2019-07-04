using System;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace KSP_PostProcessing.Parsers
{
    /// <summary>
    /// The base class for all PostProcessingModel parsers
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal abstract class Parser<T> where T : PostProcessingModel
    {
        protected void Warning(string message)
        {
            KS3P.Error("[" + nameof(T) + "]: message");
        }
        protected void Exception(string message, Exception exception)
        {
            KS3P.Exception("[" + nameof(T) + "]: Exception caught while parsing!", exception);
        }
        protected abstract T Default();
        protected virtual T Parse(ConfigNode.ValueList values) { return Default(); }
        internal virtual T Parse(ConfigNode node)
        {
            if(node == null)
            {
                return Default();
            }
            else
            {
                return Parse(node.values);
            }
        }
        internal bool TryParseEnum<E>(string enumString, out E parsed) where E : struct
        {
            if(string.IsNullOrEmpty(enumString))
            {
                Warning("Error parsing enumerator [" + nameof(E) + "]: given string that is empty or null!");
                parsed = default(E);
                return false;
            }
            try
            {
                parsed = (E)Enum.Parse(typeof(E), enumString);
                return true;
            }
            catch(Exception e)
            {
                Exception("Exception caught parsing enumerator [" + nameof(E) + "]", e);
                parsed = default(E);
                return false;
            }
        }
        
        protected void ProcessStream(ConfigNode.ValueList valueStream, ref string[] values)
        {
            // for processing
            string[] data = new string[values.Length];
            string formatted;
            int i;

            for(i = 0; i < data.Length; i++)
            {
                data[i] = null;
            }

            // process all values
            foreach(ConfigNode.Value value in valueStream)
            {
                // format this value's name
                formatted = KS3PUtil.Prepare(value.name);
                
                // does our list of requested values contain this name?
                for(i = 0; i < values.Length; i++)
                {
                    // check
                    if(values[i] == formatted)
                    {
                        // we got one. Add it to the dictionary and terminate the loop.
                        data[i] = value.value;
                        break;
                    }
                }
            }
            values = data;
        }

        protected Vector2 ParseVector2(string target)
        {
            float[] data = { 0f, 0f };
            char[] separator = { ',' };

            string[] snippets = target.Split(separator, 2);
            float parsed = 0f;
            for (int i = 0; i < snippets.Length; i++)
            {
                if (float.TryParse(snippets[i], out parsed))
                {
                    data[i] = parsed;
                }
            }
            return new Vector2(data[0], data[1]);
        }
        protected Vector3 ParseVector3(string target)
        {
            float[] data = { 0f, 0f, 0f };
            char[] separator = { ',' };

            string[] snippets = target.Split(separator, 3);
            float parsed = 0f;
            for(int i = 0; i < snippets.Length; i++)
            {
                if(float.TryParse(snippets[i], out parsed))
                {
                    data[i] = parsed;
                }
            }
            return new Vector3(data[0], data[1], data[2]);
        }
        protected Color ParseColor(string target)
        {
            if(target.StartsWith("#"))
            {
                Color parsedHTML;
                if(!ColorUtility.TryParseHtmlString(target, out parsedHTML))
                {
                    parsedHTML = new Color(0f, 0f, 0f, 1f);
                }
                return parsedHTML;
            }
            else
            {
                float[] data = { 0f, 0f, 0f, 1f };
                char[] separator = { ',' };

                string[] snippets = target.Split(separator, 3);
                float parsed = 0f;

                if (target.StartsWith("RGBA"))
                {
                    for (int i = 0; i < snippets.Length; i++)
                    {
                        if (float.TryParse(snippets[i], out parsed))
                        {
                            data[i] = parsed / 255f;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < snippets.Length; i++)
                    {
                        if (float.TryParse(snippets[i], out parsed))
                        {
                            data[i] = parsed;
                        }
                    }
                }
                return new Color(data[0], data[1], data[2], data[3]);
            }
        }
        
        protected bool TryParseCurve(ConfigNode node, out ColorGradingCurve curve)
        {
            if(node == null)
            {
                curve = null;
                return false;
            }
            else
            {
                curve = ParseCurve(node);
                return true;
            }
        }

        protected ColorGradingCurve ParseCurve(ConfigNode node)
        {
            float zero = 0f;
            bool loop = false;
            AnimationCurve curve = new AnimationCurve();
            foreach(ConfigNode.Value value in node.values)
            {
                if(KS3PUtil.Prepare(value.name) == "key")
                {
                    curve.TryAdd(value.value);
                }
                else if(KS3PUtil.Prepare(value.name) == "zero")
                {
                    if(!float.TryParse(value.value, out zero))
                    {
                        zero = 0f;
                    }
                }
                else if(KS3PUtil.Prepare(value.name) == "loop")
                {
                    if(!bool.TryParse(value.value, out loop))
                    {
                        loop = false;
                    }
                }
            }
            return new ColorGradingCurve(curve, zero, loop, curve.GetBounds());
        }

        internal abstract void ToFile(List<string> lines, T item);
    }

    /// <summary>
    /// A slight addition to the base Parser class that contains an out parameter for returning the strings assigned to texture paths.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal abstract class TextureParser<T> : Parser<T> where T : PostProcessingModel
    {
        internal virtual T Parse(ConfigNode node, out string path)
        {
            if (node == null)
            {
                path = null;
                return Default();
            }
            else
            {
                return Parse(node.values, out path);
            }
        }
        protected virtual T Parse(ConfigNode.ValueList values, out string path)
        {
            path = null;
            return Default();
        }
    }

    internal static class MiscParser
    {
        internal static bool TryParseEnum<E>(string enumString, out E parsed) where E : struct
        {
            if (string.IsNullOrEmpty(enumString))
            {
                KS3P.Warning("Error parsing enumerator [" + nameof(E) + "]: given string that is empty or null!");
                parsed = default(E);
                return false;
            }
            try
            {
                parsed = (E)Enum.Parse(typeof(E), enumString);
                return true;
            }
            catch (Exception e)
            {
                KS3P.Exception("Exception caught parsing enumerator [" + nameof(E) + "]", e);
                parsed = default(E);
                return false;
            }
        }

        internal static BitArray ParseSceneList(string input)
        {
            char[] separator = { ',' };
            string[] parts = input.Split(separator);

            BitArray toreturn = new BitArray(9, false);

            string[] keywords =
            {
                "mainmenu",
                "spacecenter",
                "vab",
                "sph",
                "trackingstation",
                "flight",
                "eva",
                "iva",
                "mapview"
            };

            if(parts.Length == 0)
            {
                return toreturn;
            }
            else
            {
                try
                {
                    for(int i = 0; i < parts.Length; i++)
                    {
                        parts[i] = KS3PUtil.Prepare(parts[i]);
                        if(keywords.Contains(parts[i]))
                        {
                            toreturn[keywords.IndexOf(parts[i])] = true;
                        }
                    }
                    return toreturn;
                }
                catch(Exception e)
                {
                    KS3P.Exception("[MiscParser]: Exception caught while parsing target scenes! Setting to Disable!", e);
                    return toreturn;
                }
            }
        }
    }
    
    internal sealed class AntiAliasingParser : Parser<AntialiasingModel>
    {
        protected override AntialiasingModel Default()
        {
            return new AntialiasingModel()
            {
                enabled = false,
                settings = AntialiasingModel.Settings.defaultSettings
            };
        }

        protected override AntialiasingModel Parse(ConfigNode.ValueList values)
        {
            AntialiasingModel.Settings settings = AntialiasingModel.Settings.defaultSettings;
            AntialiasingModel.TaaSettings taaSettings = settings.taaSettings;

            string[] data = {
              "method",
              "fxaapreset",
              "jitterspread",
              "sharpen",
              "motionblending",
              "stationaryblending",
              "enabled" };
            ProcessStream(values, ref data);

            // parse target AA method
            if(data[0] != null)
            {
                AntialiasingModel.Method method;
                if (TryParseEnum(data[0], out method))
                {
                    settings.method = method;
                }
            }

            // parse Fast Approximate Anti Aliasing
            if(data[1] != null)
            {
                AntialiasingModel.FxaaPreset fxaaPreset;
                if (TryParseEnum(data[1], out fxaaPreset))
                {
                    settings.fxaaSettings = new AntialiasingModel.FxaaSettings()
                    {
                        preset = fxaaPreset
                    };
                }
            }

            // parse Temporal Anti Aliasing
            float calc; // for handling floats
            if(data[2] != null && float.TryParse(data[2], out calc))
            {
                taaSettings.jitterSpread = calc;
            }

            if(data[4] != null && float.TryParse(data[4], out calc))
            {
                taaSettings.motionBlending = calc;
            }

            if(data[3] != null && float.TryParse(data[4], out calc))
            {
                taaSettings.sharpen = calc;
            }

            if(data[5] != null && float.TryParse(data[5], out calc))
            {
                taaSettings.stationaryBlending = calc;
            }

            settings.taaSettings = taaSettings;
            bool aaEnabled;
            if(data[6] == null || !bool.TryParse(data[6], out aaEnabled))
            {
                aaEnabled = true;
            }
            return new AntialiasingModel()
            {
                enabled = aaEnabled,
                settings = settings
            };
        }

        internal override void ToFile(List<string> lines, AntialiasingModel item)
        {
            lines.AddIndented("Anti_Aliasing");
            lines.AddIndented(true);
            // unpack here, so we can abuse how classes are passed to methods and how structs are stored in memory.
            var settings = item.settings;
            var defaultSettings = AntialiasingModel.Settings.defaultSettings;
            lines.AddIndented("method", settings.method, defaultSettings.method);
            switch(settings.method)
            {
                case AntialiasingModel.Method.Fxaa:
                    lines.AddIndented("fxaa_preset", settings.fxaaSettings.preset, defaultSettings.fxaaSettings.preset);
                    lines.AddIndented("enabled", item.enabled, true);
                    break;

                case AntialiasingModel.Method.Taa:
                    var taa = settings.taaSettings;
                    var defTaa = defaultSettings.taaSettings;
                    lines.AddIndented("jitter_spread", taa.jitterSpread, defTaa.jitterSpread);
                    lines.AddIndented("sharpen", taa.sharpen, defTaa.sharpen);
                    lines.AddIndented("motion_blending", taa.motionBlending, defTaa.motionBlending);
                    lines.AddIndented("stationary_blending", taa.stationaryBlending, defTaa.stationaryBlending);
                    lines.AddIndented("enabled", item.enabled, true);
                    break;

                default:
                    lines.AddIndented("enabled = false");
                    break;
            }

            lines.AddIndented(false);
        }
    }

    internal sealed class AmbientOcclusionParser : Parser<AmbientOcclusionModel>
    {
        protected override AmbientOcclusionModel Default()
        {
            return new AmbientOcclusionModel()
            {
                enabled = false,
                settings = AmbientOcclusionModel.Settings.defaultSettings
            };
        }

        protected override AmbientOcclusionModel Parse(ConfigNode.ValueList values)
        {
            AmbientOcclusionModel.Settings settings = AmbientOcclusionModel.Settings.defaultSettings;
            bool calcBool;
            float calcFloat;

            string[] data = {
                "ambientonly",
                "downsampling",
                "forceforwardcompatibility",
                "highprecision",
                "intensity",
                "radius",
                "enabled",
                "samplecount"
            };

            ProcessStream(values, ref data);

            if(data[0] != null && bool.TryParse(data[0], out calcBool))
            {
                settings.ambientOnly = calcBool;
            }
            if(data[1] != null && bool.TryParse(data[1], out calcBool))
            {
                settings.downsampling = calcBool;
            }
            if(data[2] != null && bool.TryParse(data[2], out calcBool))
            {
                settings.forceForwardCompatibility = calcBool;
            }
            if(data[3] != null && bool.TryParse(data[3], out calcBool))
            {
                settings.highPrecision = calcBool;
            }
            if(data[4] != null && float.TryParse(data[4], out calcFloat))
            {
                settings.intensity = calcFloat;
            }
            if(data[5] != null && float.TryParse(data[5], out calcFloat))
            {
                settings.radius = calcFloat;
            }

            if(data[6] == null || !bool.TryParse(data[6], out calcBool))
            {
                calcBool = true;
            }

            AmbientOcclusionModel.SampleCount sampleCount;
            if(data[7] != null && TryParseEnum(data[7], out sampleCount))
            {
                settings.sampleCount = sampleCount;
            }

            return new AmbientOcclusionModel()
            {
                enabled = calcBool,
                settings = settings
            };
        }
        
        internal override void ToFile(List<string> lines, AmbientOcclusionModel item)
        {
            lines.AddIndented("Ambient_Occlusion");
            lines.AddIndented(true);

            var settings = item.settings;
            var defSet = AmbientOcclusionModel.Settings.defaultSettings;
            lines.AddIndented("ambient_only", settings.ambientOnly, defSet.ambientOnly);
            lines.AddIndented("downsampling", settings.downsampling, defSet.downsampling);
            lines.AddIndented("force_forward_compatibility", settings.forceForwardCompatibility, defSet.forceForwardCompatibility);
            lines.AddIndented("high_precision", settings.highPrecision, defSet.highPrecision);
            lines.AddIndented("intensity", settings.intensity, defSet.intensity);
            lines.AddIndented("radius", settings.radius, defSet.radius);
            lines.AddIndented("sample_count", settings.sampleCount, defSet.sampleCount);
            lines.AddIndented("enabled", item.enabled, true);

            lines.AddIndented(false);
        }
    }

    internal sealed class DepthOfFieldParser : Parser<DepthOfFieldModel>
    {
        protected override DepthOfFieldModel Default()
        {
            return new DepthOfFieldModel()
            {
                enabled = false,
                settings = DepthOfFieldModel.Settings.defaultSettings
            };
        }

        protected override DepthOfFieldModel Parse(ConfigNode.ValueList values)
        {
            DepthOfFieldModel.Settings settings = DepthOfFieldModel.Settings.defaultSettings;

            bool calcBool;
            float calcFloat;

            string[] data = {
                "aperture",
                "focallength",
                "focusdistance",
                "kernelsize",
                "usecamerafov",
                "enabled"
            };

            ProcessStream(values, ref data);

            if(data[0] != null && float.TryParse(data[0], out calcFloat))
            {
                settings.aperture = calcFloat;
            }
            if(data[1] != null && float.TryParse(data[1], out calcFloat))
            {
                settings.focalLength = calcFloat;
            }
            if(data[2] != null && float.TryParse(data[2], out calcFloat))
            {
                settings.focusDistance = calcFloat;
            }
            if(data[3] != null)
            {
                DepthOfFieldModel.KernelSize size;
                if(TryParseEnum(data[3], out size))
                {
                    settings.kernelSize = size;
                }
            }
            if(data[4] != null && bool.TryParse(data[4], out calcBool))
            {
                settings.useCameraFov = calcBool;
            }
            if(data[5] == null || !bool.TryParse(data[5], out calcBool))
            {
                calcBool = true;
            }

            return new DepthOfFieldModel()
            {
                enabled = calcBool,
                settings = settings
            };
        }

        internal override void ToFile(List<string> lines, DepthOfFieldModel item)
        {
            lines.AddIndented("Depth_Of_Field");
            lines.AddIndented(true);

            var settings = item.settings;
            var def = DepthOfFieldModel.Settings.defaultSettings;
            lines.AddIndented("aperture", settings.aperture, def.aperture);
            lines.AddIndented("focal_length", settings.focalLength, def.focalLength);
            lines.AddIndented("focus_distance", settings.focusDistance, def.focusDistance);
            lines.AddIndented("kernel_size", settings.kernelSize, def.kernelSize);
            lines.AddIndented("use_camera_fov", settings.useCameraFov, def.useCameraFov);
            lines.AddIndented("enabled", item.enabled, true);

            lines.AddIndented(false);
        }
    }

    internal sealed class MotionBlurParser : Parser<MotionBlurModel>
    {
        protected override MotionBlurModel Default()
        {
            return new MotionBlurModel()
            {
                enabled = false,
                settings = MotionBlurModel.Settings.defaultSettings
            };
        }

        protected override MotionBlurModel Parse(ConfigNode.ValueList values)
        {
            MotionBlurModel.Settings settings = MotionBlurModel.Settings.defaultSettings;

            string[] data =
            {
                "frameblending",
                "samplecount",
                "shutterangle",
                "enabled"
            };

            ProcessStream(values, ref data);

            float calcFloat;
            bool enabled;

            if(data[0] != null && float.TryParse(data[0], out calcFloat))
            {
                settings.frameBlending = calcFloat;
            }
            if(data[1] != null && float.TryParse(data[1], out calcFloat))
            {
                settings.sampleCount = Convert.ToInt32(calcFloat);
            }
            if(data[2] != null && float.TryParse(data[2], out calcFloat))
            {
                settings.shutterAngle = calcFloat;
            }
            if(data[3] == null || !bool.TryParse(data[3], out enabled))
            {
                enabled = true;
            }

            return new MotionBlurModel()
            {
                enabled = enabled,
                settings = settings
            };
        }

        internal override void ToFile(List<string> lines, MotionBlurModel item)
        {
            lines.AddIndented("Motion_Blur");
            lines.AddIndented(true);

            var settings = item.settings;
            var def = MotionBlurModel.Settings.defaultSettings;
            lines.AddIndented("frame_blending", settings.frameBlending, def.frameBlending);
            lines.AddIndented("sample_count", settings.sampleCount, def.sampleCount);
            lines.AddIndented("shutter_angle", settings.shutterAngle, def.shutterAngle);
            lines.AddIndented("enabled", item.enabled, true);

            lines.AddIndented(false);
        }
    }

    internal sealed class EyeAdaptationParser : Parser<EyeAdaptationModel>
    {
        protected override EyeAdaptationModel Default()
        {
            return new EyeAdaptationModel()
            {
                enabled = false,
                settings = EyeAdaptationModel.Settings.defaultSettings
            };
        }

        protected override EyeAdaptationModel Parse(ConfigNode.ValueList values)
        {
            EyeAdaptationModel.Settings settings = EyeAdaptationModel.Settings.defaultSettings;
            string[] data =
            {
                "adaptationtype",  // 0
                "dynamickeyvalue", // 1
                "highpercent",     // 2
                "keyvalue",        // 3
                "logmax",          // 4
                "logmin",          // 5
                "lowpercent",      // 6
                "maxluminance",    // 7
                "minluminance",    // 8
                "speeddown",       // 9
                "speedup",         // 10
                "enabled"          // 11
            };
            ProcessStream(values, ref data);
            bool calcBool;
            float calcFloat;
            int calcInt;
            if(data[0] != null)
            {
                EyeAdaptationModel.EyeAdaptationType type;
                if(TryParseEnum(data[0], out type))
                {
                    settings.adaptationType = type;
                }
            }
            if(data[1] != null && bool.TryParse(data[1], out calcBool))
            {
                settings.dynamicKeyValue = calcBool;
            }
            if(data[2] != null && float.TryParse(data[2], out calcFloat))
            {
                settings.highPercent = calcFloat;
            }
            if(data[3] != null && float.TryParse(data[3], out calcFloat))
            {
                settings.keyValue = calcFloat;
            }
            if(data[4] != null && int.TryParse(data[4], out calcInt))
            {
                settings.logMax = calcInt;
            }
            if(data[5] != null && int.TryParse(data[5], out calcInt))
            {
                settings.logMin = calcInt;
            }
            if(data[6] != null && float.TryParse(data[6], out calcFloat))
            {
                settings.lowPercent = calcFloat;
            }
            if(data[7] != null && float.TryParse(data[7], out calcFloat))
            {
                settings.maxLuminance = calcFloat;
            }
            if(data[8] != null && float.TryParse(data[8], out calcFloat))
            {
                settings.minLuminance = calcFloat;
            }
            if(data[9] != null && float.TryParse(data[9], out calcFloat))
            {
                settings.speedDown = calcFloat;
            }
            if(data[10] != null && float.TryParse(data[10], out calcFloat))
            {
                settings.speedUp = calcFloat;
            }
            if(data[11] == null || !bool.TryParse(data[11], out calcBool))
            {
                calcBool = true;
            }

            return new EyeAdaptationModel()
            {
                enabled = calcBool,
                settings = settings
            };
        }

        internal override void ToFile(List<string> lines, EyeAdaptationModel item)
        {
            lines.AddIndented("Eye_Adaptation");
            lines.AddIndented(true);

            var settings = item.settings;
            var def = EyeAdaptationModel.Settings.defaultSettings;

            lines.AddIndented("adaptation_type", settings.adaptationType, def.adaptationType);
            lines.AddIndented("dynamic_key_value", settings.dynamicKeyValue, def.dynamicKeyValue);
            lines.AddIndented("high_percent", settings.highPercent, def.highPercent);
            lines.AddIndented("key_value", settings.keyValue, def.keyValue);
            lines.AddIndented("log_max", settings.logMax, def.logMax);
            lines.AddIndented("log_min", settings.logMin, def.logMin);
            lines.AddIndented("low_percent", settings.lowPercent, def.lowPercent);
            lines.AddIndented("max_luminance", settings.maxLuminance, def.maxLuminance);
            lines.AddIndented("min_luminance", settings.minLuminance, def.minLuminance);
            lines.AddIndented("speed_down", settings.speedDown, def.speedDown);
            lines.AddIndented("speed_up", settings.speedUp, def.speedUp);
            lines.AddIndented("enabled", item.enabled, true);

            lines.AddIndented(false);
        }
    }

    internal sealed class BloomParser : TextureParser<BloomModel>
    {
        protected override BloomModel Default()
        {
            return new BloomModel()
            {
                enabled = false,
                settings = BloomModel.Settings.defaultSettings
            };
        }
        protected override BloomModel Parse(ConfigNode.ValueList values, out string path)
        {
            BloomModel.LensDirtSettings dirtSettings = BloomModel.LensDirtSettings.defaultSettings;
            BloomModel.BloomSettings bloomSettings = BloomModel.BloomSettings.defaultSettings;
            string[] data =
            {
                "dirtintensity",    // 0
                "dirttexture",      // 1
                "dirtenabled",      // 2
                "bloomantiflicker", // 3
                "bloomintensity",   // 4
                "bloomradius",      // 5
                "bloomsoftknee",    // 6
                "bloomthreshold",   // 7
                "enabled"           // 8
            };
            ProcessStream(values, ref data);
            bool calcBool;
            float calcFloat;

            if(data[2] == null)
            {
                if(bool.TryParse(data[2], out calcBool))
                {
                    if(calcBool)
                    {
                        // explicit enable
                        if(data[0] != null && float.TryParse(data[0], out calcFloat))
                        {
                            dirtSettings.intensity = calcFloat;
                        }
                        else
                        {
                            dirtSettings.intensity = 0f;
                        }
                        if(data[1] != null)
                        {
                            GameDatabase database = GameDatabase.Instance;
                            Texture2D tex = database.GetTexture(data[1], false);
                            if(tex)
                            {
                                path = data[1];
                                dirtSettings.texture = tex;
                                KS3P.Register(data[1], tex, KS3P.TexType.LensDirt);
                            }
                            else
                            {
                                Warning("Failed to load dirt texture path [" + data[1] + "], loading blank fallback texture.");
                                dirtSettings.texture = database.GetTexture("KS3P/Textures/Fallback.png", false);
                                path = "KS3P/Textures/Fallback.png";
                            }
                        }
                        else
                        {
                            dirtSettings.texture = GameDatabase.Instance.GetTexture("KS3P/Textures/Fallback.png", false);
                            path = "KS3P/Textures/Fallback.png";
                        }
                    }
                    else
                    {
                        dirtSettings.intensity = 0f; // disable
                        dirtSettings.texture = GameDatabase.Instance.GetTexture("KS3P/Textures/Fallback.png", false);
                        path = "KS3P/Textures/Fallback.png";
                    }
                }
                else
                {
                    // failed to parse, enable
                    if (data[0] != null && float.TryParse(data[0], out calcFloat))
                    {
                        dirtSettings.intensity = calcFloat;
                    }
                    else
                    {
                        dirtSettings.intensity = 0f;
                    }
                    if (data[1] != null)
                    {
                        GameDatabase database = GameDatabase.Instance;
                        Texture2D tex = database.GetTexture(data[1], false);
                        if (tex)
                        {
                            dirtSettings.texture = tex;
                            path = data[1];
                            KS3P.Register(data[1], tex, KS3P.TexType.LensDirt);
                        }
                        else
                        {
                            Warning("Failed to load dirt texture path [" + data[1] + "], loading blank fallback texture.");
                            dirtSettings.texture = database.GetTexture("KS3P/Textures/Fallback.png", false);
                            path = "KS3P/Textures/Fallback.png";
                        }
                    }
                    else
                    {
                        dirtSettings.texture = GameDatabase.Instance.GetTexture("KS3P/Textures/Fallback.png", false);
                        path = "KS3P/Textures/Fallback.png";
                    }
                }
            }
            else
            {
                // no specific given, enable
                if (data[0] != null && float.TryParse(data[0], out calcFloat))
                {
                    dirtSettings.intensity = calcFloat;
                }
                else
                {
                    dirtSettings.intensity = 0f;
                }
                if (data[1] != null)
                {
                    GameDatabase database = GameDatabase.Instance;
                    Texture2D tex = database.GetTexture(data[1], false);
                    if (tex)
                    {
                        dirtSettings.texture = tex;
                        path = data[1];
                        KS3P.Register(data[1], tex, KS3P.TexType.LensDirt);
                    }
                    else
                    {
                        Warning("Failed to load dirt texture path [" + data[1] + "], loading blank fallback texture.");
                        dirtSettings.texture = database.GetTexture("KS3P/Textures/Fallback.png", false);
                        path = "KS3P/Textures/Fallback.png";
                    }
                }
                else
                {
                    dirtSettings.texture = GameDatabase.Instance.GetTexture("KS3P/Textures/Fallback.png", false);
                    path = "KS3P/Textures/Fallback.png";
                }
            }

            if(data[3] != null && bool.TryParse(data[3], out calcBool))
            {
                bloomSettings.antiFlicker = calcBool;
            }
            if(data[4] != null && float.TryParse(data[4], out calcFloat))
            {
                bloomSettings.intensity = calcFloat;
            }
            if(data[5] != null && float.TryParse(data[5], out calcFloat))
            {
                bloomSettings.radius = calcFloat;
            }
            if(data[6] != null && float.TryParse(data[6], out calcFloat))
            {
                bloomSettings.softKnee = calcFloat;
            }
            if(data[7] != null && float.TryParse(data[7], out calcFloat))
            {
                bloomSettings.threshold = calcFloat;
            }
            
            if(data[8] == null || !bool.TryParse(data[8], out calcBool))
            {
                calcBool = true;
            }

            return new BloomModel()
            {
                enabled = calcBool,
                settings = new BloomModel.Settings()
                {
                    bloom = bloomSettings,
                    lensDirt = dirtSettings
                }
            };
        }
        internal override void ToFile(List<string> lines, BloomModel item)
        {
            lines.AddIndented("Bloom");
            lines.AddIndented(true);

            var settings = item.settings;
            var def = BloomModel.Settings.defaultSettings;

            var bSet = settings.bloom;
            var bDef = def.bloom;
            lines.AddIndented("bloom_anti_flicker", bSet.antiFlicker, bDef.antiFlicker);
            lines.AddIndented("bloom_intensity", bSet.intensity, bDef.intensity);
            lines.AddIndented("bloom_radius", bSet.radius, bDef.radius);
            lines.AddIndented("bloom_soft_knee", bSet.softKnee, bDef.softKnee);
            lines.AddIndented("bloom_threshold", bSet.threshold, bDef.threshold);

            lines.AddIndented("dirt_intensity", settings.lensDirt.intensity, def.lensDirt.intensity);
            lines.AddIndented("dirt_texture = " + ConfigWriter.WriteTarget.dirtTex);
            lines.AddIndented("enabled", item.enabled, true);

            lines.AddIndented(false);
        }
    }

    internal sealed class ColorGradingParser : Parser<ColorGradingModel>
    {
        protected override ColorGradingModel Default()
        {
            return new ColorGradingModel()
            {
                enabled = false,
                settings = ColorGradingModel.Settings.defaultSettings
            };
        }

        internal override ColorGradingModel Parse(ConfigNode node)
        {
            if (node == null)
            {
                return Default();
            }
            else
            {
                string[] data = { "enabled" };
                ProcessStream(node.values, ref data);

                bool calcBool;

                if(data[0] == null || !bool.TryParse(data[0], out calcBool))
                {
                    calcBool = true;
                }

                string[] names =
                {
                    "basic",
                    "mixer",
                    "wheels",
                    "curves",
                    "tonemapper"
                };
                ConfigNode[] nodes =
                {
                    null, // 0, basic
                    null, // 1, mixer
                    null, // 2, wheels
                    null, // 3, curves
                    null  // 4, tonemapper
                };
                int index = 0;
                foreach(ConfigNode subnode in node.nodes)
                {
                    if(names.Contains(KS3PUtil.Prepare(subnode.name), out index))
                    {
                        nodes[index] = subnode;
                    }
                }

                return new ColorGradingModel()
                {
                    enabled = calcBool,

                    settings = new ColorGradingModel.Settings()
                    {
                        basic = ParseBasic(nodes[0]),
                        channelMixer = ParseMixer(nodes[1]),
                        colorWheels = ParseWheels(nodes[2]),
                        curves = ParseCurves(nodes[3]),
                        tonemapping = ParseTonemapper(nodes[4])
                    }
                };
            }
        }

        ColorGradingModel.BasicSettings ParseBasic(ConfigNode node)
        {
            ColorGradingModel.BasicSettings settings = ColorGradingModel.BasicSettings.defaultSettings;
            if(node == null)
            {
                return settings;
            }
            else
            {
                string[] data =
                {
                    "contrast",     // 0
                    "hueshift",     // 1
                    "postexposure", // 2
                    "saturation",   // 3
                    "temperature",  // 4
                    "tint"          // 5
                };
                ProcessStream(node.values, ref data);
                float calcFloat;

                if (data[0] != null && float.TryParse(data[0], out calcFloat))
                {
                    settings.contrast = calcFloat;
                }
                if (data[1] != null && float.TryParse(data[1], out calcFloat))
                {
                    settings.hueShift = calcFloat;
                }
                if (data[2] != null && float.TryParse(data[2], out calcFloat))
                {
                    settings.postExposure = calcFloat;
                }
                if (data[3] != null && float.TryParse(data[3], out calcFloat))
                {
                    settings.saturation = calcFloat;
                }
                if (data[4] != null && float.TryParse(data[4], out calcFloat))
                {
                    settings.temperature = calcFloat;
                }
                if (data[5] != null && float.TryParse(data[5], out calcFloat))
                {
                    settings.tint = calcFloat;
                }

                return settings;
            }
        }
        ColorGradingModel.ChannelMixerSettings ParseMixer(ConfigNode node)
        {
            ColorGradingModel.ChannelMixerSettings settings = ColorGradingModel.ChannelMixerSettings.defaultSettings;
            if(node == null)
            {
                return settings;
            }
            else
            {
                string[] data =
                {
                    "red",      // 0
                    "green",    // 1
                    "blue"     // 2
                };
                ProcessStream(node.values, ref data);
                if(data[0] != null)
                {
                    settings.red = ParseVector3(data[0]);
                }
                if (data[1] != null)
                {
                    settings.green = ParseVector3(data[1]);
                }
                if (data[2] != null)
                {
                    settings.blue = ParseVector3(data[2]);
                }
                return settings;
            }
        }
        ColorGradingModel.ColorWheelsSettings ParseWheels(ConfigNode node)
        {
            ColorGradingModel.ColorWheelsSettings settings = ColorGradingModel.ColorWheelsSettings.defaultSettings;
            if(node == null)
            {
                return settings;
            }
            else
            {
                string[] data = {
                    "mode",     // 0
                    "gain",     // 1
                    "gamma",    // 2
                    "lift",     // 3
                    "offset",   // 4
                    "power",    // 5
                    "slope"     // 6
                };
                ProcessStream(node.values, ref data);

                ColorGradingModel.LinearWheelsSettings linear = ColorGradingModel.LinearWheelsSettings.defaultSettings;
                ColorGradingModel.LogWheelsSettings log = ColorGradingModel.LogWheelsSettings.defaultSettings;

                if (data[0] != null)
                {
                    ColorGradingModel.ColorWheelMode method;
                    if (TryParseEnum(data[0], out method))
                    {
                        settings.mode = method;
                    }
                }
                if(data[1] != null)
                {
                    linear.gain = ParseColor(data[1]);
                }
                if(data[2] != null)
                {
                    linear.gamma = ParseColor(data[2]);
                }
                if(data[3] != null)
                {
                    linear.lift = ParseColor(data[3]);
                }
                if(data[4] != null)
                {
                    log.offset = ParseColor(data[4]);
                }
                if(data[5] != null)
                {
                    log.power = ParseColor(data[5]);
                }
                if(data[6] != null)
                {
                    log.slope = ParseColor(data[6]);
                }
                settings.linear = linear;
                settings.log = log;
                return settings;
            }
        }
        ColorGradingModel.CurvesSettings ParseCurves(ConfigNode node)
        {
            ColorGradingModel.CurvesSettings settings = ColorGradingModel.CurvesSettings.defaultSettings;

            if(node == null)
            {
                return settings;
            }
            else
            {
                ColorGradingCurve parsedCurve;

                string[] data =
                {
                    "master",                       // 0
                    "red",                          // 1
                    "green",                        // 2
                    "blue",                         // 3
                    "hueversushue",                 // 4
                    "hueversussaturation",          // 5
                    "luminosityversussaturation",   // 6
                    "saturationversussaturation"    // 7
                };
                ConfigNode[] nodes =
                {
                    null, // 0
                    null, // 1
                    null, // 2
                    null, // 3
                    null, // 4
                    null, // 5
                    null, // 6
                    null // 7
                };

                int pos;
                foreach (ConfigNode subnode in node.nodes)
                {
                    if (data.Contains(KS3PUtil.Prepare(subnode.name), out pos))
                    {
                        nodes[pos] = subnode;
                    }
                }

                if (TryParseCurve(nodes[0], out parsedCurve))
                {
                    settings.master = parsedCurve;
                }
                if (TryParseCurve(nodes[1], out parsedCurve))
                {
                    settings.red = parsedCurve;
                }
                if (TryParseCurve(nodes[2], out parsedCurve))
                {
                    settings.green = parsedCurve;
                }
                if (TryParseCurve(nodes[3], out parsedCurve))
                {
                    settings.blue = parsedCurve;
                }
                if (TryParseCurve(nodes[4], out parsedCurve))
                {
                    settings.hueVShue = parsedCurve;
                }
                if (TryParseCurve(nodes[5], out parsedCurve))
                {
                    settings.hueVSsat = parsedCurve;
                }
                if (TryParseCurve(nodes[6], out parsedCurve))
                {
                    settings.lumVSsat = parsedCurve;
                }
                if (TryParseCurve(nodes[7], out parsedCurve))
                {
                    settings.satVSsat = parsedCurve;
                }

                return settings;
            }
        }
        ColorGradingModel.TonemappingSettings ParseTonemapper(ConfigNode node)
        {
            ColorGradingModel.TonemappingSettings settings = ColorGradingModel.TonemappingSettings.defaultSettings;

            if(node == null)
            {
                return settings;
            }
            else
            {
                string[] data = {
                  "tonemapper", // 0
                  "blackin",    // 1
                  "blackout",   // 2
                  "whiteclip",  // 3
                  "whitein",    // 4
                  "whitelevel", // 5
                  "whiteout"    // 6
                };
                ProcessStream(node.values, ref data);

                float calcFloat;

                if (data[0] != null)
                {
                    ColorGradingModel.Tonemapper mapper;
                    if (TryParseEnum(data[0], out mapper))
                    {
                        settings.tonemapper = mapper;
                    }
                }
                if (data[1] != null && float.TryParse(data[1], out calcFloat))
                {
                    settings.neutralBlackIn = calcFloat;
                }
                if (data[2] != null && float.TryParse(data[2], out calcFloat))
                {
                    settings.neutralBlackOut = calcFloat;
                }
                if (data[3] != null && float.TryParse(data[3], out calcFloat))
                {
                    settings.neutralWhiteClip = calcFloat;
                }
                if (data[4] != null && float.TryParse(data[4], out calcFloat))
                {
                    settings.neutralWhiteIn = calcFloat;
                }
                if (data[5] != null && float.TryParse(data[5], out calcFloat))
                {
                    settings.neutralWhiteLevel = calcFloat;
                }
                if (data[6] != null && float.TryParse(data[6], out calcFloat))
                {
                    settings.neutralWhiteOut = calcFloat;
                }

                return settings;
            }
        }

        internal override void ToFile(List<string> lines, ColorGradingModel item)
        {
            lines.AddIndented("Color_Grading");
            lines.AddIndented(true);

            var def = ColorGradingModel.Settings.defaultSettings;
            
            #region ProcessBasic
            var basicSettings = item.settings.basic;
            var basicDef = def.basic;

            lines.AddIndented("Basic");
            lines.AddIndented(true);

            lines.AddIndented("contrast", basicSettings.contrast, basicDef.contrast);
            lines.AddIndented("hue_shift", basicSettings.hueShift, basicDef.hueShift);
            lines.AddIndented("post_exposure", basicSettings.postExposure, basicDef.postExposure);
            lines.AddIndented("saturation", basicSettings.saturation, basicDef.saturation);
            lines.AddIndented("temperature", basicSettings.temperature, basicDef.temperature);
            lines.AddIndented("tint", basicSettings.tint, basicDef.tint);

            lines.AddIndented(false);

            #endregion

            #region ProcessChannelMixer
            var mixerSettings = item.settings.channelMixer;
            var mixerDef = def.channelMixer;

            lines.AddIndented("Mixer");
            lines.AddIndented(true);

            lines.AddIndented("red", mixerSettings.red, mixerDef.red);
            lines.AddIndented("green", mixerSettings.green, mixerDef.green);
            lines.AddIndented("blue", mixerSettings.blue, mixerDef.blue);

            lines.AddIndented(false);

            #endregion

            #region ProcessColorWheels
            var wheelSettings = item.settings.colorWheels;
            var wheelDef = def.colorWheels;

            lines.AddIndented("Wheels");
            lines.AddIndented(true);
            
            switch(wheelSettings.mode)
            {
                case ColorGradingModel.ColorWheelMode.Linear:
                    lines.AddIndented("mode = Linear");
                    lines.AddIndented("gain", wheelSettings.linear.gain, wheelDef.linear.gain);
                    lines.AddIndented("gamma", wheelSettings.linear.gamma, wheelDef.linear.gamma);
                    lines.AddIndented("lift", wheelSettings.linear.lift, wheelDef.linear.lift);
                    break;
                case ColorGradingModel.ColorWheelMode.Log:
                    lines.AddIndented("mode = Log");
                    lines.AddIndented("offset", wheelSettings.log.offset, wheelDef.log.offset);
                    lines.AddIndented("power", wheelSettings.log.power, wheelDef.log.power);
                    lines.AddIndented("slope", wheelSettings.log.slope, wheelDef.log.slope);
                    break;
            }

            lines.AddIndented(false);
            #endregion

            #region ProcessCurves
            var curveSettings = item.settings.curves;

            lines.AddIndented("Curves");
            lines.AddIndented(true);

            WriteCurve(lines, "Master", curveSettings.master);
            WriteCurve(lines, "Red", curveSettings.red);
            WriteCurve(lines, "Green", curveSettings.green);
            WriteCurve(lines, "Blue", curveSettings.blue);
            WriteCurve(lines, "Hue_Versus_Hue", curveSettings.hueVShue);
            WriteCurve(lines, "Hue_Versus_Saturation", curveSettings.hueVSsat);
            WriteCurve(lines, "Luminosity_Versus_Saturation", curveSettings.lumVSsat);
            WriteCurve(lines, "Saturation_Versus_Saturation", curveSettings.satVSsat);

            lines.AddIndented(false);
            #endregion

            #region ProcessTonemapping
            var mapSettings = item.settings.tonemapping;
            var mapDef = def.tonemapping;

            lines.AddIndented("Tonemapper");
            lines.AddIndented(true);

            lines.AddIndented("tonemapper", mapSettings.tonemapper, mapDef.tonemapper);
            lines.AddIndented("black_in", mapSettings.neutralBlackIn, mapDef.neutralBlackIn);
            lines.AddIndented("black_out", mapSettings.neutralBlackOut, mapDef.neutralBlackOut);
            lines.AddIndented("white_clip", mapSettings.neutralWhiteClip, mapDef.neutralWhiteClip);
            lines.AddIndented("white_in", mapSettings.neutralWhiteIn, mapDef.neutralWhiteIn);
            lines.AddIndented("white_level", mapSettings.neutralWhiteLevel, mapDef.neutralWhiteLevel);
            lines.AddIndented("white_out", mapSettings.neutralWhiteOut, mapDef.neutralWhiteOut);

            lines.AddIndented(false);
            #endregion

            lines.AddIndented("enabled", item.enabled, true);
            lines.AddIndented(false);
        }
        const string comma = ", ";
        void WriteCurve(List<string> lines, string curveName, ColorGradingCurve curve)
        {
            lines.AddIndented(curveName);
            lines.AddIndented(true);

            lines.AddIndented("zero", curve.ZeroValue, 0f);
            lines.AddIndented("loop", curve.IsLooped, false);
            foreach(var key in curve.curve.keys)
            {
                lines.AddIndented("key = " + key.time + comma + key.value + comma + key.inTangent + comma + key.outTangent);
            }

            lines.AddIndented(false);
        }
    }

    internal sealed class UserLutParser : TextureParser<UserLutModel>
    {
        protected override UserLutModel Default()
        {
            return new UserLutModel()
            {
                enabled = false,
                settings = UserLutModel.Settings.defaultSettings
            };
        }

        protected override UserLutModel Parse(ConfigNode.ValueList values, out string path)
        {
            UserLutModel.Settings settings = UserLutModel.Settings.defaultSettings;

            string[] data = { "contribution", "lut", "enabled" };
            ProcessStream(values, ref data);

            float calcFloat;
            bool enabled;

            if(data[0] != null && float.TryParse(data[0], out calcFloat))
            {
                settings.contribution = calcFloat;
            }

            GameDatabase database = GameDatabase.Instance;
            if (data[1] != null)
            {
                Texture2D tex = database.GetTexture(data[1], false);
                if(tex)
                {
                    settings.lut = tex;
                    path = data[1];
                    KS3P.Register(data[1], tex, KS3P.TexType.Lut);
                }
                else
                {
                    Warning("Failed to load dirt texture path [" + data[1] + "], loading blank fallback texture.");
                    settings.lut = database.GetTexture("KS3P/Textures/Fallback.png", false);
                    path = "KS3P/Textures/Fallback.png";
                }
            }
            else
            {
                settings.lut = database.GetTexture("KS3P/Textures/Fallback.png", false);
                path = "KS3P/Textures/Fallback.png";
            }
            
            if(data[2] == null || !bool.TryParse(data[2], out enabled))
            {
                enabled = true;
            }

            return new UserLutModel()
            {
                enabled = enabled,
                settings = settings
            };
        }

        internal override void ToFile(List<string> lines, UserLutModel item)
        {
            lines.AddIndented("User_Lut");
            lines.AddIndented(true);

            var settings = item.settings.contribution;
            var defCon = UserLutModel.Settings.defaultSettings.contribution;
            lines.AddIndented("contribution", settings, defCon);
            lines.AddIndented("lut = " + ConfigWriter.WriteTarget.lutTex);
            lines.AddIndented("enabled", item.enabled, true);

            lines.AddIndented(false);
        }
    }

    internal sealed class ChromaticAbberationParser : TextureParser<ChromaticAberrationModel>
    {
        protected override ChromaticAberrationModel Default()
        {
            return new ChromaticAberrationModel()
            {
                enabled = false,
                settings = ChromaticAberrationModel.Settings.defaultSettings
            };
        }
        protected override ChromaticAberrationModel Parse(ConfigNode.ValueList values, out string path)
        {
            ChromaticAberrationModel.Settings settings = ChromaticAberrationModel.Settings.defaultSettings;

            string[] data = { "intensity", "texture", "enabled" };
            ProcessStream(values, ref data);

            float calcFloat;
            bool enabled;

            if (data[0] != null && float.TryParse(data[0], out calcFloat))
            {
                settings.intensity = calcFloat;
            }

            GameDatabase database = GameDatabase.Instance;
            if (data[1] != null)
            {
                Texture2D tex = database.GetTexture(data[1], false);
                if (tex)
                {
                    settings.spectralTexture = tex;
                    path = data[1];
                    KS3P.Register(data[1], tex, KS3P.TexType.ChromaticTex);
                }
                else
                {
                    Warning("Failed to load spectral texture path [" + data[1] + "], loading blank fallback texture.");
                    settings.spectralTexture = database.GetTexture("KS3P/Textures/Fallback.png", false);
                    path = "KS3P/Textures/Fallback.png";
                }
            }
            else
            {
                settings.spectralTexture = database.GetTexture("KS3P/Textures/Fallback.png", false);
                path = "KS3P/Textures/Fallback.png";
            }

            if (data[2] == null || !bool.TryParse(data[2], out enabled))
            {
                enabled = true;
            }

            return new ChromaticAberrationModel()
            {
                enabled = enabled,
                settings = settings
            };
        }
        internal override void ToFile(List<string> lines, ChromaticAberrationModel item)
        {
            lines.AddIndented("Chromatic_Abberation");
            lines.AddIndented(true);
            lines.AddIndented("intensity", item.settings.intensity, ChromaticAberrationModel.Settings.defaultSettings.intensity);
            lines.AddIndented("texture = " + ConfigWriter.WriteTarget.chromaticTex);
            lines.AddIndented("enabled", item.enabled, true);
            lines.AddIndented(false);
        }
    }

    internal sealed class GrainParser : Parser<GrainModel>
    {
        protected override GrainModel Default()
        {
            return new GrainModel()
            {
                enabled = false,
                settings = GrainModel.Settings.defaultSettings
            };
        }
        protected override GrainModel Parse(ConfigNode.ValueList values)
        {
            GrainModel.Settings settings = GrainModel.Settings.defaultSettings;

            string[] data =
            {
                "colored",
                "intensity",
                "luminancecontribution",
                "size",
                "enabled"
            };
            ProcessStream(values, ref data);

            bool calcBool;
            float calcFloat;

            if(data[0] != null && bool.TryParse(data[0], out calcBool))
            {
                settings.colored = calcBool;
            }
            if(data[1] != null && float.TryParse(data[1], out calcFloat))
            {
                settings.intensity = calcFloat;
            }
            if(data[2] != null && float.TryParse(data[2], out calcFloat))
            {
                settings.luminanceContribution = calcFloat;
            }
            if(data[3] != null && float.TryParse(data[3], out calcFloat))
            {
                settings.size = calcFloat;
            }
            if(data[4] == null || !bool.TryParse(data[4], out calcBool))
            {
                calcBool = true;
            }

            return new GrainModel()
            {
                enabled = calcBool,
                settings = settings
            };
        }

        internal override void ToFile(List<string> lines, GrainModel item)
        {
            lines.AddIndented("Grain_Model");
            lines.AddIndented(true);

            var settings = item.settings;
            var def = GrainModel.Settings.defaultSettings;
            lines.AddIndented("colored", settings.colored, def.colored);
            lines.AddIndented("intensity", settings.intensity, def.intensity);
            lines.AddIndented("luminance_contribution", settings.luminanceContribution, def.luminanceContribution);
            lines.AddIndented("size", settings.size, def.size);
            lines.AddIndented("enabled", item.enabled, true);

            lines.AddIndented(false);
        }
    }

    internal sealed class VignetteParser : TextureParser<VignetteModel>
    {
        protected override VignetteModel Default()
        {
            return new VignetteModel()
            {
                enabled = false,
                settings = VignetteModel.Settings.defaultSettings
            };
        }
        protected override VignetteModel Parse(ConfigNode.ValueList values, out string path)
        {
            VignetteModel.Settings settings = VignetteModel.Settings.defaultSettings;

            string[] data =
            {
                "center",       // 0
                "color",        // 1
                "intensity",    // 2
                "mask",         // 3
                "mode",         // 4
                "opacity",      // 5
                "rounded",      // 6
                "roundness",    // 7
                "smoothness",   // 8
                "enabled"       // 9
            };
            ProcessStream(values, ref data);

            float calcFloat;
            bool calcBool;

            if(data[0] != null)
            {
                settings.center = ParseVector2(data[0]);
            }
            if(data[1] != null)
            {
                settings.color = ParseColor(data[1]);
            }
            if(data[2] != null && float.TryParse(data[2], out calcFloat))
            {
                settings.intensity = calcFloat;
            }
            GameDatabase database = GameDatabase.Instance;
            if (data[3] != null)
            {
                Texture2D tex = database.GetTexture(data[3], false);
                if (tex)
                {
                    settings.mask = tex;
                    path = data[3];
                    KS3P.Register(data[3], tex, KS3P.TexType.VignetteMask);
                }
                else
                {
                    Warning("Failed to load mask texture path [" + data[3] + "], loading blank fallback texture.");
                    settings.mask = database.GetTexture("KS3P/Textures/Fallback.png", false);
                    path = "KS3P/Textures/Fallback.png";
                }
            }
            else
            {
                settings.mask = database.GetTexture("KS3P/Textures/Fallback.png", false);
                path = "KS3P/Textures/Fallback.png";
            }
            if(data[4] != null)
            {
                VignetteModel.Mode mode;
                if(TryParseEnum(data[4], out mode))
                {
                    settings.mode = mode;
                }
            }
            if(data[5] != null && float.TryParse(data[5], out calcFloat))
            {
                settings.opacity = calcFloat;
            }
            if(data[6] != null && bool.TryParse(data[6], out calcBool))
            {
                settings.rounded = calcBool;
            }
            if(data[7] != null && float.TryParse(data[7], out calcFloat))
            {
                settings.roundness = calcFloat;
            }
            if(data[8] != null && float.TryParse(data[8], out calcFloat))
            {
                settings.smoothness = calcFloat;
            }
            if(data[9] == null || !bool.TryParse(data[9], out calcBool))
            {
                calcBool = true;
            }

            return new VignetteModel()
            {
                enabled = calcBool,
                settings = settings
            };
        }
        internal override void ToFile(List<string> lines, VignetteModel item)
        {
            lines.AddIndented("Vignette");
            lines.AddIndented(true);

            var settings = item.settings;
            var def = VignetteModel.Settings.defaultSettings;
            lines.AddIndented("center", settings.center, def.center);
            lines.AddIndented("color", settings.color, def.color);
            lines.AddIndented("intensity", settings.intensity, def.intensity);
            lines.AddIndented("mask = " + ConfigWriter.WriteTarget.vignetteMask);
            lines.AddIndented("mode", settings.mode, def.mode);
            lines.AddIndented("opacity", settings.opacity, def.opacity);
            lines.AddIndented("rounded", settings.rounded, def.rounded);
            lines.AddIndented("roundness", settings.roundness, def.roundness);
            lines.AddIndented("smoothness", settings.smoothness, def.smoothness);
            lines.AddIndented("enabled", item.enabled, true);
            lines.AddIndented(false);
        }
    }

    internal sealed class ScreenSpaceReflectionParser : Parser<ScreenSpaceReflectionModel>
    {
        protected override ScreenSpaceReflectionModel Default()
        {
            return new ScreenSpaceReflectionModel()
            {
                enabled = false,
                settings = ScreenSpaceReflectionModel.Settings.defaultSettings
            };
        }
        protected override ScreenSpaceReflectionModel Parse(ConfigNode.ValueList values)
        {
            ScreenSpaceReflectionModel.Settings settings = ScreenSpaceReflectionModel.Settings.defaultSettings;
            var i_s = settings.intensity;
            var r_s = settings.reflection;
            var m_s = settings.screenEdgeMask;
            float calcFloat;
            int calcInt;
            bool calcBool;
            string[] data =
            {
                "blendtype",
                "reflectionquality",
                "maxdistance",
                "iterationcount",
                "stepsize",
                "widthmodifier",
                "reflectionblur",
                "reflectbackfaces",
                "reflectionmultiplier",
                "fadedistance",
                "fresnelfade",
                "fresnelfadepower",
                "intensity",
                "enabled"
            };
            
            if(data[0] != null)
            {
                ScreenSpaceReflectionModel.SSRReflectionBlendType btype;
                if(MiscParser.TryParseEnum(data[0], out btype))
                {
                    r_s.blendType = btype;
                }
            }
            if(data[1] != null)
            {
                ScreenSpaceReflectionModel.SSRResolution res;
                if(MiscParser.TryParseEnum(data[1], out res))
                {
                    r_s.reflectionQuality = res;
                }
            }
            if(data[2] != null && float.TryParse(data[2], out calcFloat))
            {
                r_s.maxDistance = calcFloat;
            }
            if(data[3] != null && int.TryParse(data[3], out calcInt))
            {
                r_s.iterationCount = calcInt;
            }
            if(data[4] != null && int.TryParse(data[4], out calcInt))
            {
                r_s.stepSize = calcInt;
            }
            if(data[5] != null && float.TryParse(data[5], out calcFloat))
            {
                r_s.widthModifier = calcFloat;
            }
            if(data[6] != null && float.TryParse(data[6], out calcFloat))
            {
                r_s.reflectionBlur = calcFloat;
            }
            if(data[7] != null && bool.TryParse(data[7], out calcBool))
            {
                r_s.reflectBackfaces = calcBool;
            }
            if(data[8] != null && float.TryParse(data[8], out calcFloat))
            {
                i_s.reflectionMultiplier = calcFloat;
            }
            if(data[9] != null && float.TryParse(data[9], out calcFloat))
            {
                i_s.fadeDistance = calcFloat;
            }
            if(data[10] != null && float.TryParse(data[10], out calcFloat))
            {
                i_s.fresnelFade = calcFloat;
            }
            if(data[11] != null && float.TryParse(data[11], out calcFloat))
            {
                i_s.fresnelFadePower = calcFloat;
            }
            if(data[12] != null && float.TryParse(data[12], out calcFloat))
            {
                m_s.intensity = calcFloat;
            }

            if(data[13] == null || !bool.TryParse(data[13], out calcBool))
            {
                calcBool = true;
            }

            settings.intensity = i_s;
            settings.reflection = r_s;
            settings.screenEdgeMask = m_s;
            return new ScreenSpaceReflectionModel()
            {
                settings = settings,
                enabled = calcBool
            };
        }
        internal override void ToFile(List<string> lines, ScreenSpaceReflectionModel item)
        {
            lines.AddIndented("Screen_Space_Reflection");
            lines.AddIndented(true);

            var settings = item.settings;
            var def = ScreenSpaceReflectionModel.Settings.defaultSettings;

            lines.AddIndented("blend_type", settings.reflection.blendType, def.reflection.blendType);
            lines.AddIndented("reflection_quality", settings.reflection.reflectionQuality, def.reflection.reflectionQuality);
            lines.AddIndented("max_distance", settings.reflection.maxDistance, def.reflection.maxDistance);
            lines.AddIndented("iteration_count", settings.reflection.iterationCount, def.reflection.iterationCount);
            lines.AddIndented("step_size", settings.reflection.stepSize, def.reflection.stepSize);
            lines.AddIndented("width_modifier", settings.reflection.widthModifier, def.reflection.widthModifier);
            lines.AddIndented("reflection_blur", settings.reflection.reflectionBlur, def.reflection.reflectionBlur);
            lines.AddIndented("reflect_backfaces", settings.reflection.reflectBackfaces, def.reflection.reflectBackfaces);
            lines.AddIndented("reflection_multiplier", settings.intensity.reflectionMultiplier, def.intensity.reflectionMultiplier);
            lines.AddIndented("fade_distance", settings.intensity.fadeDistance, def.intensity.fadeDistance);
            lines.AddIndented("fresnel_fade", settings.intensity.fresnelFade, def.intensity.fresnelFade);
            lines.AddIndented("fresnel_fade_power", settings.intensity.fresnelFadePower, def.intensity.fresnelFadePower);
            lines.AddIndented("intensity", settings.screenEdgeMask.intensity, def.screenEdgeMask.intensity);
            lines.AddIndented("enabled", item.enabled, true);
            lines.AddIndented(false);
        }
    }
}