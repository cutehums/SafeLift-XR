using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public sealed class SafeLiftXRInputManager : MonoBehaviour
{
    [SerializeField] private SafeLiftXRHapticsManager haptics;
    [SerializeField] private float navigationRepeatSeconds = .28f;
    [SerializeField] private float stickThreshold = .65f;

    private InputDevice left;
    private InputDevice right;
    private Button selectedButton;
    private SafeLiftXRPointerVisualizer pointer;
    private Image selectedImage;
    private Color selectedOriginalColor;
    private float nextNavigationAt;
    private bool lastA;
    private bool lastTrigger;

    private void Awake()
    {
        if (!haptics) haptics = GetComponent<SafeLiftXRHapticsManager>() ?? gameObject.AddComponent<SafeLiftXRHapticsManager>();
        pointer = GetComponent<SafeLiftXRPointerVisualizer>();
        FindDevices();
        SelectDefaultButton();
    }

    private void Update()
    {
        if (!left.isValid || !right.isValid) FindDevices();
        if (pointer == null) pointer = GetComponent<SafeLiftXRPointerVisualizer>();

        // UI panels are created and enabled after this component's Awake. Rebuild
        // the selection whenever a state transition replaces the active buttons.
        var activeButtons = ActiveButtons();
        if (selectedButton == null || !selectedButton.IsActive() || !selectedButton.interactable || !activeButtons.Contains(selectedButton))
        {
            if (activeButtons.Count > 0) Select(activeButtons[0]);
            else ClearSelection();
        }

        var stick = ReadNavigationStick();
        if (stick.magnitude >= stickThreshold && Time.unscaledTime >= nextNavigationAt)
        {
            Navigate(stick);
            nextNavigationAt = Time.unscaledTime + navigationRepeatSeconds;
        }

        if (stick.magnitude < .25f)
            nextNavigationAt = 0f;

        var a = Button(right, CommonUsages.primaryButton) || OvrButtonOne();
        if (a && !lastA)
            ClickSelected();

        lastA = a;

        var trigger = Trigger(right) || OvrPrimaryIndexTrigger();
        if (trigger && !lastTrigger)
        {
            // First activate the button physically under the ray. If the pose is
            // lost for a frame in Quest Link, keep the trigger usable via the
            // currently joystick-selected button.
            if (pointer == null || !pointer.TryClickPointedButton())
                ClickSelected();
        }
        lastTrigger = trigger;

        if (Input.GetKeyDown(KeyCode.Tab)) Navigate(Vector2.right);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) ClickSelected();
    }

    private Vector2 ReadNavigationStick()
    {
        Vector2 value;
        if (right.isValid && right.TryGetFeatureValue(CommonUsages.primary2DAxis, out value) && value.magnitude > .1f)
            return value;
        if (left.isValid && left.TryGetFeatureValue(CommonUsages.primary2DAxis, out value) && value.magnitude > .1f)
            return value;
        value = OvrThumbstick();
        if (value.magnitude > .1f)
            return value;
        return Vector2.zero;
    }

    private void Navigate(Vector2 direction)
    {
        var buttons = ActiveButtons();
        if (buttons.Count == 0)
        {
            ClearSelection();
            return;
        }

        var current = selectedButton != null ? buttons.IndexOf(selectedButton) : -1;
        var horizontal = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y);
        var step = horizontal
            ? (direction.x >= 0f ? 1 : -1)
            : (direction.y <= 0f ? 1 : -1);
        var next = current < 0 ? 0 : (current + step + buttons.Count) % buttons.Count;
        Select(buttons[next]);
    }

    private void SelectDefaultButton()
    {
        var buttons = ActiveButtons();
        if (buttons.Count > 0) Select(buttons[0]);
    }

    private void ClickSelected()
    {
        if (selectedButton == null || !selectedButton.IsActive() || !selectedButton.interactable)
            SelectDefaultButton();

        if (selectedButton == null) return;
        haptics?.ButtonClick();
        selectedButton.onClick.Invoke();
    }

    private void Select(Button button)
    {
        if (button == selectedButton) return;

        ClearSelection();
        selectedButton = button;
        selectedImage = selectedButton != null ? selectedButton.GetComponent<Image>() : null;
        if (selectedImage != null)
        {
            selectedOriginalColor = selectedImage.color;
            selectedImage.color = new Color(1f, .72f, .16f, 1f);
        }

        haptics?.ButtonClick();
    }

    private void ClearSelection()
    {
        if (selectedImage != null) selectedImage.color = selectedOriginalColor;
        selectedImage = null;
        selectedButton = null;
    }

    private List<Button> ActiveButtons()
    {
        var result = new List<Button>();
        var canvas = FindSafeLiftCanvas();
        if (canvas == null) return result;

        foreach (var button in canvas.GetComponentsInChildren<Button>(false))
        {
            if (button == null || !button.IsActive() || !button.interactable) continue;
            result.Add(button);
        }

        result.Sort((a, b) =>
        {
            var ar = a.GetComponent<RectTransform>();
            var br = b.GetComponent<RectTransform>();
            if (ar == null || br == null) return 0;
            var ay = ar.position.y;
            var by = br.position.y;
            if (Mathf.Abs(ay - by) > .02f) return -ay.CompareTo(by);
            return ar.position.x.CompareTo(br.position.x);
        });
        return result;
    }

    private static Canvas FindSafeLiftCanvas()
    {
        foreach (var canvas in FindObjectsOfType<Canvas>(true))
        {
            if (canvas != null && canvas.name == "SafeLiftDemoWorldCanvas" && canvas.gameObject.activeInHierarchy)
                return canvas;
        }
        return null;
    }

    private void FindDevices()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, devices);
        if (devices.Count > 0) left = devices[0];
        devices.Clear();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);
        if (devices.Count > 0) right = devices[0];
    }

    private static bool Button(InputDevice device, InputFeatureUsage<bool> usage)
    {
        bool value;
        return device.isValid && device.TryGetFeatureValue(usage, out value) && value;
    }

    private static bool Trigger(InputDevice device)
    {
        if (Button(device, CommonUsages.triggerButton)) return true;
        float value;
        return device.isValid && device.TryGetFeatureValue(CommonUsages.trigger, out value) && value >= .55f;
    }

    private static Vector2 OvrThumbstick()
    {
#if UNITY_ANDROID || UNITY_EDITOR
        try
        {
            var right = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
            if (right.magnitude > .1f) return right;
            return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        }
        catch
        {
            return Vector2.zero;
        }
#else
        return Vector2.zero;
#endif
    }

    private static bool OvrButtonOne()
    {
#if UNITY_ANDROID || UNITY_EDITOR
        try
        {
            return OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private static bool OvrPrimaryIndexTrigger()
    {
#if UNITY_ANDROID || UNITY_EDITOR
        try
        {
            return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)
                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) >= .55f
                || OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)
                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch) >= .55f;
        }
        catch { return false; }
#else
        return false;
#endif
    }

}
