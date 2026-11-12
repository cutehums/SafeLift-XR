using UnityEngine;

/// Keeps the world-space dashboard comfortably in front of the Quest headset.
/// It is intentionally smooth so the panel does not snap when the user turns.
public sealed class QuestDashboardFollow : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private float distance = 1.7f;
    [SerializeField] private float verticalOffset = -0.05f;
    [SerializeField] private float positionSmooth = 7f;
    [SerializeField] private float rotationSmooth = 9f;
    [SerializeField] private float followYawOnly = 1f;
    [SerializeField] private bool followContinuously = false;

    private Vector3 velocity;

    private void Awake()
    {
        ResolveHead();
        Recenter();
        var legacyBillboard = GetComponent<DashboardBillboard>();
        if (legacyBillboard != null) legacyBillboard.enabled = false;
    }

    private void LateUpdate()
    {
        if (!ResolveHead() || !followContinuously) return;

        var forward = head.forward;
        forward.y *= followYawOnly;
        if (forward.sqrMagnitude < 0.001f) forward = head.forward;
        forward.Normalize();

        var desiredPosition = head.position + forward * distance + Vector3.up * verticalOffset;
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, 1f / Mathf.Max(.1f, positionSmooth));

        var toHead = head.position - transform.position;
        toHead.y = 0f;
        if (toHead.sqrMagnitude > 0.001f)
        {
            var desiredRotation = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime));
        }
    }

    public void Recenter()
    {
        if (!ResolveHead()) return;
        var forward = head.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
        forward.Normalize();
        transform.position = head.position + forward * distance + Vector3.up * verticalOffset;
        var toHead = head.position - transform.position;
        toHead.y = 0f;
        if (toHead.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
        velocity = Vector3.zero;
    }

    private bool ResolveHead()
    {
        if (head != null) return true;
        if (Camera.main != null) head = Camera.main.transform;
        if (head == null)
        {
            var camera = Object.FindObjectOfType<Camera>();
            if (camera != null) head = camera.transform;
        }
        return head != null;
    }
}
