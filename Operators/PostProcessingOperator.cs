using UnityEngine;

namespace KSP_PostProcessing.Operators
{
    public abstract class PostProcessingOperator : MonoBehaviour
    {
        protected abstract void Start();

        protected internal void Patch(bool scaled, KS3P.Scene target)
        {
            GameObject cam = scaled ? ScaledCamera.Instance.gameObject : Camera.main.gameObject;

            if (cam)
            {
                if (!scaled)
                {
                    GameObject scaledCam = ScaledCamera.Instance.gameObject;
                    if (scaledCam)
                    {
                        scaledCam.AddOrGetComponent<PostProcessingBehaviour>().enabled = false;
                    }
                }

                KS3P.Register(cam.AddOrGetComponent<PostProcessingBehaviour>(), target);
            }
        }
    }
}
