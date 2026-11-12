using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bridges imported visual assets into the runtime-created SafeLift UI.
/// The main flow still uses one state-machine-owned card so screens do not overlap;
/// this adapter only decorates the final review with a score gauge and SET comparison.
/// </summary>
public sealed class SafeLiftVisualAssetAdapter : MonoBehaviour
{
    private GameObject panel;
    private TMP_Text title;
    private TMP_Text scoreText;
    private TMP_Text detailText;
    private Image scoreRing;
    private Image set1Bar;
    private Image set2Bar;
    private Material radialMaterial;

    public void Attach(Transform parent)
    {
        if (parent == null || panel != null) return;

        panel = new GameObject("SafeLiftAssetVisualPanel");
        panel.transform.SetParent(parent, false);
        panel.SetActive(false);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(.025f, .055f, .10f, .98f);
        var outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(.15f, .82f, 1f, .88f);
        outline.effectDistance = new Vector2(3f, -3f);
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300f, 286f);
        rt.anchoredPosition = new Vector2(250f, 16f);

        var accent = new GameObject("ScoreAccent");
        accent.transform.SetParent(panel.transform, false);
        var accentRect = accent.AddComponent<RectTransform>();
        accentRect.sizeDelta = new Vector2(260f, 8f);
        accentRect.anchoredPosition = new Vector2(0f, 132f);
        accent.AddComponent<Image>().color = new Color(.1f, .82f, 1f, .95f);
        accent.transform.SetAsFirstSibling();

        title = Text(panel.transform, "AssetVisualTitle", "최종 점수 · 세트 비교", new Vector2(0f, 118f), 17f);
        title.color = new Color(.9f, .99f, 1f, 1f);

        scoreRing = FilledImage(panel.transform, "RadialScoreGauge", new Vector2(-88f, 26f), new Vector2(116f, 116f), Image.FillMethod.Radial360);
        scoreRing.color = new Color(.15f, .85f, 1f, .95f);
        var scoreBadge = new GameObject("ScoreBadge");
        scoreBadge.transform.SetParent(panel.transform, false);
        var badgeRect = scoreBadge.AddComponent<RectTransform>();
        badgeRect.sizeDelta = new Vector2(140f, 140f);
        badgeRect.anchoredPosition = new Vector2(-88f, 26f);
        scoreBadge.AddComponent<Image>().color = new Color(.04f, .12f, .22f, .96f);
        scoreBadge.transform.SetAsFirstSibling();
        radialMaterial = CreateRadialMaterial();
        if (radialMaterial != null) scoreRing.material = radialMaterial;

        scoreText = Text(panel.transform, "ScoreGaugeText", "0\n점", new Vector2(-88f, 20f), 25f);
        scoreText.alignment = TextAlignmentOptions.Center;

        CreateLabel(panel.transform, "SET 1", new Vector2(3f, 64f));
        set1Bar = FilledImage(panel.transform, "XChartsSet1BarFallback", new Vector2(74f, 64f), new Vector2(140f, 16f), Image.FillMethod.Horizontal);
        set1Bar.color = new Color(.35f, .58f, 1f, .9f);

        CreateLabel(panel.transform, "SET 2", new Vector2(3f, 28f));
        set2Bar = FilledImage(panel.transform, "XChartsSet2BarFallback", new Vector2(74f, 28f), new Vector2(140f, 16f), Image.FillMethod.Horizontal);
        set2Bar.color = new Color(.2f, 1f, .72f, .95f);

