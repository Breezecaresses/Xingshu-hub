using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using APXRuntime.Core;
using APXRuntime.UI;
using Core;
using Core.Scene;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XingShu.Cards;
using Image = UnityEngine.UI.Image;

namespace APXRuntime.Utilities
{
    public sealed class HandCardDock : MonoBehaviour
    {
        private static HandCardDock _instance;
        private GameObject _canvas, _body, _content;
        private RectTransform _panel, _heading, _handle, _viewport;
        private readonly List<RectTransform> _cards = new List<RectTransform>();
        private HandFanPose[] _poses = new HandFanPose[0];
        private int _hovered = -1;
        private float _fanWidth = -1, _fanHeight;
        private Button _collapseButton, _settingsButton, _closeButton;
        private Text _title, _footer;
        private readonly List<TrackedPlayerHand> _hands = new List<TrackedPlayerHand>();
        private readonly Button[] _tabs = new Button[4];
        private int _selected;
        private bool _open, _collapsed;
        private float _next;
        private string _signature;
        private bool _wasValid;
        private int _cardCount;
        private float _cardWidth = 184, _cardHeight = 314;
        private const float CardsTop = 84;
        private float _panelWidth = 404, _panelHeight = 434;
        private Vector2 _position = new Vector2(12, 150);
        internal const float DefaultScale = .6f, MinScale = .4f, MaxScale = 1.2f;
        internal static float Scale
        {
            get => Mathf.Clamp(APXConfigService.GetFloat("XS_HAND_DOCK_SCALE", DefaultScale), MinScale, MaxScale);
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                APXConfigService.SetFloat("XS_HAND_DOCK_SCALE", Mathf.Clamp(value, MinScale, MaxScale));
                APXConfigService.Save();
            }
        }
        internal static bool IsOpen => _instance != null && _instance._open;

