using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using KSP_PostProcessing.Parsers;

namespace KSP_PostProcessing
{
    /// <summary>
    /// Contains several (extension) methods used by KS3P.
    /// </summary>
    internal static class KS3PUtil
    {
        /// <summary>
        /// Preprocesses a string for parsing.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        internal static string Prepare(string s) => s.Replace("_", string.Empty).ToLower();
        internal static bool Contains<T>(this T[] array, T item)
        {
            for(int x = 0; x < array.Length; x++)
            {
                if(array[x].Equals(item))
                {
                    return true;
                }
            }
            return false;
        }
        internal static bool Contains<T>(this T[] array, T item, out int pos)
        {
            for (int x = 0; x < array.Length; x++)
            {
                if (array[x].Equals(item))
                {
                    pos = x;
                    return true;
                }
            }
            pos = -1;
            return false;
        }
        internal static void Remove<T>(this List<T> list, int id)
        {
            list.Remove(list[id]);
        }
        
        internal static bool IsParsableType(this Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: return true;
                case TypeCode.Byte: return true;
                case TypeCode.Char: return true;
                case TypeCode.Decimal: return true;
                case TypeCode.Double: return true;
                case TypeCode.Int16: return true;
                case TypeCode.Int32: return true;
                case TypeCode.Int64: return true;
                case TypeCode.Object: return true;
                case TypeCode.SByte: return true;
                case TypeCode.Single: return true;
                case TypeCode.String: return true;
                case TypeCode.UInt16: return true;
                case TypeCode.UInt32: return true;
                case TypeCode.UInt64: return true;
                default:
                    if (t == typeof(Vector2))
                    {
                        return true;
                    }
                    else if (t == typeof(Vector3))
                    {
                        return true;
                    }
                    else if (t == typeof(Vector4))
                    {
                        return true;
                    }
                    else if (t == typeof(Color))
                    {
                        return true;
                    }
                    else if (t == typeof(Texture) || t == typeof(Texture2D))
                    {
                        return true;
                    }
                    else { return false; }
            }
        }

        internal static V Grab<K, V>(this Dictionary<K,V> dictionary, K key) where V : class
        {
            if (dictionary.ContainsKey(key))
            {
                return dictionary[key];
            }
            else return null;
        }

        internal static string TrimRGBA(this string s)
        {
            char[] separator = { '(' };
            string[] parts = s.Split(separator);
            separator = new char[1] { ')' };
            parts = parts[1].Split(separator);
            return parts[0];
        }

        internal static bool TryParseKeyframe(string input, out Keyframe output)
        {
            char[] separator = { ',' };
            string[] parts = input.Split(separator);

            if (parts.Length < 2)
            {
                output = new Keyframe();
                return false;
            }
            else
            {
                if (parts.Length == 2)
                {
                    float[] data = { 0f, 0f };
                    float calcFloat;
                    if (float.TryParse(parts[0], out calcFloat))
                    {
                        data[0] = calcFloat;
                    }
                    if (float.TryParse(parts[1], out calcFloat))
                    {
                        data[1] = calcFloat;
                    }
                    output = new Keyframe(data[0], data[1]);
                    return true;
                }
                else
                {
                    float[] data = { 0f, 0f, 0f, 0f };
                    float calcFloat;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (float.TryParse(parts[i], out calcFloat))
                        {
                            data[i] = calcFloat;
                        }
                    }
                    output = new Keyframe(data[0], data[1], data[2], data[3]);
                    return true;
                }
            }
        }

        internal static bool TryAdd(this AnimationCurve curve, string input)
        {
            Keyframe frame;
            if (TryParseKeyframe(input, out frame))
            {
                curve.AddKey(frame);
                return true;
            }
            else return false;
        }
        internal static bool TryAdd(this AnimationCurve curve, string input, out int index)
        {
            Keyframe frame;
            if (TryParseKeyframe(input, out frame))
            {
                index = curve.AddKey(frame);
                return true;
            }
            else
            {
                index = 0;
                return false;
            }
        }

        internal static T[] AsArray<T>(this T obj)
        {
            return new T[1] { obj };
        }

        internal static string RemoveEnd(this string s, int count)
        {
            char[] parts = s.ToCharArray();
            string target = string.Empty;
            for(int i = 0; i < parts.Length - count; i++)
            {
                target += parts[i].ToString();
            }
            return target;
        }

        internal static string Root { get; private set; } = KSPUtil.ApplicationRootPath.Replace("KSP_x64_Data/../", string.Empty);
        internal static string Log { get; private set; } = Path.Combine(Root, Path.Combine("GameData", "KS3P"));
        internal static string Export { get; private set; } = Path.Combine(Log, "Export");
        
        internal static void Blit(Texture source, Material mat)
        {
            Graphics.Blit(source, mat);
        }
        internal static void Blit(Texture source, RenderTexture dest)
        {
            Graphics.Blit(source, dest);
        }
        internal static void Blit(Texture source, Material mat, int pass)
        {
            Graphics.Blit(source, mat, pass);
        }
        internal static void Blit(Texture source, RenderTexture dest, Material mat)
        {
            Graphics.Blit(source, dest, mat);
        }
        internal static void Blit(Texture source, RenderTexture dest, Material mat, int pass)
        {
            Graphics.Blit(source, dest, mat, pass);
        }
        internal static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset)
        {
            Graphics.Blit(source, dest, scale, offset);
        }

        internal static AntiAliasingParser aaParser = new AntiAliasingParser();
        internal static AmbientOcclusionParser aoParser = new AmbientOcclusionParser();
        internal static DepthOfFieldParser dofParser = new DepthOfFieldParser();
        internal static MotionBlurParser mbParser = new MotionBlurParser();
        internal static EyeAdaptationParser eaParser = new EyeAdaptationParser();
        internal static BloomParser bParser = new BloomParser();
        internal static ColorGradingParser cgParser = new ColorGradingParser();
        internal static UserLutParser ulParser = new UserLutParser();
        internal static ChromaticAbberationParser caParser = new ChromaticAbberationParser();
        internal static GrainParser gParser = new GrainParser();
        internal static VignetteParser vParser = new VignetteParser();
        internal static ScreenSpaceReflectionParser ssrParser = new ScreenSpaceReflectionParser();

        internal static Vector2 GetBounds(this AnimationCurve curve)
        {
            var frames = curve.keys;
            return new Vector2(frames[0].time, frames[frames.Length - 1].time);
        }
    }
}
