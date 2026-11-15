using XingShuRuntime.Core;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.UI;

internal sealed class XingShuHotkeySettingsView
{
    internal GameObject Root { get; private set; }
    private readonly Text[] _labels;
    private readonly Text _status;
    private int _revision = -1, _capturing = -2;
    private string _lastStatus;

    internal XingShuHotkeySettingsView(Transform parent)
    {
        Root = XingShuUIFactory.CreateUIObject("Page_Hotkeys", parent.gameObject);
        RectTransform root = Root.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        XingShuUIFactory.CreateVerticalScrollView(Root, "HotkeyScroll", out GameObject content);
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10f; layout.padding = new RectOffset(0, 10, 0, 20);
        GameObject header = Card(content, "Header", 100f);
        Text title = XingShuUIFactory.CreateText(header, "Title", "快捷键设置", 20, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
        Place(title.gameObject, 0f, 1f, 20f, -8f, -220f, 32f);
        Text help = XingShuUIFactory.CreateText(header, "Help", "即时保存 · 支持组合键 · Esc 取消修改\n自由镜头方向键需按住；默认轻按左 Ctrl 快速开关加速。", 13, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
        Place(help.gameObject, 0f, 1f, 20f, -40f, -40f, 52f);
        Button reset = XingShuUIFactory.CreateButton(header, "ResetAll", "恢复全部默认", null, XingShuUITheme.Pink, Color.white, XingShuHotkeys.ResetAll, 13);
        Place(reset.gameObject, 1f, 1f, -176f, -10f, 156f, 32f);
        GameObject statusCard = Card(content, "Status", 72f);
        _status = XingShuUIFactory.CreateText(statusCard, "Text", "", 14, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft);
        Place(_status.gameObject, 0f, 1f, 20f, 0f, -40f, 72f);
        _labels = new Text[XingShuHotkeys.Count];
        for (int i = 0; i < _labels.Length; i++)
        {
            int index = i;
            var binding = XingShuHotkeys.GetAt(i);
            GameObject row = Card(content, "Shortcut_" + binding.Action, 94f);
            Text name = XingShuUIFactory.CreateText(row, "Name", binding.Name, 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(name.gameObject, 0f, 1f, 20f, -4f, -40f, 32f);
            Button change = XingShuUIFactory.CreateButton(row, "Bind", binding.Display, null, XingShuUITheme.Pink, Color.white,
                () => XingShuHotkeys.BeginCapture(index), 14);
            Place(change.gameObject, 0f, 1f, 20f, -42f, -210f, 38f);
            _labels[i] = change.GetComponentInChildren<Text>();
            Button restore = XingShuUIFactory.CreateButton(row, "Default", "默认", null, XingShuUITheme.Card, XingShuUITheme.PrimaryText,
                () => Set(index, true), 13);
            Place(restore.gameObject, 1f, 1f, -174f, -42f, 72f, 38f);
            Button clear = XingShuUIFactory.CreateButton(row, "Clear", "清除", null, XingShuUITheme.Card, XingShuUITheme.PrimaryText,
                () => Set(index, false), 13);
            Place(clear.gameObject, 1f, 1f, -92f, -42f, 72f, 38f);
            clear.interactable = binding.Action != XingShuShortcut.Center;
        }
        UpdateSync();
    }

    private static GameObject Card(GameObject parent, string name, float height)
    {
        GameObject card = XingShuUIFactory.CreateUIObject(name, parent);
        LayoutElement element = card.AddComponent<LayoutElement>();
        element.preferredHeight = element.minHeight = height; element.flexibleHeight = 0;
        Image image = card.AddComponent<Image>();
        image.sprite = XingShuUIAssetService.GetSprite("card_base.png"); image.type = Image.Type.Sliced;
        image.color = XingShuUIAssetService.IsFallbackSprite(image.sprite) ? XingShuUITheme.Card : Color.white;
        return card;
    }

    private static void Place(GameObject go, float minX, float maxX, float x, float y, float width, float height)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(minX, 1f); rt.anchorMax = new Vector2(maxX, 1f);
        rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, height);
    }

    private void Set(int index, bool defaults)
    {
        XingShuHotkeys.CancelCapture();
        var binding = XingShuHotkeys.GetAt(index);
        string error;
        bool changed = XingShuHotkeys.TrySet(binding.Action, defaults ? binding.DefaultKey : KeyCode.None,
            defaults ? binding.DefaultModifiers : XingShuHotkeyModifiers.None, out error);
        _status.text = changed ? "已保存「" + binding.Name + "」：" + binding.Display : error;
        _lastStatus = XingShuHotkeys.Status;
    }

    internal void UpdateSync()
    {
        if (_revision != XingShuHotkeys.Revision || _capturing != XingShuHotkeys.CapturingIndex)
        {
            _revision = XingShuHotkeys.Revision; _capturing = XingShuHotkeys.CapturingIndex;
            for (int i = 0; i < _labels.Length; i++)
                _labels[i].text = i == _capturing ? "请按键…（Esc 取消）" : XingShuHotkeys.GetAt(i).Display;
        }
        if (_lastStatus != XingShuHotkeys.Status)
        { _lastStatus = XingShuHotkeys.Status; _status.text = _lastStatus; }
    }
}
