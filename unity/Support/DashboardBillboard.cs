using UnityEngine;

public class DashboardBillboard : MonoBehaviour
{
    [SerializeField] private Transform head; [SerializeField] private bool faceUser = false; [SerializeField] private float smoothTime = .15f;
    private void Start() { if (head == null && Camera.main != null) head = Camera.main.transform; }
    private void LateUpdate()
    {
        if (!faceUser || head == null) return;
        var direction = transform.position - head.position; direction.y = 0f;
        if (direction.sqrMagnitude < .0001f) return;
        var target = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(.01f, smoothTime)));
    }
    public void Recenter() { if (head == null) return; transform.position = head.position + head.forward * 1.7f + Vector3.down * .1f; transform.rotation = Quaternion.LookRotation((transform.position - head.position).normalized, Vector3.up); }
}
