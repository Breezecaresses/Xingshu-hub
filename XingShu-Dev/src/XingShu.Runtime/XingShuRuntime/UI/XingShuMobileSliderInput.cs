using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    // Defer the Slider's pointer-down (which changes its value immediately) until
    // the gesture is known. Use the native EventTrigger across the hot-update boundary.
    internal sealed class XingShuMobileSliderInput : MonoBehaviour
    {
        private enum Gesture { Pending, Adjust, Scroll }
        private Slider _slider;
        private ScrollRect _scroll;
        private int _pointerId = int.MinValue;
        private Vector2 _press;
        private float _threshold;
        private Gesture _gesture;
        private PointerEventData _lastEvent;

        internal static void Bind(Slider slider)
        {
            // This surface is the only raycast target in the control: the native
            // Slider must never see pointer-down before direction is decided.
            foreach (Graphic graphic in slider.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            var surface = XingShuUIFactory.CreateUIObject("MobileSliderInput", slider.gameObject);
            var rect = surface.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var hit = surface.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, .001f);
            hit.raycastTarget = true;
            var input = surface.AddComponent<XingShuMobileSliderInput>();
            input._slider = slider;
            var trigger = surface.AddComponent<EventTrigger>();
            Add(trigger, EventTriggerType.PointerDown, data => input.Down((PointerEventData)data));
            Add(trigger, EventTriggerType.InitializePotentialDrag, data => input.InitializeDrag((PointerEventData)data));
            Add(trigger, EventTriggerType.BeginDrag, data => input.Drag((PointerEventData)data));
            Add(trigger, EventTriggerType.Drag, data => input.Drag((PointerEventData)data));
            Add(trigger, EventTriggerType.PointerUp, data => input.Up((PointerEventData)data));
            Add(trigger, EventTriggerType.EndDrag, data => input.Finish((PointerEventData)data));
            Add(trigger, EventTriggerType.Scroll, data => input._slider.GetComponentInParent<ScrollRect>()?.OnScroll((PointerEventData)data));
        }

        private static void Add(EventTrigger trigger, EventTriggerType type, UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        private void Down(PointerEventData data)
        {
            if (_pointerId != int.MinValue || data.button != PointerEventData.InputButton.Left) return;
            _pointerId = data.pointerId;
            _press = data.position;
            _gesture = Gesture.Pending;
            _lastEvent = data;
            _scroll = _slider.GetComponentInParent<ScrollRect>();
            var canvas = _slider.GetComponentInParent<Canvas>();
            _threshold = Mathf.Max(8f, 8f * (canvas != null ? canvas.scaleFactor : 1f));
        }

        private void InitializeDrag(PointerEventData data)
        {
            if (data.pointerId != _pointerId) return;
            data.useDragThreshold = true;
            _scroll?.OnInitializePotentialDrag(data);
        }

        private void Drag(PointerEventData data)
        {
            if (data.pointerId != _pointerId) return;
            _lastEvent = data;
            if (_gesture == Gesture.Pending)
            {
                Vector2 delta = data.position - _press;
                if (Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) < _threshold) return;
                // Bias diagonal swipes towards scrolling. Once chosen, keep the
                // same owner even when the finger crosses another control.
                bool adjust = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.25f && _slider.IsInteractable();
                _gesture = adjust ? Gesture.Adjust : Gesture.Scroll;
                Vector2 current = data.position;
                data.position = _press;
                if (adjust) _slider.OnPointerDown(data);
                else _scroll?.OnBeginDrag(data);
                data.position = current;
            }
            if (_gesture == Gesture.Adjust) _slider.OnDrag(data);
            else _scroll?.OnDrag(data);
        }

        private void Up(PointerEventData data)
        {
            if (data.pointerId != _pointerId) return;
            // A tap still selects a value, but only on release. A vertical swipe
            // never calls any Slider input method, so no value/save callback runs.
            Vector2 delta = data.position - _press;
            if (_gesture == Gesture.Pending && !data.dragging &&
                Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) < _threshold &&
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, data.position, data.pressEventCamera))
            {
                _slider.OnPointerDown(data);
                _slider.OnPointerUp(data);
            }
            Finish(data);
        }

        private void Finish(PointerEventData data)
        {
            if (data.pointerId != _pointerId) return;
            if (_gesture == Gesture.Adjust) _slider.OnPointerUp(data);
            else if (_gesture == Gesture.Scroll) _scroll?.OnEndDrag(data);
            _pointerId = int.MinValue;
            _lastEvent = null;
        }

        private void OnDisable()
        {
            if (_lastEvent != null) Finish(_lastEvent);
        }
    }
}
