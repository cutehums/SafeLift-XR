using UnityEngine;

/// <summary>
/// Deterministic editor-only controller affordance.  Desktop Game View has no
/// tracked Touch device, so Meta's controller prefab and XR pointer ray are
/// legitimately hidden there.  This small overlay keeps the dashboard's
/// joystick/controller cue visible while testing the UI without a headset.
/// </summary>
public sealed class SafeLiftDesktopJoystickOverlay : MonoBehaviour
{
    private Texture2D circle;
    private Texture2D ring;
    private Texture2D pixel;

    private void Awake()
    {
#if UNITY_EDITOR
        circle = MakeDisc(96, 0.30f);
        ring = MakeRing(96, 0.16f, 0.75f);
        pixel = Texture2D.whiteTexture;
        Debug.Log("SafeLift desktop joystick overlay active: editor Game View controller cue enabled.");
#else
        enabled = false;
#endif
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        if (circle) Destroy(circle);
        if (ring) Destroy(ring);
#endif
    }

    private void OnGUI()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying || circle == null || ring == null) return;
        GUI.depth = -10000;
        var size = Mathf.Clamp(Screen.height * 0.13f, 72f, 128f);
        var y = Screen.height - size * 1.35f;
        DrawJoystick(new Rect(size * 0.30f, y, size, size), new Color(0.08f, 0.12f, 0.16f, 0.86f));
        DrawJoystick(new Rect(Screen.width - size * 1.30f, y, size, size), new Color(0.08f, 0.12f, 0.16f, 0.86f));
#endif
    }

#if UNITY_EDITOR
    private void DrawJoystick(Rect rect, Color tint)
    {
        GUI.color = tint;
        GUI.DrawTexture(rect, circle, ScaleMode.StretchToFill, true);
        GUI.color = new Color(0.10f, 0.78f, 0.92f, 0.92f);
        GUI.DrawTexture(new Rect(rect.x + rect.width * 0.38f, rect.y + rect.height * 0.20f, rect.width * 0.24f, rect.height * 0.45f), pixel);
        GUI.DrawTexture(new Rect(rect.x + rect.width * 0.29f, rect.y + rect.height * 0.10f, rect.width * 0.42f, rect.width * 0.42f), ring);
        GUI.color = Color.white;
    }

    private static Texture2D MakeDisc(int size, float alpha)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = (size - 1) * 0.5f;
        var radius = center - 1f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                texture.SetPixel(x, y, distance <= radius ? new Color(1f, 1f, 1f, alpha) : Color.clear);
            }
        texture.Apply();
        return texture;
    }

    private static Texture2D MakeRing(int size, float thickness, float alpha)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = (size - 1) * 0.5f;
        var outer = center - 1f;
        var inner = outer * (1f - thickness);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                texture.SetPixel(x, y, distance <= outer && distance >= inner ? new Color(1f, 1f, 1f, alpha) : Color.clear);
            }
        texture.Apply();
        return texture;
    }
#endif
}
