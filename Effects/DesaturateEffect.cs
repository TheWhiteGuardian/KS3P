//using KSP_PostProcessing.Effects;
using UnityEngine;

/*

namespace KSP_PostProcessing.Effects
{
    /// <summary>
    /// Interpolates the screen to grayscale.
    /// </summary>
    public sealed class DesaturateEffect : PostProcessingEffect
    {

        /// <summary>
        /// How much should be interpolated. 0 = color, 1 = greyscale, anything in between is interpolation.
        /// </summary>
        public float percentage;

        Material mat;

        public override void Setup()
        {
            mat = new Material(ShaderLoader.GetShader("KS3P/Desaturate"));
        }

        public override void Apply(RenderTexture src, RenderTexture dest)
        {
            mat.SetFloat("_Strength", percentage);
            Graphics.Blit(src, dest, mat);
        }
    }
}

namespace KSP_PostProcessing.Parsers
{
    public sealed class DesaturateEffectParser : Parser<DesaturateEffect>
    {
        protected override DesaturateEffect Default()
        {
            return new DesaturateEffect()
            {
                enabled = false,
                percentage = 0f
            };
        }

        protected override DesaturateEffect Parse(ConfigNode.ValueList values)
        {
            string[] data = { "percentage", "enabled" };
            ProcessStream(values, ref data);
            float percent;
            if(data[0] == null || !float.TryParse(data[0], out percent))
            {
                percent = 1f;
            }

            bool enabled;
            if(data[1] == null || !bool.TryParse(data[1], out enabled))
            {
                enabled = true;
            }

            return new DesaturateEffect()
            {
                enabled = enabled,
                percentage = percent
            };
        }
    }
}

*/