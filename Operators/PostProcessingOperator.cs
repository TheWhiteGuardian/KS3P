using UnityEngine;

namespace KSP_PostProcessing.Operators
{
    public abstract class PostProcessingOperator : MonoBehaviour
    {
        protected abstract void Start();

        protected internal void Patch(bool scaled, KS3P.Scene target)
        {
            GameObject cam = (scaled) ? GameObject.Find("Camera ScaledSpace") : Camera.main.gameObject;

            if(cam)
            {
                KS3P.Register(cam.AddOrGetComponent<PostProcessingBehaviour>(), target);
            }
        }
    }
}
