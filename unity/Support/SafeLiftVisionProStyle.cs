using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class SafeLiftVisionProStyle
{
    private static Material windowMaterial;
    private static Material panelMaterial;
    private static Material buttonMaterial;

    public static void Apply(Transform root)
    {
        if (root == null) return;

        LoadVisionKitMaterials();

        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image == null) continue;

            // The imported Vision Pro materials can ignore Image.color and keep
            // the panel bright/translucent. SafeLift uses explicit runtime colors
            // so hover/click feedback remains visible in Quest.
            image.material = null;

            if (image.GetComponent<Button>() != null || image.name.IndexOf("Button", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                image.color = new Color(.055f, .085f, .135f, .98f);
                continue;
            }

            if (image.name.IndexOf("MainCard", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                image.color = new Color(.018f, .028f, .05f, .97f);
                continue;
            }

            if (image.name.IndexOf("Frame", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                image.name.IndexOf("Panel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                image.color = new Color(.028f, .045f, .075f, .96f);
        }

        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == null) continue;
            SafeLiftKoreanText.Apply(text);
            text.margin = new Vector4(6f, 4f, 6f, 4f);
            if (text.fontSize > 18f) text.fontStyle = FontStyles.Bold;
        }
    }

    private static void LoadVisionKitMaterials()
    {
        if (windowMaterial != null || panelMaterial != null || buttonMaterial != null) return;

#if UNITY_EDITOR
        const string basePath = "Assets/JetXR/UI Kit For Vision Pro OS/Runtime/Materials/";
        windowMaterial = AssetDatabase.LoadAssetAtPath<Material>(basePath + "WindowBlurredBackground.mat");
        panelMaterial = AssetDatabase.LoadAssetAtPath<Material>(basePath + "DarkElementBackground.mat");
        buttonMaterial = AssetDatabase.LoadAssetAtPath<Material>(basePath + "LightElementBackgroundWithFrame.mat");
#endif
    }
}
