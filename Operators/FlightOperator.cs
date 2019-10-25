using UnityEngine;

namespace KSP_PostProcessing.Operators
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FlightOperator : MonoBehaviour
    {
        enum State
        {
            Flight,
            EVA,
            IVA,
            Map,
            Initial
        }

        State activeState = State.Initial;

        private void Start()
        {
            mainCam = Camera.main.gameObject.AddOrGetComponent<PostProcessingBehaviour>();
            scaledCam = ScaledCamera.Instance.gameObject.AddOrGetComponent<PostProcessingBehaviour>();
            ivaCamInstance = InternalCamera.Instance;
            ivaCam = ivaCamInstance.gameObject.AddOrGetComponent<PostProcessingBehaviour>();
        }

        PostProcessingBehaviour mainCam;
        PostProcessingBehaviour scaledCam;
        PostProcessingBehaviour ivaCam;
        InternalCamera ivaCamInstance;

        private void Update()
        {
            if(!FlightGlobals.ready)
            {
                return;
            }
            if (MapView.MapIsEnabled)
            {
                if (activeState != State.Map)
                {
                    KS3P.currentScene = KS3P.Scene.MapView;
                    KS3P.Register(scaledCam, KS3P.Scene.MapView);
                    activeState = State.Map;
                }
            }
            else if (ivaCamInstance.isActive)
            {
                if (activeState != State.IVA)
                {
                    scaledCam.enabled = false;
                    KS3P.currentScene = KS3P.Scene.IVA;
                    KS3P.Register(ivaCam, KS3P.Scene.IVA);
                    activeState = State.IVA;
                }
            }
            else if (FlightGlobals.ActiveVessel.isEVA)
            {
                if (activeState != State.EVA)
                {
                    scaledCam.enabled = false;
                    KS3P.currentScene = KS3P.Scene.EVA;
                    KS3P.Register(mainCam, KS3P.Scene.EVA);
                    activeState = State.EVA;
                }
            }
            else
            {
                if (activeState != State.Flight)
                {
                    scaledCam.enabled = false;
                    KS3P.currentScene = KS3P.Scene.Flight;
                    KS3P.Register(mainCam, KS3P.Scene.Flight);
                    activeState = State.Flight;
                }
            }
        }
    }
}
