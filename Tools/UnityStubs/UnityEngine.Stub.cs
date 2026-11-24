// Stub UnityEngine / UnityEngine.UI / UnityEditor tối thiểu — CHỈ để kiểm tra biên dịch ngoài Unity.
// Chữ ký được sao theo Unity 6 (ugui 2.x). Không có hành vi thật.
#pragma warning disable CS0067, CS0649, CS0169, CS0414
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        bool destroyed;
        public static void Destroy(Object obj) { if ((object)obj != null) obj.destroyed = true; }
        public static void Destroy(Object obj, float t) { if ((object)obj != null) obj.destroyed = true; }
        public static void DontDestroyOnLoad(Object target) { }
        public static void DestroyImmediate(Object obj) { }
        public static T Instantiate<T>(T original) where T : Object { return original; }
        public static T Instantiate<T>(T original, Transform parent) where T : Object { return original; }
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object { return original; }
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform parent) where T : Object { return original; }
        public static T FindFirstObjectByType<T>() where T : Object { return null; }
        public static T FindObjectOfType<T>() where T : Object { return null; }
        public static implicit operator bool(Object exists) { return !ReferenceEquals(exists, null) && !exists.destroyed; }
        public static bool operator ==(Object x, Object y) { return ReferenceEquals(x, y); }
        public static bool operator !=(Object x, Object y) { return !ReferenceEquals(x, y); }
        public override bool Equals(object other) { return base.Equals(other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; private set; }
        public Transform transform { get { return gameObject != null ? gameObject.transform : null; } }
        internal void Attach(GameObject go) { gameObject = go; }
        public T GetComponent<T>() { return gameObject != null ? gameObject.GetComponent<T>() : default(T); }
        public T GetComponentInChildren<T>() { return gameObject != null ? gameObject.GetComponentInChildren<T>(false) : default(T); }
        public T GetComponentInChildren<T>(bool includeInactive) { return gameObject != null ? gameObject.GetComponentInChildren<T>(includeInactive) : default(T); }
        public T[] GetComponentsInChildren<T>() { return null; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return null; }
    }
    public class Behaviour : Component { public bool enabled { get; set; } public bool isActiveAndEnabled { get; } }
    public class YieldInstruction { }
    public class Coroutine : YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public readonly float Seconds; public WaitForSeconds(float seconds) { Seconds = seconds; } }
    public class WaitForSecondsRealtime : YieldInstruction { public readonly float Seconds; public WaitForSecondsRealtime(float seconds) { Seconds = seconds; } }
    public class WaitForEndOfFrame : YieldInstruction { }
    public class WaitForFixedUpdate : YieldInstruction { }
    /// <summary>Chạy một coroutine theo thời gian ảo (Time.time). Hỗ trợ yield IEnumerator lồng, WaitForSeconds, null.</summary>
    public sealed class CoroutineRunner
    {
        readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        float wakeAt = -1;
        public bool Done { get; private set; }
        public CoroutineRunner(IEnumerator root) { stack.Push(root); }
        public void Step()
        {
            if (Done || wakeAt > Time.time) return;
            wakeAt = -1;
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                object cur = top.Current;
                var nested = cur as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                var w = cur as WaitForSeconds;
                if (w != null) { wakeAt = Time.time + w.Seconds; return; }
                var wr = cur as WaitForSecondsRealtime;
                if (wr != null) { wakeAt = Time.time + wr.Seconds; return; }
                return;     // null, WaitForEndOfFrame, Coroutine...: nghỉ một frame
            }
            Done = true;
        }
    }

    public class MonoBehaviour : Behaviour
    {
        public readonly List<CoroutineRunner> Routines = new List<CoroutineRunner>();
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            var run = new CoroutineRunner(routine);
            Routines.Add(run);
            run.Step();           // Unity chạy coroutine đồng bộ tới lệnh yield đầu tiên
            return new Coroutine();
        }
        public void StopCoroutine(Coroutine routine) { }
        public void StopAllCoroutines() { Routines.Clear(); }
        /// <summary>Dành cho bộ mô phỏng: chạy một bước tất cả coroutine.</summary>
        public void StepRoutines() { foreach (var r in Routines.ToArray()) r.Step(); Routines.RemoveAll(r => r.Done); }
        public void Invoke(string methodName, float time) { }
        public void CancelInvoke() { }
    }

    public sealed class GameObject : Object
    {
        readonly List<Component> components = new List<Component>();
        bool active = true;
        public GameObject() : this("GameObject", new Type[0]) { }
        public GameObject(string name) : this(name, new Type[0]) { }
        public GameObject(string name, params Type[] comps)
        {
            this.name = name;
            bool wantRect = comps != null && Array.IndexOf(comps, typeof(RectTransform)) >= 0;
            Transform t = wantRect ? new RectTransform() : new Transform();
            t.Attach(this);
            transform = t;
            components.Add(t);
            if (comps != null)
                foreach (Type c in comps)
                    if (c != typeof(RectTransform) && c != typeof(Transform)) AddComponent(c);
        }
        public Transform transform { get; private set; }
        public bool activeSelf { get { return active; } }
        public bool activeInHierarchy
        {
            get
            {
                for (Transform t = transform; t != null; t = t.parent) if (!t.gameObject.active) return false;
                return true;
            }
        }
        public int layer { get; set; }
        public string tag { get; set; }
        public void SetActive(bool value) { active = value; }
        public T AddComponent<T>() where T : Component { return (T)AddComponent(typeof(T)); }
        public Component AddComponent(Type t)
        {
            var c = (Component)Activator.CreateInstance(t);
            c.Attach(this);
            components.Add(c);
            return c;
        }
        public T GetComponent<T>()
        {
            foreach (Component c in components) if (c is T) return (T)(object)c;
            return default(T);
        }
        public T GetComponentInChildren<T>() { return GetComponentInChildren<T>(false); }
        public T GetComponentInChildren<T>(bool includeInactive)
        {
            if (includeInactive || active)
            {
                T own = GetComponent<T>();
                if (own != null) return own;
            }
            foreach (Transform child in transform)
            {
                if (!includeInactive && !child.gameObject.active) continue;
                T found = child.gameObject.GetComponentInChildren<T>(includeInactive);
                if (found != null) return found;
            }
            return default(T);
        }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
        public static Vector2 up { get { return new Vector2(0, 1); } }
        public static Vector2 down { get { return new Vector2(0, -1); } }
        public static Vector2 left { get { return new Vector2(-1, 0); } }
        public static Vector2 right { get { return new Vector2(1, 0); } }
        public float magnitude { get { return 0; } }
        public float sqrMagnitude { get { return 0; } }
        public Vector2 normalized { get { return this; } }
        public string ToString(string format) { return ""; }
        public static float Distance(Vector2 a, Vector2 b) { return 0; }
        public static float Dot(Vector2 a, Vector2 b) { return 0; }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { return a; }
        public static Vector2 ClampMagnitude(Vector2 v, float maxLength) { return v; }
        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta) { return current; }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator -(Vector2 a) { return a; }
        public static Vector2 operator *(Vector2 a, float d) { return a; }
        public static Vector2 operator *(float d, Vector2 a) { return a; }
        public static Vector2 operator /(Vector2 a, float d) { return a; }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } }
        public float magnitude { get { return 0; } }
        public string ToString(string format) { return ""; }
        public static float Distance(Vector3 a, Vector3 b) { return 0; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float d) { return a; }
        public static Vector3 operator *(float d, Vector3 a) { return a; }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
    }

    public struct Quaternion
    {
        public static Quaternion identity { get { return default(Quaternion); } }
        public static Quaternion Euler(float x, float y, float z) { return default(Quaternion); }
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) { return a; }
        public Vector3 eulerAngles { get { return default(Vector3); } }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return a; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color black { get { return new Color(0, 0, 0, 1); } }
        public static Color clear { get { return new Color(0, 0, 0, 0); } }
        public static Color Lerp(Color a, Color b, float t) { return a; }
        public static implicit operator Color(Color32 c) { return default(Color); }
        public static implicit operator Vector4(Color c) { return default(Vector4); }
        public static Color operator *(Color a, float b) { return a; }
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) { return default(Color32); }
    }
    public struct Vector4 { public float x, y, z, w; }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin { get { return x; } }
        public float yMin { get { return y; } }
        public float xMax { get { return x + width; } }
        public float yMax { get { return y + height; } }
        public Vector2 center { get { return new Vector2(x + width / 2, y + height / 2); } }
        public Vector2 size { get { return new Vector2(width, height); } }
        public bool Contains(Vector2 point) { return false; }
    }
    public struct Ray { public Vector3 origin, direction; public Vector3 GetPoint(float distance) { return origin; } }
    public struct Plane { public Plane(Vector3 inNormal, Vector3 inPoint) { } public bool Raycast(Ray ray, out float enter) { enter = 0; return false; } }
    public struct Bounds { }

    public class Transform : Component, IEnumerable
    {
        readonly List<Transform> children = new List<Transform>();
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Transform parent { get; private set; }
        public int childCount { get { return children.Count; } }
        public void SetParent(Transform p) { SetParent(p, true); }
        public void SetParent(Transform p, bool worldPositionStays)
        {
            if (parent != null) parent.children.Remove(this);
            parent = p;
            if (p != null) p.children.Add(this);
        }
        public void SetAsLastSibling() { if (parent != null) { parent.children.Remove(this); parent.children.Add(this); } }
        public void SetAsFirstSibling() { if (parent != null) { parent.children.Remove(this); parent.children.Insert(0, this); } }
        public void SetSiblingIndex(int index) { }
        public Transform Find(string n) { foreach (Transform c in children) if (c.gameObject.name == n) return c; return null; }
        public Transform GetChild(int index) { return children[index]; }
        public IEnumerator GetEnumerator() { return children.ToArray().GetEnumerator(); }
        public Vector3 TransformPoint(Vector3 position) { return position; }
        public Vector3 InverseTransformPoint(Vector3 position) { return position; }
        public Vector3 right { get { return default(Vector3); } }
        public Vector3 up { get { return default(Vector3); } }
        public Vector3 forward { get { return default(Vector3); } }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Rect rect { get; }
    }

    public static class RectTransformUtility
    {
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam) { return false; }
    }

    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Max(params float[] values) { float m = float.NegativeInfinity; foreach (float v in values) if (v > m) m = v; return m; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Abs(float f) { return f < 0 ? -f : f; }
        public static int Abs(int v) { return v < 0 ? -v : v; }
        public static float Clamp(float v, float min, float max) { return v < min ? min : v > max ? max : v; }
        public static int Clamp(int v, int min, int max) { return v < min ? min : v > max ? max : v; }
        public static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float LerpAngle(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float InverseLerp(float a, float b, float v) { return a == b ? 0 : Clamp01((v - a) / (b - a)); }
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
        public static float MoveTowards(float current, float target, float maxDelta) { return Abs(target - current) <= maxDelta ? target : current + Sign(target - current) * maxDelta; }
        public static float DeltaAngle(float current, float target) { float d = (target - current) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return d; }
        public static float Sin(float f) { return (float)Math.Sin(f); }
        public static float Cos(float f) { return (float)Math.Cos(f); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Pow(float f, float p) { return (float)Math.Pow(f, p); }
        public static int CeilToInt(float f) { return (int)Math.Ceiling(f); }
        public static int FloorToInt(float f) { return (int)Math.Floor(f); }
        public static int RoundToInt(float f) { return (int)Math.Round(f); }
        public static float Sign(float f) { return f >= 0 ? 1 : -1; }
        public static bool Approximately(float a, float b) { return Abs(a - b) < 1e-6f; }
    }

    public static class Time
    {
        public static float deltaTime { get; set; }
        public static float unscaledDeltaTime { get; set; }
        public static float fixedDeltaTime { get; set; }
        public static float time { get; set; }
        public static float realtimeSinceStartup { get; set; }
        public static float timeScale { get; set; }
        public static int frameCount { get; set; }
    }

    public enum KeyCode
    {
        None = 0, Escape = 27, Return = 13, KeypadEnter = 271, Space = 32,
        Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53,
        R = 114, P = 112, F1 = 282, F2 = 283, F12 = 293, Backspace = 8, Tab = 9, UpArrow = 273, DownArrow = 274, LeftArrow = 276, RightArrow = 275
    }

    public static class Input
    {
        /// <summary>Bộ mô phỏng bấm phím: thêm vào tập này trong một frame, bị xóa sau frame đó.</summary>
        public static readonly HashSet<KeyCode> SimKeysDown = new HashSet<KeyCode>();
        public static bool GetKeyDown(KeyCode key) { return SimKeysDown.Contains(key); }
        public static bool GetKey(KeyCode key) { return false; }
        public static bool GetKeyUp(KeyCode key) { return false; }
        public static bool GetMouseButton(int button) { return false; }
        public static bool GetMouseButtonDown(int button) { return false; }
        public static bool GetMouseButtonUp(int button) { return false; }
        public static Vector3 mousePosition { get; set; }
        public static string compositionString { get; }
    }

    public static class Application
    {
        public static string version { get; }
        public static string streamingAssetsPath { get; }
        public static string dataPath { get; }
        public static string persistentDataPath { get; }
        public static int targetFrameRate { get; set; }
        public static bool runInBackground { get; set; }
        public static bool isPlaying { get; }
        public static bool isEditor { get; }
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceived;
        public static void Quit() { }
        public static void Quit(int exitCode) { }
    }

    public enum LogType { Error, Assert, Warning, Log, Exception }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void Log(object message, Object context) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
        public static void LogException(Exception e) { }
    }

    public static class PlayerPrefs
    {
        public static string GetString(string key) { return ""; }
        public static string GetString(string key, string defaultValue) { return defaultValue; }
        public static void SetString(string key, string value) { }
        public static int GetInt(string key, int defaultValue) { return defaultValue; }
        public static void SetInt(string key, int value) { }
        public static bool HasKey(string key) { return false; }
        public static void DeleteKey(string key) { }
        public static void Save() { }
    }

    public static class Random
    {
        public static int Range(int minInclusive, int maxExclusive) { return minInclusive; }
        public static float Range(float minInclusive, float maxInclusive) { return minInclusive; }
        public static float value { get; }
    }

    public static class ScreenCapture
    {
        public static void CaptureScreenshot(string filename) { }
        public static void CaptureScreenshot(string filename, int superSize) { }
    }

    public static class Screen { public static int width { get; } public static int height { get; } }

    public class Camera : Behaviour
    {
        public static Camera main { get; }
        public float orthographicSize { get; set; }
        public Ray ScreenPointToRay(Vector3 pos) { return default(Ray); }
        public Color backgroundColor { get; set; }
    }

    public class Font : Object { }
    public class Texture : Object { }
    public class Texture2D : Texture { }
    public class Sprite : Object { }
    public class AudioClip : Object { }
    public class AudioSource : Behaviour
    {
        public float volume { get; set; }
        public bool loop { get; set; }
        public AudioClip clip { get; set; }
        public void Play() { }
        public void Stop() { }
        public void PlayOneShot(AudioClip clip) { }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }
    }
    public class SpriteRenderer : Component { public Color color { get; set; } public Sprite sprite { get; set; } public int sortingOrder { get; set; } }
    public class Renderer : Component { public bool enabled { get; set; } }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class)] public sealed class SerializeFieldAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float min) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponentAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class RequireComponentAttribute : Attribute { public RequireComponentAttribute(Type t) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrderAttribute : Attribute { public DefaultExecutionOrderAttribute(int o) { } }

    // ---- 2D physics (chỉ phần LastKingPhysics dùng)
    public enum RigidbodyType2D { Dynamic, Kinematic, Static }
    public enum CollisionDetectionMode2D { Discrete, Continuous }
    public enum ForceMode2D { Force, Impulse }
    public class Collider2D : Behaviour
    {
        public Rigidbody2D attachedRigidbody { get; }
        public bool isTrigger { get; set; }
        public bool OverlapPoint(Vector2 point) { return false; }
        public Vector2 ClosestPoint(Vector2 position) { return position; }
    }
    public enum RigidbodyInterpolation2D { None, Interpolate, Extrapolate }
    public class Joint2D : Behaviour { }
    public class AnchoredJoint2D : Joint2D { }
    public class HingeJoint2D : AnchoredJoint2D { public float jointAngle { get; } public float jointSpeed { get; } public bool useMotor { get; set; } public bool useLimits { get; set; } }
    public class BoxCollider2D : Collider2D { public Vector2 size { get; set; } public Vector2 offset { get; set; } }
    public class Rigidbody2D : Component
    {
        public RigidbodyType2D bodyType { get; set; }
        public Vector2 linearVelocity { get; set; }
        public float angularVelocity { get; set; }
        public float linearDamping { get; set; }
        public float angularDamping { get; set; }
        public float gravityScale { get; set; }
        public float mass { get; set; }
        public bool freezeRotation { get; set; }
        public CollisionDetectionMode2D collisionDetectionMode { get; set; }
        public Vector2 position { get; set; }
        public float rotation { get; set; }
        public void AddForce(Vector2 force) { }
        public void AddForce(Vector2 force, ForceMode2D mode) { }
        public void AddTorque(float torque) { }
        public void MovePosition(Vector2 position) { }
        public void MoveRotation(float angle) { }
        public bool simulated { get; set; }
        public bool useAutoMass { get; set; }
        public RigidbodyInterpolation2D interpolation { get; set; }
        public void WakeUp() { }
        public Vector2 GetPointVelocity(Vector2 point) { return default(Vector2); }
    }
    public class PhysicsMaterial2D : Object { public float bounciness { get; set; } public float friction { get; set; } }
    public static class Physics2D
    {
        public static void SyncTransforms() { }
        public static Vector2 gravity { get; set; }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
    public class UnityEvent
    {
        readonly List<UnityAction> calls = new List<UnityAction>();
        public void AddListener(UnityAction call) { calls.Add(call); }
        public void RemoveListener(UnityAction call) { calls.Remove(call); }
        public void RemoveAllListeners() { calls.Clear(); }
        public void Invoke() { foreach (var c in calls.ToArray()) c(); }
    }
    public class UnityEvent<T0>
    {
        readonly List<UnityAction<T0>> calls = new List<UnityAction<T0>>();
        public void AddListener(UnityAction<T0> call) { calls.Add(call); }
        public void RemoveListener(UnityAction<T0> call) { calls.Remove(call); }
        public void Invoke(T0 arg0) { foreach (var c in calls.ToArray()) c(arg0); }
    }
}

namespace UnityEngine.EventSystems
{
    public abstract class UIBehaviour : MonoBehaviour { }
    public class EventSystem : UIBehaviour { public static EventSystem current { get; } }
}

namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;
    using UnityEngine.Events;

    public struct ColorBlock
    {
        public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor;
        public float colorMultiplier, fadeDuration;
        public static ColorBlock defaultColorBlock { get { return default(ColorBlock); } }
    }

    public abstract class Graphic : UIBehaviour
    {
        public Color color { get; set; }
        public bool raycastTarget { get; set; }
        public RectTransform rectTransform { get; }
    }
    public abstract class MaskableGraphic : Graphic { }
    public class Selectable : UIBehaviour
    {
        public bool interactable { get; set; } = true;
        public Graphic targetGraphic { get; set; }
        public ColorBlock colors { get; set; }
    }
    public class Text : MaskableGraphic
    {
        public string text { get; set; } = "";
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool supportRichText { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public float lineSpacing { get; set; }
    }
    public class Image : MaskableGraphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public Sprite sprite { get; set; }
        public Type type { get; set; }
        public FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; }
        public bool preserveAspect { get; set; }
    }
    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; set; } = new ButtonClickedEvent();
    }
    public class InputField : Selectable
    {
        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public delegate char OnValidateInput(string text, int charIndex, char addedChar);
        public string text { get; set; } = "";
        public Text textComponent { get; set; }
        public Graphic placeholder { get; set; }
        public LineType lineType { get; set; }
        public ContentType contentType { get; set; }
        public int characterLimit { get; set; }
        public OnValidateInput onValidateInput { get; set; }
        public bool isFocused { get; }
        public void ActivateInputField() { }
    }
    public class RectMask2D : UIBehaviour { }
    public class CanvasScaler : UIBehaviour { }
    public class GraphicRaycaster : UIBehaviour { }
}

namespace UnityEngine.UI
{
    // (đã định nghĩa ở trên) — chỗ này để mở rộng nếu cần
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string itemName) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction) { }
    }
    public static class EditorApplication { public static void Exit(int returnValue) { } }
}
