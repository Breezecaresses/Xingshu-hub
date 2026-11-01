using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Scene;
using Cinemachine;
using UnityEngine;
using XingShuRuntime.CameraPort;

// Headless integration tests compile the production camera controller. The
// fakes below supply engine I/O only; camera ownership and pose code are real.
internal static class CameraZoomTests
{
    private static int _checks, _failed;
    private static void Check(bool value, string label) { _checks++; if (!value) { _failed++; Console.WriteLine("FAIL: " + label); } }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .005f; }
    private static void Run(bool orthographic, float zoom)
    {
        using (var fixture = new Fixture(orthographic, zoom))
        {
            var map = fixture.Map;
            var brain = fixture.Brain;
            var camera = fixture.Camera;
            var target = new Vector3(8, 0, 13);
            MapTargetReader.Mode = CameraSelectionPolicy.Read(true, false, false, false, null, null, 2);
            map.Apply();
            if (orthographic) Check(Near(camera.orthographicSize, zoom), "movement handoff must not flash native orthographic size");
            Check(brain.enabled, "native movement keeps native target processing");
            brain.Push(camera, target, 20, 12);
            Check(Near(fixture.Distance(), zoom), "movement button preserves selected zoom: " + orthographic + "/" + zoom);
            Check(Near(camera.fieldOfView, 60), "board movement preserves original FOV");
            Check((fixture.Focus() - target).magnitude < .005f, "movement keeps native target location");

            MapTargetReader.Mode = CameraControlMode.Selection;
            map.Apply();
            brain.Push(camera, new Vector3(-4, 0, 17), 35, 9);
            Check(Near(fixture.Distance(), zoom), "skill target selection preserves zoom");
            var selectedFocus = fixture.Focus();
            // A late writer changes the physical output between native pushes.
            // The next Apply must use the already captured native target.
            camera.transform.position = new Vector3(400, 100, 400);
            map.Apply();
            Check((fixture.Focus() - selectedFocus).magnitude < .005f, "reapplying pose cannot feed a modified output back into native focus");

            MapTargetReader.Mode = CameraSelectionPolicy.Read(true, false, false, false, null, null, 2);
            map.Apply();
            brain.Push(camera, target, 18, 7);
            Check(Near(fixture.Distance(), zoom), "selection to native movement preserves zoom");

            brain.IsBlending = true;
            MapTargetReader.Mode = CameraControlMode.Free;
            map.Apply();
            Check(brain.enabled, "native blend completes before free input resumes");
            brain.Push(camera, target, 25, 5);
            Check(Near(fixture.Distance(), zoom), "native blend cannot overwrite zoom");
            brain.IsBlending = false;
            brain.Push(camera, target, 22, 6);
            map.Apply();
            Check(!brain.enabled, "finished native blend returns free camera ownership");
            Check(Near(fixture.Distance(), zoom), "resume keeps saved zoom");

            MapTargetReader.Mode = CameraSelectionPolicy.Read(true, false, false, false, null, null, 3);
            map.Apply();
            brain.Push(camera, target, 19, 10);
            Check(Near(fixture.Distance(), zoom), "map marking preserves zoom and native target");
            MapTargetReader.Mode = CameraControlMode.Free;
            map.Apply();
            MapTargetReader.Mode = CameraControlMode.Selection;
            map.Apply();
            MapTargetReader.Mode = CameraControlMode.Free;
            map.Apply();
            Check(brain.enabled, "instant-close selection still allows its first native push");
            brain.Push(camera, target, 17, 10);
            map.Apply();
            Check(!brain.enabled && Near(fixture.Distance(), zoom), "after first native push resumes saved zoom");

            MapTargetReader.Mode = CameraControlMode.Combat;
            map.Apply();
            brain.Push(camera, target, 11, 4);
            Check(Near(fixture.Distance(), orthographic ? 4 : 11), "combat camera retains native zoom");
            MapTargetReader.Mode = CameraControlMode.System;
            map.Apply();
            brain.Push(camera, target, 16, 8);
            Check(Near(fixture.Distance(), orthographic ? 8 : 16), "unavailable or unknown camera remains native");

            brain.IsBlending = true;
            MapTargetReader.Mode = CameraControlMode.Free;
            map.Apply();
            Check(brain.enabled, "combat return blend is allowed to finish");
            brain.Push(camera, target, 13, 5);
            Check(Near(fixture.Distance(), orthographic ? 5 : 13), "combat return blend is not overridden early");
            brain.IsBlending = false;
            brain.Push(camera, target, 20, 9);
            map.Apply();
            Check(Near(fixture.Distance(), zoom), "after combat restores user zoom");
            Check(Near(map.Zoom, zoom), "native controls never overwrite stored zoom");
            map.Restore();
            Check(brain.enabled, "closing free camera restores native brain");
            Check(Near(camera.orthographicSize, 14) && Near(camera.fieldOfView, 60), "closing restores original lens");
            Check(CinemachineCore.CameraUpdatedEvent.ListenerCount == 0, "closing removes native callback");
        }
    }
    private static int Main()
    {
        foreach (bool orthographic in new[] { false, true })
            foreach (float zoom in new[] { 6f, 40f, 140f, 300f, 999f }) Run(orthographic, zoom);
        Check(CameraSelectionPolicy.Read(false, false, false, false, null, null, 2) == CameraControlMode.Unavailable, "missing contract yields");
        Check(CameraSelectionPolicy.Read(true, true, true, true, null, null, 2) == CameraControlMode.Combat, "combat overrides selection and native movement");
        Check(CameraSelectionPolicy.Read(true, false, false, false, null, null, 99) == CameraControlMode.System, "unknown status yields");
        Console.WriteLine("Camera zoom integration: " + (_checks - _failed) + "/" + _checks + " passed");
        return _failed == 0 ? 0 : 1;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MapCamera Map = new MapCamera();
        internal readonly Camera Camera;
        internal readonly CinemachineBrain Brain;
        internal Fixture(bool ortho, float zoom)
        {
            CinemachineCore.CameraUpdatedEvent = new CinemachineBrain.BrainEvent();
            MapTargetReader.Mode = CameraControlMode.Free;
            MapTargetReader.IsRunning = true;
            Camera = new Camera { orthographic = ortho, orthographicSize = 14, fieldOfView = 60, farClipPlane = 2000, aspect = 16f / 9 };
            Camera.transform.rotation = new Quaternion((float)Math.Sin(Math.PI / 8), 0, 0, (float)Math.Cos(Math.PI / 8));
            Camera.transform.position = -(Camera.transform.rotation * Vector3.forward) * 100;
            Brain = new CinemachineBrain();
            BattleSceneController.inst = new BattleSceneController { mainCamera = Camera, cinemachineBrain = Brain, freeObject = new Core.FreeCameraObject() };
            Map.Toggle(new[] { new Vector3(-20, 0, -20), new Vector3(-20, 0, 20), new Vector3(20, 0, -20), new Vector3(20, 0, 20) });
            typeof(MapCamera).GetField("_zoom", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Map, zoom);
            Map.Apply();
        }
        internal Vector3 Focus()
        {
            Vector3 direction = Camera.transform.rotation * Vector3.forward;
            return Camera.transform.position + direction * (-Camera.transform.position.y / direction.y);
        }
        internal float Distance() { return Camera.orthographic ? Camera.orthographicSize : Vector3.Distance(Camera.transform.position, Focus()); }
        public void Dispose() { Map.Restore(); }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) { return !ReferenceEquals(value, null); }
        public static bool operator !(Object value) { return ReferenceEquals(value, null); }
    }
    public class Behaviour : Object { public bool enabled = true; }
    public class Transform { public Vector3 position; public Quaternion rotation = Quaternion.identity; public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; } }
    public class Camera : Object
    {
        public string name = "TestBoard";
        public Transform transform = new Transform();
        public object targetTexture;
        public bool orthographic;
        public float orthographicSize, fieldOfView, farClipPlane, aspect;
        public Rect pixelRect = new Rect(0, 0, 1920, 1080);
        public Ray ScreenPointToRay(Vector3 point) { return new Ray { origin = transform.position, direction = transform.rotation * Vector3.forward }; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }
        public void Normalize() { float n = magnitude; if (n > 0) { x /= n; y /= n; z /= n; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator /(Vector3 a, float b) { return a * (1 / b); }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0, 0, 0, 1); } }
        public static Quaternion Inverse(Quaternion q) { float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w; return new Quaternion(-q.x / n, -q.y / n, -q.z / n, q.w / n); }
        public static Vector3 operator *(Quaternion q, Vector3 v) { Vector3 u = new Vector3(q.x, q.y, q.z); return v + Vector3.Cross(u, v) * (2 * q.w) + Vector3.Cross(u, Vector3.Cross(u, v)) * 2; }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public bool Contains(Vector2 p) { return p.x >= x && p.y >= y && p.x <= x + width && p.y <= y + height; }
    }
    public struct Bounds
    {
        public Vector3 center, extents;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; extents = size / 2; }
        public Vector3 min { get { return center - extents; } }
        public Vector3 max { get { return center + extents; } }
        public void Encapsulate(Vector3 point) { Vector3 lo = new Vector3(Math.Min(min.x, point.x), Math.Min(min.y, point.y), Math.Min(min.z, point.z)); Vector3 hi = new Vector3(Math.Max(max.x, point.x), Math.Max(max.y, point.y), Math.Max(max.z, point.z)); center = (lo + hi) / 2; extents = (hi - lo) / 2; }
        public void Expand(Vector3 amount) { extents += amount / 2; }
    }
    public struct Ray { public Vector3 origin, direction; }
    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public struct Touch { public int fingerId; public TouchPhase phase; public Vector2 position; }
    public enum KeyCode { LeftArrow, RightArrow, UpArrow, DownArrow, Escape }
    public static class Input
    {
        public static int touchCount;
        public static Vector2 mouseScrollDelta;
        public static Vector3 mousePosition;
        public static Touch GetTouch(int index) { return default(Touch); }
        public static bool GetMouseButton(int index) { return false; }
        public static bool GetMouseButtonDown(int index) { return false; }
        public static bool GetKey(KeyCode key) { return false; }
    }
    public static class Screen { public static int width = 1920, height = 1080; public static float dpi = 96; public static Rect safeArea = new Rect(0, 0, 1920, 1080); }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float unscaledDeltaTime = .016f; }
    public static class Debug { public static void Log(object value) { } }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Abs(float a) { return Math.Abs(a); }
        public static float Clamp(float v, float lo, float hi) { return Math.Max(lo, Math.Min(hi, v)); }
        public static float Exp(float value) { return (float)Math.Exp(value); }
        public static float Tan(float value) { return (float)Math.Tan(value); }
    }
}
namespace UnityEngine.EventSystems { public class EventSystem { public static EventSystem current; public bool IsPointerOverGameObject() { return false; } } }
namespace Cinemachine
{
    public struct CameraState { public Vector3 FinalPosition; public Quaternion FinalOrientation; }
    public class CinemachineBrain : Behaviour
    {
        public bool IsBlending;
        public CameraState CurrentCameraState;
        public sealed class BrainEvent
        {
            private readonly List<Action<CinemachineBrain>> _listeners = new List<Action<CinemachineBrain>>();
            public int ListenerCount { get { return _listeners.Count; } }
            public void AddListener(Action<CinemachineBrain> action) { _listeners.Add(action); }
            public void RemoveListener(Action<CinemachineBrain> action) { _listeners.Remove(action); }
            public void Invoke(CinemachineBrain brain) { foreach (var listener in _listeners.ToArray()) listener(brain); }
        }
        public void Push(Camera camera, Vector3 focus, float distance, float size)
        {
            if (!enabled) return;
            var rotation = new Quaternion((float)Math.Sin(Math.PI / 8), 0, 0, (float)Math.Cos(Math.PI / 8));
            CurrentCameraState = new CameraState { FinalPosition = focus - rotation * Vector3.forward * distance, FinalOrientation = rotation };
            camera.transform.SetPositionAndRotation(CurrentCameraState.FinalPosition, rotation);
            camera.orthographicSize = size; camera.fieldOfView = 48;
            CinemachineCore.CameraUpdatedEvent.Invoke(this);
        }
    }
    public static class CinemachineCore { public static CinemachineBrain.BrainEvent CameraUpdatedEvent = new CinemachineBrain.BrainEvent(); }
}
namespace Core { public class FreeCameraObject : Behaviour { } }
namespace Core.Scene { public class BattleSceneController : UnityEngine.Object { public static BattleSceneController inst; public Camera mainCamera; public CinemachineBrain cinemachineBrain; public Core.FreeCameraObject freeObject; } }
namespace XingShuRuntime.UI { public static class XingShuUIAssetService { public static void LogUIRuntime(string value) { } } }
namespace XingShuRuntime.CameraPort
{
    internal static class MapTargetReader { internal static CameraControlMode Mode; internal static bool IsRunning; internal static bool Running() { return IsRunning; } internal static CameraControlMode Read(BattleSceneController controller) { return Mode; } internal static bool IsMarking(BattleSceneController controller) { return false; } }
    internal static class MapCameraNative
    {
        internal static bool TouchInputOnly { get { return false; } }
        internal static void CancelTouchClick(int id) { }
        internal static bool TouchBlocked(int id, Vector2 point) { return false; }
        internal static bool? IsBlending(Behaviour brain) { return ((CinemachineBrain)brain).IsBlending; }
        internal static bool CancelManualStatus(Behaviour obj) { return true; }
        internal static bool KeyboardFocused() { return false; }
        internal static bool PointerBlocked() { return false; }
    }
    internal struct MapTouchSample { internal int Id; internal Vector2 Position; internal bool Began, Ended, Cancelled, Allowed; }
    internal struct MapTouchMotion { internal Vector2 Previous, Current; internal float ZoomRatio; internal MapTouchMotion(Vector2 previous, Vector2 current, float ratio) { Previous = previous; Current = current; ZoomRatio = ratio; } }
    internal sealed class MapTouchGesture { internal bool Consumed { get { return false; } } internal void Cancel() { } internal bool Owns(int id) { return false; } internal bool Update(MapTouchSample[] samples, int count, bool allowed, float threshold, out MapTouchMotion motion) { motion = default(MapTouchMotion); return false; } }
    internal sealed class MapDragGesture { internal void Cancel() { } internal bool Update(Vector2 point, bool began, bool held, bool free, bool allowed, out Vector2 previous) { previous = point; return false; } }
}
