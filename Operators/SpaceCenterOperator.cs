namespace KSP_PostProcessing.Operators
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public sealed class SpaceCenterOperator : PostProcessingOperator
    {
        protected override void Start()
        {
            KS3P.currentScene = KS3P.Scene.SpaceCenter;
            Patch(false, KS3P.Scene.SpaceCenter);
        }
    }
}