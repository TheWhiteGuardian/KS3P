using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace KSP_PostProcessing
{
    internal static class ConfigWriter
    {
        const string tab = "    ",
            doubleTab = tab + tab,
            tripleTab = doubleTab + tab,
            quadTab = tripleTab + tab,
            quinTab = quadTab + tab,
            secTab = quinTab + tab,
            sepTab = secTab + tab,
            ocTab = sepTab + tab;

        const string bracketOpen = "{", bracketClose = "}", equiv = " = ";
        
        internal static void AddIndented(this List<string> list, string value)
        {
            switch (tabCount)
            {
                case 1: list.Add(tab + value); break;
                case 2: list.Add(doubleTab + value); break;
                case 3: list.Add(tripleTab + value); break;
                case 4: list.Add(quadTab + value); break;
                case 5: list.Add(quinTab + value); break;
                case 6: list.Add(secTab + value); break;
                case 7: list.Add(sepTab + value); break;
                case 8: list.Add(ocTab + value); break;
                default: list.Add(string.Empty + value); break;
            }
        }
        internal static void AddIndented<T>(this List<string> list, string value, T item, T defaultValue)
        {
            if(!defaultValue.Equals(item))
            {
                AddIndented(list, value + equiv + item.ToString());
            }
        }
        internal static void AddIndented(this List<string> list, bool open)
        {
            if(open)
            {
                list.AddIndented(bracketOpen);
                tabCount++;
            }
            else
            {
                tabCount--;
                list.AddIndented(bracketClose);
            }
        }
        static byte tabCount = 0;

        static void Write(string tab, string name, object value, object defaultValue, ref List<string> data)
        {
            if(value != defaultValue)
            {
                data.Add(tab + name + " = " + value.ToString());
            }
        }
        static void Write(string ttab, string qtab, string name, ColorGradingCurve value, ColorGradingCurve defaultValue, ref List<string> data)
        {
            if(value.Equals(defaultValue))
            {
                data.Add(ttab + name);
                data.Add(ttab + "{");

                Write(qtab, "zero", value.ZeroValue, 0f, ref data);
                Write(qtab, "loop", value.IsLooped, false, ref data);
                Write(qtab, "bounds", value.Range, Vector2.zero, ref data);

                foreach(Keyframe key in value.curve.keys)
                {
                    data.Add(qtab + "key = " + key.time + ", " + key.value + ", " + key.inTangent + ", " + key.outTangent);
                }

                data.Add(ttab + "}");
            }
        }
        internal static Profile WriteTarget { get; private set; }

        internal static void ToFile(Profile profile, string profileName, string authorName)
        {
            WriteTarget = profile;
            List<string> data = new List<string>();

            data.AddIndented("KS3P");
            data.AddIndented(true);

            data.AddIndented("Profile");
            data.AddIndented(true);

            data.AddIndented("name = " + profileName);
            data.AddIndented("author = " + authorName);

            bool foundScene = false;
            string scene = "scene = ";
            for(int i = 0; i < 9; i++)
            {
                if(profile.scenes[i])
                {
                    if(!foundScene)
                    {
                        foundScene = true;
                    }
                    else
                    {
                        scene += ", ";
                    }

                    scene += ((KS3P.Scene)i).ToString();
                }
            }
            if(foundScene)
            {
                data.AddIndented(scene);
            }

            KS3PUtil.aaParser.ToFile(data, profile.profile.antialiasing);
            KS3PUtil.aoParser.ToFile(data, profile.profile.ambientOcclusion);
            KS3PUtil.dofParser.ToFile(data, profile.profile.depthOfField);
            KS3PUtil.mbParser.ToFile(data, profile.profile.motionBlur);
            KS3PUtil.eaParser.ToFile(data, profile.profile.eyeAdaptation);
            KS3PUtil.bParser.ToFile(data, profile.profile.bloom);
            KS3PUtil.cgParser.ToFile(data, profile.profile.colorGrading);
            KS3PUtil.ulParser.ToFile(data, profile.profile.userLut);
            KS3PUtil.caParser.ToFile(data, profile.profile.chromaticAberration);
            KS3PUtil.gParser.ToFile(data, profile.profile.grain);
            KS3PUtil.vParser.ToFile(data, profile.profile.vignette);
            KS3PUtil.ssrParser.ToFile(data, profile.profile.screenSpaceReflection);

            data.AddIndented(false);

            data.AddIndented(false);

            WriteTarget = null;

            string file = Path.Combine(KS3PUtil.Export, profileName + "_by_" + authorName + ".txt");
            File.WriteAllLines(file, data.ToArray());
        }
    }
}
