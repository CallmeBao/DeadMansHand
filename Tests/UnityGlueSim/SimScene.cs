using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using TheLastKing.Online;
using UnityEngine;
using UnityEngine.UI;

namespace TheLastKing.Sim
{
    /// <summary>
    /// Scene giả + bộ điều khiển frame. Dựng các đối tượng giống scene thật (cùng tên trường công khai của controller),
    /// gọi Start/Update bằng reflection như Unity, chạy coroutine theo thời gian ảo.
    /// </summary>
    public sealed class SimScene
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public LastKingGameController Game;
        public LastKingPhysics Physics;
        public LastKingOnlineUi Ui;
        public Transform Canvas;
        readonly List<MonoBehaviour> behaviours = new List<MonoBehaviour>();
        float now;
        public int Frames;

        // ------------------------------------------------------------------ dựng scene

        static Text NewText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = new Font();
            return t;
        }

        static Image NewImage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<Image>();
        }

        static Button NewButton(Transform parent, string name, bool caption)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            var b = go.AddComponent<Button>();
            b.targetGraphic = image;
            if (caption) NewText(go.transform, "Caption");
            return b;
        }

        static GameObject NewPanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>();
            return go;
        }

        public void Build()
        {
            var root = new GameObject("Match Controller");
            Game = root.AddComponent<LastKingGameController>();
            Physics = root.AddComponent<LastKingPhysics>();
            var canvas = new GameObject("HUD Canvas", typeof(RectTransform));
            Canvas = canvas.transform;

            foreach (FieldInfo f in typeof(LastKingGameController).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type ft = f.FieldType;
                object v = null;
                if (ft == typeof(LastKingPhysics)) v = Physics;
                else if (ft == typeof(Camera)) v = new GameObject("World Camera").AddComponent<Camera>();
                else if (ft == typeof(Sprite[])) v = new[] { new Sprite(), new Sprite(), new Sprite(), new Sprite() };
                else if (ft == typeof(Button[])) v = new[] { NewButton(Canvas, "Card 1", false), NewButton(Canvas, "Card 2", false), NewButton(Canvas, "Card 3", false) };
                else if (ft == typeof(Image[])) v = new[] { NewImage(Canvas, "Card Image 1"), NewImage(Canvas, "Card Image 2"), NewImage(Canvas, "Card Image 3") };
                else if (ft == typeof(Text[])) v = new[] { NewText(Canvas, "Card Label 1"), NewText(Canvas, "Card Label 2"), NewText(Canvas, "Card Label 3") };
                else if (ft == typeof(GameObject[])) v = new[] { NewPanel(Canvas, "Mark 1"), NewPanel(Canvas, "Mark 2"), NewPanel(Canvas, "Mark 3") };
                else if (ft == typeof(Button)) v = NewButton(Canvas, f.Name, f.Name == "RestartButton" || f.Name == "StartButton" || f.Name == "ResumeButton");
                else if (ft == typeof(Text)) v = NewText(Canvas, f.Name);
                else if (ft == typeof(Image)) v = NewImage(Canvas, f.Name);
                else if (ft == typeof(GameObject)) v = NewPanel(Canvas, f.Name);
                else if (ft == typeof(Transform)) v = new GameObject("Opponent Visual").transform;
                else if (ft == typeof(SpriteRenderer)) v = new GameObject("Opponent Tint").AddComponent<SpriteRenderer>();
                else if (ft == typeof(AudioSource)) v = new GameObject("Audio").AddComponent<AudioSource>();
                else if (ft == typeof(AudioClip)) v = new AudioClip();
                if (v != null) f.SetValue(Game, v);
            }
            // các phần tử mà giao diện online tìm theo tên trong scene thật
            NewText(Game.StartPanel.transform, "Welcome Controls");
            NewText(Game.PausePanel.transform, "Pause Title").text = "TẠM DỪNG";
            Game.RestartButton.GetComponentInChildren<Text>(true).text = "VÁN ĐẤU MỚI";
        }

        /// <summary>Start() của controller (như Unity gọi) rồi gắn lớp online như LastKingOnlineBootstrap.</summary>
        public void Start(bool withOnline = true)
        {
            Time.timeScale = 1;
            Call(Game, "Start");
            behaviours.Add(Game); behaviours.Add(Physics);
            if (withOnline)
            {
                Ui = Game.gameObject.AddComponent<LastKingOnlineUi>();
                Ui.Init(Game);
                behaviours.Add(Ui);
            }
        }

        static void Call(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, Any);
            if (m == null) return;
            try { m.Invoke(target, null); }
            catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }

        // ------------------------------------------------------------------ chạy frame

        public void Frame(float dt = 0.05f, int sleepMs = 2)
        {
            now += dt; Frames++;
            Time.time = now; Time.realtimeSinceStartup = now; Time.deltaTime = dt; Time.unscaledDeltaTime = dt; Time.frameCount = Frames;
            foreach (MonoBehaviour b in behaviours.ToArray()) Call(b, "Update");
            foreach (MonoBehaviour b in behaviours.ToArray()) b.StepRoutines();
            Input.SimKeysDown.Clear();
            if (sleepMs > 0) Thread.Sleep(sleepMs);
        }

        public bool Until(Func<bool> cond, int maxFrames = 600)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (cond()) return true;
                Frame();
            }
            return cond();
        }

        public void Run(int frames) { for (int i = 0; i < frames; i++) Frame(); }

        // ------------------------------------------------------------------ truy cập trạng thái riêng tư / tìm đối tượng

        public static V Get<V>(object o, string field)
        {
            Type t = o.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, Any | BindingFlags.DeclaredOnly);
                if (f != null) return (V)f.GetValue(o);
                t = t.BaseType;
            }
            PropertyInfo p = o.GetType().GetProperty(field, Any);
            if (p != null) return (V)p.GetValue(o);
            throw new InvalidOperationException("Không có thành viên " + field + " trên " + o.GetType().Name);
        }

        public string Stage { get { return Get<object>(Game, "stage").ToString(); } }
        public int Selected { get { return Get<int>(Game, "selected"); } }
        public bool LinkLost { get { return Get<bool>(Game, "linkLost"); } }
        public bool LockPending { get { return Get<bool>(Game, "lockPending"); } }
        public bool MenuOpen { get { return Get<bool>(Game, "onlineMenuOpen"); } }
        public int Me { get { return Get<int>(Game, "Me"); } }
        public string OpponentLabel { get { return Get<string>(Game, "OpponentLabel"); } }
        public OnlineClient UiNet { get { return Get<OnlineClient>(Ui, "net"); } }

        public Transform FindTransform(string name) { return Find(Canvas, name); }

        static Transform Find(Transform root, string name)
        {
            foreach (Transform c in root)
            {
                if (c.gameObject.name == name) return c;
                Transform deep = Find(c, name);
                if (deep != null) return deep;
            }
            return null;
        }

        public GameObject Obj(string name) { Transform t = FindTransform(name); return t != null ? t.gameObject : null; }
        public bool IsActive(string name) { GameObject g = Obj(name); return g != null && g.activeInHierarchy; }
        public Button FindButton(string name) { GameObject g = Obj(name); return g != null ? g.GetComponent<Button>() : null; }
        public string TextOf(string name) { GameObject g = Obj(name); Text t = g != null ? g.GetComponent<Text>() : null; return t != null ? t.text : null; }
        public bool Visible(GameObject g) { return g != null && g.activeInHierarchy; }

        public bool Click(Button b)
        {
            if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) return false;
            b.onClick.Invoke();
            return true;
        }

        public bool Click(string name) { return Click(FindButton(name)); }

        public void SetInput(string uiField, string text)
        {
            Get<InputField>(Ui, uiField).text = text;
        }

        public void PressKey(KeyCode key) { Input.SimKeysDown.Add(key); }

        /// <summary>Đứt mạng đột ngột phía client của giao diện (không báo gì cho máy chủ).</summary>
        public void KillUiSocket()
        {
            var f = typeof(OnlineClient).GetField("transport", Any);
            ((OnlineTransport)f.GetValue(UiNet)).Dispose();
        }
    }
}
