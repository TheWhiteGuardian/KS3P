namespace KSP_PostProcessing.Operators
{
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public sealed class TrackStationOperator : PostProcessingOperator
    {
        protected override void Start()
        {
            KS3P.currentScene = KS3P.Scene.TrackingStation;
            Patch(true, KS3P.Scene.TrackingStation);
        }
    }
}
