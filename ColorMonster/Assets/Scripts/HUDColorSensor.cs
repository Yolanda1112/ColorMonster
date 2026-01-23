// ファイル名: HUDColorSensor.cs
//
// 目的:
//  - ColorSensorToPlayerController.DebugChannels[0..3] を読んで
//    1) 各chの「生RGB」を色として表示（raw swatch）
//    2) 各chの「単色判定(baseColor)」を文字で表示
//    3) 全ch共通の「最終判定(finalColor)」を色として表示（final swatch）
//    4) ついでに数値(R,G,B,C)も表示
//
// 注意:
//  - このスクリプトは SerialPort を開きません（読むだけ）
//  - UIは「自動生成」するので、Canvas/Panelを手で作らなくても動きます
//  - もし「パネルがデカすぎる」なら inspector の panelSize を小さくしてください

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDColorSensor : MonoBehaviour
{
    [Header("Source (Reader)")]
    [SerializeField] private ColorSensorToPlayerController source;

    [Header("UI Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(420, 260);  // ここ小さくすると画面を隠さない
    [SerializeField] private Vector2 panelOffset = new Vector2(12, -12); // 左上からのオフセット(px)
    [SerializeField] private float rowHeight = 60f;
    [SerializeField] private float swatchSize = 28f;

    [Header("Behavior")]
    [SerializeField] private bool showOnlyActive = false; // true: hasData=falseのch行を薄くする

    // 生成したUI参照
    private Canvas _canvas;
    private RectTransform _panel;
    private Image[] _rawSwatch = new Image[4];
    private Image[] _finalSwatch = new Image[4];
    private TMP_Text[] _text = new TMP_Text[4];

    void Awake()
    {
        // source が未指定なら探す
        if (source == null) source = FindObjectOfType<ColorSensorToPlayerController>();

        BuildUI();
    }

    void Update()
    {
        if (source == null) return;
        var dbg = source.DebugChannels;
        if (dbg == null || dbg.Length < 4) return;

        for (int ch = 0; ch < 4; ch++)
        {
            var d = dbg[ch];

            // --- 生RGB（raw） ---
            Color raw = new Color(d.r / 255f, d.g / 255f, d.b / 255f, 1f);
            _rawSwatch[ch].color = d.hasData ? raw : new Color(0, 0, 0, 0.35f);

            // --- 最終判定（final） ---
            Color finalCol = NameToColor(d.finalColor);
            _finalSwatch[ch].color = (d.finalColor == "none") ? new Color(0, 0, 0, 0.2f) : finalCol;

            // --- テキスト ---
            _text[ch].text =
                $"CH {ch}\n" +
                $"R:{d.r} G:{d.g} B:{d.b} C:{d.c}\n" +
                $"base:{d.baseColor}  final:{d.finalColor}";

            // --- 非アクティブ表示（任意） ---
            if (showOnlyActive && !d.hasData)
            {
                SetRowAlpha(ch, 0.25f);
            }
            else
            {
                SetRowAlpha(ch, 1f);
            }
        }
    }

    // -------------------------
    // UI生成
    // -------------------------
    private void BuildUI()
    {
        // Canvas を探す（無ければ作る）
        _canvas = FindObjectOfType<Canvas>();
        if (_canvas == null)
        {
            var go = new GameObject("HUDCanvas");
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<GraphicRaycaster>();

            // 反転して見える事故を避ける（スケールを正に戻す）
            go.transform.localScale = Vector3.one;
            go.transform.rotation = Quaternion.identity;
        }

        // Panel
        var panelGO = new GameObject("ColorSensorHUDPanel");
        panelGO.transform.SetParent(_canvas.transform, false);

        _panel = panelGO.AddComponent<RectTransform>();
        _panel.anchorMin = new Vector2(0, 1); // 左上
        _panel.anchorMax = new Vector2(0, 1);
        _panel.pivot = new Vector2(0, 1);
        _panel.anchoredPosition = panelOffset;
        _panel.sizeDelta = panelSize;
        _panel.localScale = Vector3.one; // 反転対策

        var bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.35f);

        var layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 4行作る
        for (int ch = 0; ch < 4; ch++)
        {
            CreateRow(panelGO.transform, ch);
        }
    }

    private void CreateRow(Transform parent, int ch)
    {
        // Row container
        var rowGO = new GameObject($"Row_CH{ch}");
        rowGO.transform.SetParent(parent, false);

        var rowRT = rowGO.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, rowHeight);
        rowRT.localScale = Vector3.one; // 反転対策

        var rowBG = rowGO.AddComponent<Image>();
        rowBG.color = new Color(0, 0, 0, 0.25f);

        var h = rowGO.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 6, 6);
        h.spacing = 10;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlHeight = true;
        h.childControlWidth = true;
        h.childForceExpandHeight = false;
        h.childForceExpandWidth = true;

        // Raw swatch
        _rawSwatch[ch] = CreateSwatch(rowGO.transform, $"RawSwatch_CH{ch}", swatchSize);

        // Final swatch
        _finalSwatch[ch] = CreateSwatch(rowGO.transform, $"FinalSwatch_CH{ch}", swatchSize);

        // Text
        _text[ch] = CreateTMP(rowGO.transform, $"Text_CH{ch}");
    }

    private Image CreateSwatch(Transform parent, string name, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.localScale = Vector3.one; // 反転対策

        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.2f);

        // Layout用
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size;
        le.preferredHeight = size;

        return img;
    }

    private TMP_Text CreateTMP(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.localScale = Vector3.one; // 反転対策

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;
        tmp.text = "CH\nR:0 G:0 B:0 C:0\nbase:none final:none";

        return tmp;
    }

    private void SetRowAlpha(int ch, float a)
    {
        // swatch/textだけ薄くしたいので、個別にalphaを掛ける
        var rc = _rawSwatch[ch].color;   rc.a = Mathf.Clamp01(rc.a * a);   _rawSwatch[ch].color = rc;
        var fc = _finalSwatch[ch].color; fc.a = Mathf.Clamp01(fc.a * a);  _finalSwatch[ch].color = fc;
        var tc = _text[ch].color;        tc.a = Mathf.Clamp01(a);         _text[ch].color = tc;
    }

    // finalColor 名から UnityEngine.Color へ
    private Color NameToColor(string name)
    {
        switch (name)
        {
            case "red":    return Color.red;
            case "blue":   return Color.blue;
            case "yellow": return Color.yellow;
            case "purple": return new Color(0.55f, 0.2f, 0.75f, 1f);
            case "orange": return new Color(1f, 0.55f, 0.0f, 1f);
            case "green":  return Color.green;
            default:       return new Color(0, 0, 0, 0.2f); // none
        }
    }
}