        public static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<HandCardDock>() == null) host.AddComponent<HandCardDock>();
        }
        internal static void Toggle()
        {
            SetOpen(!IsOpen, true);
        }
        internal static void SetOpen(bool open, bool closeCenter = false)
        {
            if (_instance == null) return;
            _instance._open = open; _instance._collapsed = false; _instance._next = 0;
            if (_instance._open)
            {
                HandCardDisplayManager.IsEnabled = true;
                foreach (var hand in HandCardDisplayManager.PlayerHands.Values) hand.IsPinned = true;
                HandCardDisplayManager.TriggerFetchCards("FloatingDetailDock");
                if (closeCenter) APXUICenter.Close();
            }
            var ball = UnityEngine.Object.FindObjectOfType<AssistBallGUI>();
            if (ball != null) ball.SetMenuOpen(false);
        }
        private void Awake()
        {
            _instance = this;
            _position = new Vector2(APXConfigService.GetFloat("XS_HAND_DOCK_X", 12), APXConfigService.GetFloat("XS_HAND_DOCK_Y", 150));
            APXOverlayInput.Register(this, () => _canvas != null && _canvas.activeInHierarchy, () => APXOverlayInput.ScreenRect(_panel));
        }
        private void Build()
        {
            APXCentralInputManager.EnsureEventSystemAvailable("HandCardDock");
            _canvas = new GameObject("XingShu_HandDetailDock", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            DontDestroyOnLoad(_canvas);
            var canvas = _canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30018;
            _panel = APXDetailLayout.Box(_canvas.transform, "Dock", 0, 0, _panelWidth, _panelHeight);
            APXDetailLayout.Panel(_panel, "window_frame.png", .78f);
            _heading = APXDetailLayout.Box(_panel, "Header", 0, 0, _panelWidth, 42); APXDetailLayout.Panel(_heading, "header_bar.png");
            _handle = APXDetailLayout.Box(_heading, "Drag", 0, 0, 253, 42); _handle.gameObject.AddComponent<Image>().color = Color.clear;
            _handle.gameObject.AddComponent<HandDockDrag>().Dock = this;
            _title = APXDetailLayout.Text(_heading, "手牌详情", 14, 6, 238, 30, 15); _title.color = Color.white;
            _collapseButton = APXDetailLayout.Button(_heading, "−", 258, 8, 32, 26, () => { _collapsed = !_collapsed; Layout(); });
            _settingsButton = APXDetailLayout.Button(_heading, "设置", 295, 8, 58, 26, () => APXUICenter.OpenHandCardPage());
            _closeButton = APXDetailLayout.Button(_heading, "×", 359, 8, 32, 26, () => _open = false);
            _body = APXUIFactory.CreateUIObject("Body", _panel.gameObject); APXDetailLayout.Fill(_body.GetComponent<RectTransform>());
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                _tabs[i] = APXDetailLayout.Button(_body.transform, "P" + (i + 1), 12 + i * 89, 48, 84, 28, () => { _selected = slot; _signature = null; _next = 0; });
            }
            _viewport = APXDetailLayout.Box(_body.transform, "Viewport", 12, CardsTop, 380, _cardHeight);
            _viewport.gameObject.AddComponent<Image>().color = Color.clear;
            _content = APXUIFactory.CreateUIObject("Cards", _viewport.gameObject);
            APXDetailLayout.Fill(_content.GetComponent<RectTransform>());
            _footer = APXDetailLayout.Text(_body.transform, "选择玩家 · 悬停抬起查看详情", 14, 412, 376, 22, 11);
        }
        private void Update()
        {
            bool valid = _open && HandCardDisplayManager.IsEnabled && (HandCardDisplayManager.IsPVEMode() || XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession()) &&
                SimpleSingletonProvider<SceneManager>.inst.currentType.Value == SceneType.Battle;
            if (!valid)
            {
                if (_canvas != null) _canvas.SetActive(false);
                if (_wasValid) { _signature = null; ClearCards(); }
                _wasValid = false; return;
            }
            try
            {
                if (_canvas == null) Build();
                _canvas.SetActive(true); _wasValid = true; Layout();
                if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + .3f;
                _hands.Clear();
                foreach (var hand in HandCardDisplayManager.PlayerHands.Values) if (hand != null && hand.Slot >= 0 && hand.Slot < 4) _hands.Add(hand);
                _hands.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                var selected = _hands.Find(h => h.Slot == _selected);
                for (int i = 0; i < _tabs.Length; i++) _tabs[i].image.sprite = APXUIAssetService.GetSprite(i == _selected ? "button_selected.png" : "button_secondary.png");
                if (selected == null) { _title.text = "手牌详情 · 等待玩家"; _footer.text = "等待当前玩家手牌同步"; _signature = null; ClearCards(); return; }
                selected.IsPinned = true;
                _title.text = "P" + (_selected + 1) + "  " + selected.PlayerName;
                var signature = new StringBuilder().Append(selected.PlayerId).Append(':').Append(selected.SyncState).Append(':').Append(HandCardDisplayManager.CardScale);
                foreach (var card in selected.Cards) signature.Append('|').Append(card.CardId).Append(':').Append(card.BattleCost).Append(':').Append(card.PurifyNum);
                if (_signature == signature.ToString()) return;
                _signature = signature.ToString(); Render(selected);
            }
            catch (Exception ex) { if (_footer != null) _footer.text = "手牌数据暂未就绪"; Debug.LogWarning("[APX HandDock] " + ex.Message); }
        }
        private void ClearCards()
        {
            if (_content == null) return;
            foreach (Transform child in _content.transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            _cards.Clear(); _poses = new HandFanPose[0]; _hovered = -1; _fanWidth = -1; _cardCount = 0;
        }
        private void Render(TrackedPlayerHand hand)
        {
            ClearCards();
            float cardScale = Mathf.Clamp(HandCardDisplayManager.CardScale, .8f, 1.5f);
            _cardWidth = 184 * cardScale; _cardHeight = 314 * cardScale; _cardCount = 0;
            int unknown = 0;
            foreach (var card in hand.Cards)
            {
                if (card.CardId <= 0) { unknown++; continue; }
                _cardCount++;
                var row = APXUIFactory.CreateUIObject("PortraitCard", _content);
                var rowRt = row.GetComponent<RectTransform>();
                _cards.Add(rowRt);
                var border = APXDetailLayout.Panel(rowRt, "card_base.png"); border.raycastTarget = false;
                border.color = new Color(.85f, .69f, .30f, 1);
                var face = APXDetailLayout.Box(rowRt, "Face", 3, 3, _cardWidth - 6, _cardHeight - 6);
                APXDetailLayout.Fill(face, 3, 3, 3, 3);
                var faceImage = APXDetailLayout.Panel(face); faceImage.raycastTarget = false; faceImage.color = new Color(.09f, .085f, .115f, 1);
                var art = APXDetailLayout.Box(rowRt, "Art", 6 * cardScale, 6 * cardScale, 172 * cardScale, 172 * cardScale).gameObject.AddComponent<RawImage>();
                art.raycastTarget = false; art.color = Color.clear;
                var config = card.Config;
                int type = config == null ? 0 : (int)config.CardType;
                Color accent = CardAccent(type);
                var symbol = APXDetailLayout.Box(rowRt, "Type", 10 * cardScale, 10 * cardScale, 26 * cardScale, 26 * cardScale);
                var symbolImage = APXDetailLayout.Panel(symbol, "button_secondary.png"); symbolImage.color = new Color(.06f, .065f, .085f, .95f); symbolImage.raycastTarget = false;
                var typeText = APXDetailLayout.Text(symbol, CardSymbol(type), 0, 0, 26 * cardScale, 26 * cardScale, Mathf.RoundToInt(17 * cardScale));
                typeText.color = accent; typeText.alignment = TextAnchor.MiddleCenter;
                if (card.BattleCost > 0)
                {
                    string cost = card.BattleCost <= 5 ? new string('◆', card.BattleCost) : "◆ ×" + card.BattleCost;
                    var costPlate = APXDetailLayout.Box(rowRt, "Cost", 10 * cardScale, 40 * cardScale, 88 * cardScale, 22 * cardScale);
                    var costImage = APXDetailLayout.Panel(costPlate, "button_secondary.png"); costImage.raycastTarget = false; costImage.color = new Color(.06f, .065f, .085f, .94f);
                    var costText = APXDetailLayout.Text(costPlate, cost, 4 * cardScale, 0, 80 * cardScale, 22 * cardScale, Mathf.RoundToInt(12 * cardScale));
                    costText.color = accent;
                }
                string name = config != null ? config.NameID.GetLocal(UIStringType.Card) : CardRegistryGenerated.GetCardName(card.CardId);
                var nameplate = APXDetailLayout.Box(rowRt, "Nameplate", 14 * cardScale, 166 * cardScale, 156 * cardScale, 28 * cardScale);
                var nameImage = APXDetailLayout.Panel(nameplate, "button_secondary.png"); nameImage.raycastTarget = false; nameImage.color = accent;
                var title = APXDetailLayout.Text(nameplate, PlainCardText(name), 5 * cardScale, 0, 146 * cardScale, 28 * cardScale, Mathf.RoundToInt(14 * cardScale));
                title.alignment = TextAnchor.MiddleCenter; title.fontStyle = FontStyle.Bold;
                title.resizeTextForBestFit = true; title.resizeTextMinSize = Mathf.RoundToInt(10 * cardScale); title.resizeTextMaxSize = Mathf.RoundToInt(14 * cardScale);
                string desc = config == null ? "卡牌说明暂未加载" : config.DescId.GetLocal(UIStringType.Card);
                var description = APXDetailLayout.Text(rowRt, PlainCardText(desc), 12 * cardScale, 206 * cardScale, 160 * cardScale, 82 * cardScale, Mathf.RoundToInt(12 * cardScale));
                description.color = new Color(.96f, .95f, .98f, 1); description.alignment = TextAnchor.UpperLeft; description.lineSpacing = 1.2f;
                float descriptionHeight = Mathf.Max(82 * cardScale, description.preferredHeight + 4);
                description.rectTransform.sizeDelta = new Vector2(160 * cardScale, descriptionHeight);
                _cardHeight = Mathf.Max(_cardHeight, 232 * cardScale + descriptionHeight);
                var number = APXDetailLayout.Text(rowRt, "#" + card.CardId, 12 * cardScale, 0, 140 * cardScale, 16 * cardScale, Mathf.RoundToInt(9 * cardScale), true);
                number.color = new Color(.75f, .72f, .79f, 1); number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(0, 0);
                number.rectTransform.pivot = Vector2.zero; number.rectTransform.anchoredPosition = new Vector2(12 * cardScale, 7 * cardScale);
                var star = APXDetailLayout.Text(rowRt, "✦", 0, 0, 22 * cardScale, 22 * cardScale, Mathf.RoundToInt(17 * cardScale));
                star.color = Color.white; star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(1, 0);
                star.rectTransform.pivot = new Vector2(1, 0); star.rectTransform.anchoredPosition = new Vector2(-9 * cardScale, 6 * cardScale);
                bool alt; string altTitle;
                bool self = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(hand.PlayerId);
                var candidates = HandCardDisplayManager.GetCardImageCandidates(config, hand, self, out alt, out altTitle);
                if (candidates.Count > 0) APXDetailImages.Load(art, candidates, 0, null, true);
            }
            if (unknown > 0 || hand.Cards.Count == 0)
            {
                _cardCount++;
                var empty = APXUIFactory.CreateUIObject("Unavailable", _content); _cards.Add(empty.GetComponent<RectTransform>());
                APXDetailLayout.Panel(empty.GetComponent<RectTransform>());
                var label = APXDetailLayout.Text(empty.transform, unknown > 0 ? "另有 " + unknown + " 张\n未同步手牌" : "暂无已同步手牌", 12 * cardScale, 96 * cardScale, 160 * cardScale, 100 * cardScale, Mathf.RoundToInt(14 * cardScale), true);
                label.alignment = TextAnchor.MiddleCenter;
            }
            foreach (var card in _cards) card.sizeDelta = new Vector2(_cardWidth, _cardHeight);
            _footer.text = "P" + (hand.Slot + 1) + " · " + hand.Cards.Count + " 张 · " + (hand.IsAuthoritative ? "已同步" : "当前可用数据") + " · 悬停抬起查看详情";
            Layout();
        }
        private static string PlainCardText(string value)
        {
            value = Regex.Replace(value ?? "", "\\{[A-Za-z][A-Za-z0-9_]*=([^{}]+)\\}", "$1");
            value = Regex.Replace(value, "<sprite[^>]*>", " ◆ ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "<[^>]+>|\\[/?(?:color|size|b|i|url)(?:=[^\\]]*)?\\]", "", RegexOptions.IgnoreCase);
            return value.Replace("\\n", "\n");
        }
        private static string CardSymbol(int type)
        {
            switch (type) { case 1: return "攻"; case 2: return "防"; case 3: return "效"; case 4: return "反"; case 5: return "事"; case 6: return "幸"; case 7: return "厄"; case 8: return "咒"; case 9: return "建"; default: return "卡"; }
        }
        private static Color CardAccent(int type)
        {
            switch (type)
            {
                case 1: return new Color(.95f, .37f, .49f, 1);
                case 2: return new Color(.42f, .85f, .94f, 1);
                case 3: return new Color(.78f, .93f, .32f, 1);
                case 4: case 9: return new Color(.90f, .74f, .47f, 1);
                case 5: return new Color(.77f, .64f, .91f, 1);
                default: return new Color(1, .78f, .25f, 1);
            }
        }
        private void Layout()
        {
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), .7f, 1.5f) * Scale;
            Rect safe = APXMobileUIHelper.SafeScreenRect;
            float naturalWidth = _cardWidth + Mathf.Max(0, _cardCount - 1) * _cardWidth * .82f + 144;
            _panelWidth = Mathf.Min(Mathf.Clamp(naturalWidth, 520, 1120), safe.width / scale);
            float fanHeight = HandFanLayout.Headroom + _cardHeight + HandFanLayout.Bottomroom;
            _panelHeight = CardsTop + fanHeight + 30;
            // Fit long descriptions as a complete card rather than cutting off their last lines.
            scale = Mathf.Min(scale, safe.height / (_collapsed ? 42 : _panelHeight));
            _position.x = Mathf.Clamp(_position.x, safe.x, Mathf.Max(safe.x, safe.xMax - _panelWidth * scale));
            _position.y = Mathf.Clamp(_position.y, Screen.height - safe.yMax, Mathf.Max(Screen.height - safe.yMax, Screen.height - safe.yMin - (_collapsed ? 42 : _panelHeight) * scale));
            _panel.anchoredPosition = new Vector2(_position.x, -_position.y); _panel.localScale = new Vector3(scale, scale, 1);
            _panel.sizeDelta = new Vector2(_panelWidth, _collapsed ? 42 : _panelHeight); _body.SetActive(!_collapsed);
            _heading.sizeDelta = new Vector2(_panelWidth, 42); _handle.sizeDelta = new Vector2(_panelWidth - 151, 42);
            _title.rectTransform.sizeDelta = new Vector2(_panelWidth - 166, 30);
            _collapseButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(_panelWidth - 146, -8);
            _settingsButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(_panelWidth - 109, -8);
            _closeButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(_panelWidth - 45, -8);
            float tabWidth = (_panelWidth - 42) / 4;
            for (int i = 0; i < _tabs.Length; i++)
            {
                var tab = _tabs[i].GetComponent<RectTransform>(); tab.anchoredPosition = new Vector2(12 + i * (tabWidth + 6), -48); tab.sizeDelta = new Vector2(tabWidth, 28);
            }
            _viewport.sizeDelta = new Vector2(_panelWidth - 24, fanHeight);
            _footer.rectTransform.anchoredPosition = new Vector2(14, -CardsTop - fanHeight - 4); _footer.rectTransform.sizeDelta = new Vector2(_panelWidth - 28, 22);
            if (_fanWidth != _viewport.sizeDelta.x || _fanHeight != _cardHeight || _poses.Length != _cards.Count)
            {
                _fanWidth = _viewport.sizeDelta.x; _fanHeight = _cardHeight;
                _poses = HandFanLayout.Arrange(_cards.Count, _fanWidth, _cardWidth, _cardHeight);
                ApplyFan();
            }
        }
        private void LateUpdate()
        {
            if (_canvas == null || !_canvas.activeInHierarchy || _collapsed || _poses.Length == 0) return;
            int hover = -1; Vector2 local;
            if (!APXUICenter.IsOpen && RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, Input.mousePosition, null, out local) &&
                local.x >= 0 && local.x <= _viewport.sizeDelta.x && -local.y >= 0 && -local.y <= _viewport.sizeDelta.y)
            {
                hover = HandFanLayout.Hit(_poses, local.x, -local.y, _cardWidth, _cardHeight);
                if (hover < 0 && _hovered >= 0)
                {
                    var raised = _poses[_hovered]; raised.Angle = 0; raised.Y -= 20;
                    if (HandFanLayout.Contains(raised, local.x, -local.y, _cardWidth * 1.08f, _cardHeight * 1.08f)) hover = _hovered;
                }
            }
            if (_hovered == hover) return;
            _hovered = hover; ApplyFan();
        }
        private void ApplyFan()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i]; var pose = _poses[i]; bool raised = i == _hovered;
                card.anchorMin = card.anchorMax = new Vector2(0, 1); card.pivot = new Vector2(.5f, 0);
                card.anchoredPosition = new Vector2(pose.X, -pose.Y + (raised ? 20 : 0));
                card.localEulerAngles = new Vector3(0, 0, raised ? 0 : pose.Angle);
                card.localScale = Vector3.one * (raised ? 1.08f : 1); card.SetSiblingIndex(i);
            }
            if (_hovered >= 0 && _hovered < _cards.Count) _cards[_hovered].SetAsLastSibling();
        }
        internal void Drag(Vector2 delta) { _position += new Vector2(delta.x, -delta.y); Layout(); }
        internal void SavePosition()
        {
            float scale = _panel.localScale.x;
            if (_position.x < Screen.width * .15f) _position.x = 12;
            else if (_position.x + _panelWidth * scale > Screen.width * .85f) _position.x = Screen.width - _panelWidth * scale - 12;
            Layout(); APXConfigService.SetFloat("XS_HAND_DOCK_X", _position.x); APXConfigService.SetFloat("XS_HAND_DOCK_Y", _position.y); APXConfigService.Save();
        }
        private void OnDestroy() { if (_canvas != null) Destroy(_canvas); if (_instance == this) _instance = null; }
    }

    internal sealed class HandDockDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal HandCardDock Dock;
        public void OnBeginDrag(PointerEventData e) { e.Use(); }
        public void OnDrag(PointerEventData e) { Dock.Drag(e.delta); e.Use(); }
        public void OnEndDrag(PointerEventData e) { Dock.SavePosition(); e.Use(); }
    }

    internal static class APXDetailImages
    {
        internal static void Load(RawImage target, List<string> candidates, int index, Func<bool> current = null, bool squareCardArt = false)
        {
            if (target == null || index >= candidates.Count || (current != null && !current())) return;
            try
            {
                SimpleSingletonProvider<TextureManager>.inst.AsyncLoad(candidates[index], texture =>
                {
                    if (target == null || (current != null && !current())) return;
                    if (texture == null) { Load(target, candidates, index + 1, current, squareCardArt); return; }
                    target.texture = texture.nativeTexture; target.uvRect = texture.uvRect;
                    target.color = Color.white;
                    if (squareCardArt)
                    {
                        // Card sources can include the entire portrait card. Its illustration
                        // occupies the top square, as in the game's own hand-card renderer.
                        var uv = texture.uvRect;
                        if (texture.height > texture.width)
                        {
                            float part = (float)texture.width / Mathf.Max(1, texture.height);
                            uv.y += uv.height * (1 - part); uv.height *= part;
                        }
                        else if (texture.width > texture.height)
                        {
                            float part = (float)texture.height / Mathf.Max(1, texture.width);
                            uv.x += uv.width * (1 - part) * .5f; uv.width *= part;
                        }
                        target.uvRect = uv; return;
                    }
                    var size = target.rectTransform.sizeDelta;
                    float fit = Mathf.Min(size.x / Mathf.Max(1, texture.width), size.y / Mathf.Max(1, texture.height));
                    target.rectTransform.sizeDelta = new Vector2(texture.width * fit, texture.height * fit);
                }, null).Forget();
            }
            catch { Load(target, candidates, index + 1, current, squareCardArt); }
        }
    }
}
