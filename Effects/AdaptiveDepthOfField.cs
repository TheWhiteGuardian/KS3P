using UnityEngine;

/*

namespace KSP_PostProcessing.Effects
{
    public class AdaptiveDepthOfField : MonoBehaviour
    {
        DepthOfFieldModel model;
        DepthOfFieldModel.Settings settings;
        Camera cam;
        Transform camTransform;
        RaycastHit hit;
        float defaultDistance;

        void Start()
        {
            model = GetComponent<PostProcessingBehaviour>().profile.depthOfField;
            cam = KS3P.Instance.TargetCamera;
            camTransform = cam.transform;
            defaultDistance = model.settings.focusDistance;
        }

        void Update()
        {
            settings = model.settings;
            if (Physics.Raycast(cam.ScreenPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit))
            {
                settings.focusDistance = Vector3.Distance(hit.point, camTransform.position);
            }
            else
            {
                settings.focusDistance = defaultDistance;
            }
            model.settings = settings;
        }
    }
}

*/