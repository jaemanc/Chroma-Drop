// UnityStubs.cs — GameManager.cs 컴파일 검증 전용. Unity 프로젝트에 넣지 말 것.
using System;
using System.Collections;

namespace UnityEngine
{
    public class Object { public static void Destroy(Object o) { } }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1); } }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 operator *(Vector3 v, float s) { return new Vector3(v.x * s, v.y * s, v.z * s); }
    }
    public struct Rect { public float x, y, w, h; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; this.w = w; this.h = h; } }

    public class Transform : Object, IEnumerable
    {
        public Transform parent; public Vector3 position; public Vector3 localScale;
        public GameObject gameObject = new GameObject();
        public IEnumerator GetEnumerator() { yield break; }
    }
    public class Component : Object { public Transform transform = new Transform(); }
    public class GameObject : Object
    {
        public Transform transform = new Transform();
        public GameObject() { } public GameObject(string n) { }
        public T AddComponent<T>() where T : Component, new() { return new T(); }
    }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator r) { return null; } }
    public class Coroutine { }
    public class Renderer : Component { public int sortingOrder; }
    public class SpriteRenderer : Renderer { public Sprite sprite; public Color color; public bool enabled; }
    public class Texture2D : Object { public Texture2D(int w, int h) { } public void SetPixels(Color[] p) { } public void Apply() { } }
    public class Sprite : Object { public static Sprite Create(Texture2D t, Rect r, Vector2 p, float ppu) { return new Sprite(); } }
    public class Camera : Component
    {
        public static Camera main = new Camera();
        public bool orthographic; public float orthographicSize; public Color backgroundColor;
        public Vector3 ScreenToWorldPoint(Vector3 p) { return p; }
    }
    public enum KeyCode { R }
    public static class Input
    {
        public static Vector3 mousePosition { get { return new Vector3(); } }
        public static bool GetKeyDown(KeyCode k) { return false; }
        public static bool GetMouseButtonDown(int b) { return false; }
    }
    public static class Mathf
    {
        public static int RoundToInt(float f) { return (int)Math.Round(f); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
    }
    public static class Time { public static float time { get { return 0f; } } }
    public class WaitForSeconds { public WaitForSeconds(float s) { } }
}
