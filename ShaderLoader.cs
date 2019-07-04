using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KSP_PostProcessing
{
    // Mostly borrowed from https://github.com/Kopernicus/Kopernicus/blob/master/src/Kopernicus.Components/ShaderLoader.cs
    // Credit and lots of thanks goes to Thomas P.

    /// <summary>
    /// Responsible for loading, indexing and selecting all KS3P shaders.
    /// </summary>
    public static class ShaderLoader
    {
        /// <summary>
        /// The collection of all shaders.
        /// </summary>
        static Dictionary<string, Shader> shaderDictionary = new Dictionary<string, Shader>();

        /// <summary>
        /// The collection of all compute shaders.
        /// </summary>
        static Dictionary<string, ComputeShader> computeShaderDictionary = new Dictionary<string, ComputeShader>();

        public static ComputeShader GetComputeShader(string shaderName)
        {
            Debug.Log("[KS3P]: Searching for compute shader [" + shaderName + "].");

            if (computeShaderDictionary.ContainsKey(shaderName))
            {
                return computeShaderDictionary[shaderName];
            }
            else
            {
                // If we reach this part, we have found no shader
                Debug.LogError("[KS3P]: No compute shader found with name [" + shaderName + "].");
                return null;
            }
        }

        public static Shader GetShader(string shaderName)
        {
            Debug.Log("[KS3P]: Searching for shader [" + shaderName + "].");

            if (shaderDictionary.ContainsKey(shaderName))
            {
                return shaderDictionary[shaderName];
            }
            else
            {
                // If we reach this part, we have found no shader
                Debug.LogError("[KS3P]: No shader found with name [" + shaderName + "].");
                return null;
            }
        }

        public static void LoadShaders(ref List<string> log)
        {
            string path = Path.Combine(KS3PUtil.Root, "GameData");
            path = Path.Combine(path, "KS3P");
            path = Path.Combine(path, "Shaders");


            // gather GPU data
            string gpuString = SystemInfo.graphicsDeviceVersion;

            // check DX11

            if(gpuString.Contains("Direct3D 11.0"))
            {
                // inject dx11
                KS3P.Log("DX11 preference detected, loading DX11 designed shaders (big thanks to forum user jrodriguez!)", ref log);
                path = Path.Combine(path, "postprocessingshaders-dx11");
            }
            else
            {
                // merge path but don't add dx11 targeting
                path = Path.Combine(path, "postprocessingshaders");
            }

            // get target platform AND check OpenGL

            if (Application.platform == RuntimePlatform.WindowsPlayer)
            {
                if (gpuString.StartsWith("OpenGL"))
                {
                    KS3P.Log("OpenGL preference detected, responding appropriately.", ref log);
                    path += "-linux.unity3d"; // For OpenGL users on Windows we load the Linux shaders to fix OpenGL issues
                }
                else
                {
                    path += "-windows.unity3d";
                }
            }
            else if (Application.platform == RuntimePlatform.LinuxPlayer)
            {
                path += "-linux.unity3d";
            }
            else
            {
                path += "-macosx.unity3d";
            }

            // target bundle finalized. Let's roll.

            KS3P.Log("Loading asset bundle at path " + path, ref log);

            using (WWW www = new WWW("file://" + path))
            {
                AssetBundle bundle = www.assetBundle;

                // load shaders
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                foreach (Shader shader in shaders)
                {
                    if(shaderDictionary.ContainsKey(shader.name))
                    {
                        KS3P.Warning("Blocking duplicate shader [" + shader.name + "].", ref log);
                    }
                    else
                    {
                        KS3P.Warning("Adding shader [" + shader.name + "].", ref log);
                        shaderDictionary.Add(shader.name, shader);
                    }
                }

                // load compute shaders
                ComputeShader[] computeshaders = bundle.LoadAllAssets<ComputeShader>();
                foreach(ComputeShader cShader in computeshaders)
                {
                    if(computeShaderDictionary.ContainsKey(cShader.name))
                    {
                        KS3P.Warning("Blocking duplicate compute shader [" + cShader.name + "].", ref log);
                    }
                    else
                    {
                        KS3P.Log("Adding compute shader [" + cShader.name + "].", ref log);
                        computeShaderDictionary.Add(cShader.name, cShader);
                    }
                }

                bundle.Unload(false);
            }
        }
    }
}