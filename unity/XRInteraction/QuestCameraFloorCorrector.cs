using UnityEngine;
using UnityEngine.XR;

public sealed class QuestCameraFloorCorrector : MonoBehaviour
{
    [SerializeField] private float editorPreviewHeight = 1.5f;
    [SerializeField] private float minimumEyeHeight = 1.05f;
    [SerializeField] private float floorLift = 0.1f;
    private bool applied;

    private System.Collections.IEnumerator Start()
    {
        var environment = GameObject.Find("IndoorGymEnvironment");
        if (environment != null && environment.transform.position.y < floorLift)
            environment.transform.position = new Vector3(environment.transform.position.x, floorLift, environment.transform.position.z);

        yield return new WaitForSecondsRealtime(.75f);
        var rig = Object.FindObjectOfType<OVRCameraRig>();
        var camera = rig != null && rig.centerEyeAnchor != null
            ? rig.centerEyeAnchor.GetComponent<Camera>()
            : (Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>());
        if (camera != null && camera.transform.position.y < minimumEyeHeight)
        {
            var correction = editorPreviewHeight - camera.transform.position.y;
            var root = transform;
            while (root.parent != null && root.name != "OVRCameraRig" && root.name != "XR Origin" && root.name != "XR Rig")
                root = root.parent;
            root.position += Vector3.up * correction;
        }
        applied = true;
    }

    private void LateUpdate()
    {
        if (!applied || !Application.isEditor) return;
    }
}
