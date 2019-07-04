namespace KSP_PostProcessing.Operators
{
    [KSPAddon(KSPAddon.Startup.EditorVAB, false)]
    public sealed class VABOperator : PostProcessingOperator
    {
        protected override void Start()
        {
            KS3P.currentScene = KS3P.Scene.VAB;
            Patch(false, KS3P.Scene.VAB);
        }
    }
}