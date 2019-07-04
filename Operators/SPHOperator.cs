namespace KSP_PostProcessing.Operators
{
    [KSPAddon(KSPAddon.Startup.EditorSPH, false)]
    public sealed class SPHOperator : PostProcessingOperator
    {
        protected override void Start()
        {
            KS3P.currentScene = KS3P.Scene.SPH;
            Patch(false, KS3P.Scene.SPH);
        }
    }
}
