// Run the production event router with native UI calls recorded at its boundary.
// No Unity player is needed; actual touch dispatch remains a separate device check.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XingShuRuntime.UI;

internal static class MobileSliderInputTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); _checks++; }
    private sealed class Touch
    {
        internal Slider slider = new Slider();
        internal PointerEventData data = new PointerEventData { pointerId = 1, position = new Vector2(100, 100) };
        internal XingShuMobileSliderInput input;
        internal Touch(float canvasScale = 1)
        {
            slider.canvas.scaleFactor = canvasScale;
            XingShuMobileSliderInput.Bind(slider);
            input = XingShuUIFactory.Last.GetComponent<XingShuMobileSliderInput>();
        }
        internal void Call(string name, PointerEventData other = null)
        {
            var method = typeof(XingShuMobileSliderInput).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(input, name == "OnDisable" ? null : new object[] { other ?? data });
        }
        internal void Down() { Call("Down"); Call("InitializeDrag"); }
        internal void Move(float x, float y)
        { data.position = new Vector2(100 + x, 100 + y); data.dragging = true; Call("Drag"); }
        internal void Up() { Call("Up"); Call("Finish"); }
    }
    private static void Main()
    {
        var tap = new Touch(); tap.Down();
        Check(tap.slider.down == 0, "Pointer down must not change a slider");
        Check(tap.data.useDragThreshold, "Use a drag threshold");
        tap.Up();
        Check(tap.slider.down == 1 && tap.slider.up == 1, "Tap applies on release once");

        var vertical = new Touch(); vertical.Down(); vertical.Move(2, 40); vertical.Up();
        Check(vertical.slider.down == 0 && vertical.slider.drag == 0 && vertical.slider.up == 0, "Vertical swipe must never reach Slider");
        Check(vertical.slider.scroll.begin == 1 && vertical.slider.scroll.drag == 1 && vertical.slider.scroll.end == 1, "Vertical swipe scrolls and ends once");
        Check(vertical.slider.scroll.beginPosition.y == 100, "Scroll must begin at the original press position");

        var horizontal = new Touch(); horizontal.Down(); horizontal.Move(40, 2); horizontal.Move(60, 3); horizontal.Up();
        Check(horizontal.slider.down == 1 && horizontal.slider.drag == 2 && horizontal.slider.up == 1, "Horizontal drag adjusts once per event");
        Check(horizontal.slider.scroll.begin == 0, "Horizontal drag must not scroll");
        Check(horizontal.slider.downPosition.x == 100 && horizontal.data.position.x == 160, "Slider gets original press, event keeps current position");

        var diagonal = new Touch(); diagonal.Down(); diagonal.Move(20, 20); diagonal.Up();
        Check(diagonal.slider.down == 0 && diagonal.slider.scroll.begin == 1, "Diagonal swipe favors page scrolling");

        var locked = new Touch(); locked.Down(); locked.Move(1, 20); locked.Move(90, 22); locked.Up();
        Check(locked.slider.down == 0 && locked.slider.scroll.drag == 2, "Crossing a slider later cannot steal scrolling");
        var adjustLock = new Touch(); adjustLock.Down(); adjustLock.Move(20, 1); adjustLock.Move(22, 80); adjustLock.Up();
        Check(adjustLock.slider.drag == 2 && adjustLock.slider.scroll.begin == 0, "Horizontal adjustment keeps ownership");

        var scale = new Touch(3); scale.Down(); scale.Move(15, 1);
        Check(scale.slider.down == 0 && scale.slider.scroll.begin == 0, "Threshold scales with the canvas");
        scale.Move(30, 1); scale.Up();
        Check(scale.slider.down == 1, "Deliberate movement passes scaled threshold");

        var multi = new Touch(); multi.Down();
        var second = new PointerEventData { pointerId = 2, position = new Vector2(500, 500) };
        multi.Call("Down", second); multi.Call("Drag", second); multi.Call("Up", second);
        Check(multi.slider.down == 0 && multi.slider.scroll.begin == 0, "Second finger cannot change the active gesture");
        multi.Move(2, 30); multi.Up();
        Check(multi.slider.scroll.end == 1, "Original finger remains active");

        var disable = new Touch(); disable.Down(); disable.Move(1, 30); disable.Call("OnDisable"); disable.Up();
        Check(disable.slider.scroll.end == 1, "Closing the page ends its scroll exactly once");

        var outside = new Touch(); outside.Down(); outside.data.position = new Vector2(-1, 100); outside.Up();
        Check(outside.slider.down == 0, "Release outside cannot apply a value");
        var noMoveEvent = new Touch(); noMoveEvent.Down(); noMoveEvent.data.position = new Vector2(100, 150); noMoveEvent.Up();
        Check(noMoveEvent.slider.down == 0, "Fast swipe with no drag event cannot become a tap");
        var disabled = new Touch(); disabled.slider.interactable = false; disabled.Down(); disabled.Move(30, 1); disabled.Up();
        Check(disabled.slider.down == 0, "Disabled slider cannot be adjusted");
        Console.WriteLine("Mobile slider input: " + _checks + " checks passed");
    }
}

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject;
        public RectTransform transform { get { return gameObject.rect; } }
        public T[] GetComponentsInChildren<T>(bool inactive) { return new T[0]; }
        public T GetComponentInParent<T>() where T : class
        {
            var slider = this as Slider;
            return (typeof(T) == typeof(ScrollRect) ? (object)slider.scroll : slider.canvas) as T;
        }
    }
    public class MonoBehaviour : Component { }
    public class GameObject
    {
        private readonly Dictionary<Type, Component> _parts = new Dictionary<Type, Component>();
        public RectTransform rect = new RectTransform();
        public T GetComponent<T>() where T : Component { return typeof(T) == typeof(RectTransform) ? rect as T : _parts[typeof(T)] as T; }
        public T AddComponent<T>() where T : Component
        { var part = (T)Activator.CreateInstance(typeof(T), true); part.gameObject = this; _parts[typeof(T)] = part; return part; }
    }
    public class RectTransform : Component { public Vector2 anchorMin, anchorMax, offsetMin, offsetMax; }
    public class Canvas : Component { public float scaleFactor = 1; }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float a, float b) { x = a; y = b; }
        public static Vector2 zero { get { return new Vector2(); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
    }
    public static class Mathf { public static float Abs(float x) { return Math.Abs(x); } public static float Max(float x, float y) { return Math.Max(x, y); } }
    public static class RectTransformUtility
    { public static bool RectangleContainsScreenPoint(RectTransform r, Vector2 p, object camera) { return p.x >= 0 && p.y >= 0; } }
}
namespace UnityEngine.Events { public delegate void UnityAction<T>(T value); }
namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public class PointerEventData : BaseEventData
    {
        public enum InputButton { Left, Right }
        public int pointerId; public Vector2 position; public InputButton button;
        public bool useDragThreshold, dragging; public object pressEventCamera;
    }
    public enum EventTriggerType { PointerDown, InitializePotentialDrag, BeginDrag, Drag, PointerUp, EndDrag, Scroll }
    public class EventTrigger : Component
    {
        public class Callback { public void AddListener(UnityEngine.Events.UnityAction<BaseEventData> action) { } }
        public class Entry { public EventTriggerType eventID; public Callback callback = new Callback(); }
        public List<Entry> triggers = new List<Entry>();
    }
}
namespace UnityEngine.UI
{
    public class Graphic : Component { public bool raycastTarget; }
    public class Image : Graphic { public Color color; }
    public class Slider : Component
    {
        public ScrollRect scroll = new ScrollRect(); public Canvas canvas = new Canvas();
        public int down, drag, up; public bool interactable = true; public Vector2 downPosition;
        public Slider() { gameObject = new GameObject(); }
        public bool IsInteractable() { return interactable; }
        public void OnPointerDown(PointerEventData data) { down++; downPosition = data.position; }
        public void OnDrag(PointerEventData data) { drag++; }
        public void OnPointerUp(PointerEventData data) { up++; }
    }
    public class ScrollRect : Component
    {
        public int begin, drag, end; public Vector2 beginPosition;
        public void OnInitializePotentialDrag(PointerEventData data) { }
        public void OnBeginDrag(PointerEventData data) { begin++; beginPosition = data.position; }
        public void OnDrag(PointerEventData data) { drag++; }
        public void OnEndDrag(PointerEventData data) { end++; }
        public void OnScroll(PointerEventData data) { }
    }
}
namespace XingShuRuntime.UI
{
    internal static class XingShuUIFactory
    {
        internal static GameObject Last;
        internal static GameObject CreateUIObject(string name, GameObject parent) { return Last = new GameObject(); }
    }
}
