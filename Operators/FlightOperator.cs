using UnityEngine;

namespace KSP_PostProcessing.Operators
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FlightOperator : MonoBehaviour
    {
        byte state = 0;
        // 0 = flight
        // 1 = EVA
        // 2 = IVA
        // 3 = Map
        
        private void Start()
        {
            mainCam = Camera.main.gameObject.AddOrGetComponent<PostProcessingBehaviour>();
            scaledCam = GameObject.Find("Camera ScaledSpace").AddOrGetComponent<PostProcessingBehaviour>(); ;
            ivaCam = InternalCamera.Instance;
        }

        PostProcessingBehaviour mainCam;
        PostProcessingBehaviour scaledCam;
        InternalCamera ivaCam;

        private void Update()
        {
            if(!FlightGlobals.ready)
            {
                return;
            }
            if(MapView.MapIsEnabled)
            {
                if(state != 3)
                {
                    KS3P.Register(scaledCam, KS3P.Scene.MapView);
                    KS3P.currentScene = KS3P.Scene.MapView;
                    state = 3;
                }
            }
            else if(ivaCam.isActive)
            {
                if(state != 2)
                {
                    KS3P.Register(mainCam, KS3P.Scene.IVA);
                    KS3P.currentScene = KS3P.Scene.IVA;
                    state = 2;
                }
            }
            else if(FlightGlobals.ActiveVessel.isEVA)
            {
                if(state != 1)
                {
                    KS3P.Register(mainCam, KS3P.Scene.EVA);
                    KS3P.currentScene = KS3P.Scene.EVA;
                    state = 1;
                }
            }
            else
            {
                if(state != 0)
                {
                    KS3P.Register(mainCam, KS3P.Scene.Flight);
                    KS3P.currentScene = KS3P.Scene.Flight;
                    state = 0;
                }
            }
        }
    }
}
