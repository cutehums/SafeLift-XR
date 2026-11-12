using UnityEngine;

public class DashboardInputController : MonoBehaviour
{
    [SerializeField] private GameObject dashboardRoot; [SerializeField] private GameObject reportPanel; [SerializeField] private DashboardBillboard billboard;
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Fire1")) Toggle(dashboardRoot);
        if (Input.GetKeyDown(KeyCode.R)) Toggle(reportPanel);
        if (Input.GetKeyDown(KeyCode.C)) billboard?.Recenter();
    }
    private static void Toggle(GameObject target) { if (target != null) target.SetActive(!target.activeSelf); }
}
