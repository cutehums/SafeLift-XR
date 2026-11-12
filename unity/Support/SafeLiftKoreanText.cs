using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class SafeLiftKoreanText
{
    private static TMP_FontAsset readableFontAsset;

    public static void Apply(TMP_Text text)
    {
        if (text == null) return;

        var font = FontAsset();
        if (font != null)
        {
            text.font = font;
        }
        text.enableWordWrapping = true;
        text.ForceMeshUpdate(false, false);
    }

    public static void ApplyAllSafeLiftTexts()
    {
        foreach (var text in Object.FindObjectsOfType<TMP_Text>(true))
        {
            if (text == null || text.gameObject == null) continue;
            if (!IsSafeLiftUi(text.transform)) continue;
            Apply(text);
        }
    }

    public static void ApplyAllUnder(Transform root)
    {
        if (root == null) return;
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            Apply(text);
    }

    private static bool IsSafeLiftUi(Transform transform)
    {
        var current = transform;
        while (current != null)
        {
            var name = current.name;
            if (name == "SafeLiftDemoWorldCanvas" ||
                name == "SafeLiftXRRuntime" ||
                name == "SafeLiftDashboardRoot" ||
                name == "SafeLiftMainCard" ||
                name.Contains("SafeLift") ||
                name.Contains("QR") ||
                name.Contains("Review") ||
                name.Contains("Tutorial") ||
                name.Contains("Content"))
                return true;
            current = current.parent;
        }

        return false;
    }

    private static TMP_FontAsset FontAsset()
    {
        if (readableFontAsset != null) return readableFontAsset;

        var osFont = Resources.Load<Font>("Fonts/MalgunGothic");
        if (osFont == null)
        {
            osFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial Unicode MS", "Segoe UI" },
                90);
        }
        if (osFont == null)
        {
            Debug.LogWarning("SafeLift Korean TMP font: no Korean-capable OS font was found.");
            return null;
        }

        readableFontAsset = TMP_FontAsset.CreateFontAsset(
            osFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            2048,
            2048,
            AtlasPopulationMode.Dynamic);
        if (readableFontAsset != null)
        {
            readableFontAsset.name = "SafeLiftKoreanRuntimeTMP";
            readableFontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        }

        return readableFontAsset;
    }
}
