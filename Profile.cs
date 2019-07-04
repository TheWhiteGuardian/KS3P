using UnityEngine;
using System.IO;
using KSP_PostProcessing.Parsers;
using System.Collections.Generic;
using System.Collections;

namespace KSP_PostProcessing
{
    /// <summary>
    /// A KS3P post-processing profile.
    /// </summary>
    [System.Serializable]
    public sealed class Profile
    {
        public PostProcessingProfile profile;
        public BitArray scenes = new BitArray(9, false);
        public string dirtTex;
        public string lutTex;
        public string vignetteMask;
        public string chromaticTex;
        public string ProfileName = "Undefined";
        public string AuthorName = "Unknown";
        public string identifier => ProfileName + " by " + AuthorName;
        public Profile(ConfigNode node)
        {
            string filtered;
            foreach (ConfigNode.Value v in node.values)
            {
                filtered = KS3PUtil.Prepare(v.name);
                if (filtered == "name")
                {
                    ProfileName = v.value;
                }
                else if (filtered == "author")
                {
                    AuthorName = v.value;
                }
                else if (filtered == "scene")
                {
                    scenes = MiscParser.ParseSceneList(v.value);
                }
            }

            Dictionary<string, ConfigNode> nodes = new Dictionary<string, ConfigNode>();
            foreach (ConfigNode subnode in node.nodes)
            {
                nodes.Add(KS3PUtil.Prepare(subnode.name), subnode);
            }
            profile = ScriptableObject.CreateInstance<PostProcessingProfile>();
            profile.antialiasing = KS3PUtil.aaParser.Parse(nodes.Grab("antialiasing"));
            profile.ambientOcclusion = KS3PUtil.aoParser.Parse(nodes.Grab("ambientocclusion"));
            profile.depthOfField = KS3PUtil.dofParser.Parse(nodes.Grab("depthoffield"));
            profile.motionBlur = KS3PUtil.mbParser.Parse(nodes.Grab("motionblur"));
            profile.eyeAdaptation = KS3PUtil.eaParser.Parse(nodes.Grab("eyeadaptation"));
            profile.bloom = KS3PUtil.bParser.Parse(nodes.Grab("bloom"), out dirtTex);
            profile.colorGrading = KS3PUtil.cgParser.Parse(nodes.Grab("colorgrading"));
            profile.userLut = KS3PUtil.ulParser.Parse(nodes.Grab("userlut"), out lutTex);
            profile.chromaticAberration = KS3PUtil.caParser.Parse(nodes.Grab("chromaticabberation"), out chromaticTex);
            profile.grain = KS3PUtil.gParser.Parse(nodes.Grab("grain"));
            profile.vignette = KS3PUtil.vParser.Parse(nodes.Grab("vignette"), out vignetteMask);
            profile.dithering = new DitheringModel()
            {
                enabled = (nodes.Grab("dithering") != null),
                settings = DitheringModel.Settings.defaultSettings
            };
            profile.screenSpaceReflection = new ScreenSpaceReflectionParser().Parse(nodes.Grab("screenspacereflection"));
        }
        public static implicit operator Profile(ConfigNode node) { return new Profile(node); }
    }
}