        detailText = Text(panel.transform, "AssetVisualDetail", "리포트 데이터를 기다리는 중입니다.", new Vector2(0f, -84f), 10.5f);
        detailText.rectTransform.sizeDelta = new Vector2(264f, 78f);
        detailText.alignment = TextAlignmentOptions.TopLeft;
        detailText.color = new Color(.84f, .96f, 1f, .95f);
    }

    public void SetVisible(bool visible)
    {
        if (panel != null) panel.SetActive(visible);
    }

    public void ApplyReport(SafeLiftState state, SafeLiftSetComparison comparison, SafeLiftDemoScenario scenario, SafeLiftScoreResult unityScore = null)
    {
        if (panel == null) return;

        var score = state != null ? state.score_summary : null;
        var safeLiftScore = unityScore != null ? Mathf.RoundToInt(unityScore.totalScore) : 0;
        var improvementDelta = score != null ? score.improvement_delta : 0;
        var set1 = unityScore != null ? safeLiftScore : 0;
        var set2 = unityScore != null ? safeLiftScore : 0;

        SetScoreGauge(safeLiftScore);
        if (set1Bar != null) set1Bar.fillAmount = Mathf.Clamp01(set1 / 100f);
        if (set2Bar != null) set2Bar.fillAmount = Mathf.Clamp01(set2 / 100f);

        var delta = set2 - set1;
        var xchartsReady = IsXChartsImported() ? "import 확인" : "fallback 표시";
        var radialReady = radialMaterial != null ? "셰이더 적용" : "UGUI 게이지";
        if (detailText != null)
        {
            detailText.text =
                "RadialProgressBar: " + radialReady + "\n" +
                "XCharts: " + xchartsReady + " / SET 비교 데이터 연결\n" +
                "SET 1 예상: " + Mathf.RoundToInt(set1) + "점\n" +
                "SET 2 예상: " + Mathf.RoundToInt(set2) + "점 (" + (delta >= 0 ? "+" : "") + Mathf.RoundToInt(delta) + ")\n" +
                "UI Kit: Vision Pro 스타일 적용";
        }
    }

    public void ApplyVisionStyle(Transform root)
    {
        if (root == null) return;

        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image == null) continue;
            var name = image.gameObject.name;
            if (name.Contains("Button") || image.GetComponent<Button>() != null)
            {
                image.color = new Color(.16f, .50f, .92f, .94f);
            }
            else if (name.Contains("Frame") || name.Contains("Card") || name.Contains("Panel"))
            {
                var color = image.color;
                image.color = new Color(
                    Mathf.Max(color.r, .08f),
                    Mathf.Max(color.g, .12f),
                    Mathf.Max(color.b, .18f),
                    Mathf.Max(color.a, .74f));
            }
        }
    }

    private void SetScoreGauge(int score)
    {
        var normalized = Mathf.Clamp01(score / 100f);
        if (scoreRing != null) scoreRing.fillAmount = normalized;
        if (radialMaterial != null) radialMaterial.SetFloat("_Fillpercentage", normalized);
        if (scoreText != null) scoreText.text = score + "\n점";
    }

    private static void CreateLabel(Transform parent, string value, Vector2 pos)
    {
        var label = Text(parent, "Label_" + value.Replace(" ", ""), value, pos, 10f);
        label.rectTransform.sizeDelta = new Vector2(48f, 20f);
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.color = new Color(.78f, .92f, 1f, .95f);
    }

    private static Image FilledImage(Transform parent, string name, Vector2 pos, Vector2 size, Image.FillMethod fillMethod)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var image = go.AddComponent<Image>();
        image.type = Image.Type.Filled;
        image.fillMethod = fillMethod;
        image.fillOrigin = fillMethod == Image.FillMethod.Radial360 ? (int)Image.Origin360.Top : (int)Image.OriginHorizontal.Left;
        image.fillAmount = 0f;
        return image;
    }

    private static TMP_Text Text(Transform parent, string name, string value, Vector2 pos, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(260f, 48f);
        var text = go.AddComponent<TextMeshProUGUI>();
        SafeLiftKoreanText.Apply(text);
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    private static Material CreateRadialMaterial()
    {
        var shader = Shader.Find("AdultLink/RadialProgressBar/SimpleRadialProgressBar_UI");
        if (shader == null) return null;

        var material = new Material(shader);
        material.SetFloat("_Radius", .34f);
        material.SetFloat("_Arcrange", 330f);
        material.SetFloat("_Rotation", 195f);
        material.SetFloat("_Globalopacity", 1f);
        material.SetColor("_Barmincolor", new Color(.1f, .55f, 1f, 1f));
        material.SetColor("_Barmaxcolor", new Color(.2f, 1f, .7f, 1f));
        return material;
    }

    private static bool IsXChartsImported()
    {
        return System.Type.GetType("XCharts.Runtime.BarChart, XCharts.Runtime") != null ||
               System.Type.GetType("XCharts.Runtime.LineChart, XCharts.Runtime") != null ||
               System.Type.GetType("XCharts.Runtime.PieChart, XCharts.Runtime") != null;
    }

    private static float EstimateSetScore(SafeLiftSetComparison comparison, bool set2, int fallbackScore, int improvementDelta)
    {
        if (comparison == null) return set2 ? fallbackScore : Mathf.Max(0, fallbackScore - improvementDelta);

        var improvement = Mathf.Max(0, improvementDelta);
        var errorReductionBonus = comparison.error_count_delta < 0 ? Mathf.Abs(comparison.error_count_delta) * 2 : 0;
        var baseScore = Mathf.Clamp(fallbackScore - improvement - errorReductionBonus, 0, 100);
        return set2 ? Mathf.Clamp(baseScore + improvement + errorReductionBonus, 0, 100) : baseScore;
    }

    private static int DemoScoreFor(SafeLiftDemoScenario scenario)
    {
        switch (scenario)
        {
            case SafeLiftDemoScenario.KneeValgus: return 76;
            case SafeLiftDemoScenario.WeightShift: return 78;
            case SafeLiftDemoScenario.CorePressureDrop: return 74;
            case SafeLiftDemoScenario.ComplexError: return 68;
            default: return 86;
        }
    }
}
