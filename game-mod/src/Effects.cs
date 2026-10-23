using System.Collections.Generic;
using Il2Cppcozyhouse;
using Il2CppTMPro;
using Il2CppTS;
using UnityEngine;
using UnityEngine.UI;

namespace CozyClaude
{
    // The room's little reactions, drawn on an overlay canvas of their own like the bubble: confetti over the
    // character when Claude commits, merges a pull request or gets a test run to pass, and "z"s drifting up from the
    // character while Claude's context window is nearly full. Nothing here is part of the room or reaches the save.
    internal sealed class Effects
    {
        private sealed class Piece
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Pos;      // screen pixels
            public Vector2 Velocity; // reference pixels per second
            public float Spin;
            public float Flutter;
            public float Born;
            public float Life;
            public Color Color;
        }

        private sealed class Zee
        {
            public RectTransform Rect;
            public TextMeshProUGUI Text;
            public float Born;
            public float Sway;
        }

        private static readonly Color[] Warm =
        {
            new(1f, 0.80f, 0.30f), new(1f, 0.55f, 0.45f), new(0.55f, 0.85f, 0.70f), new(0.50f, 0.72f, 0.95f), new(0.98f, 0.62f, 0.80f),
        };
        private static readonly Color[] Grand =
        {
            new(1f, 0.80f, 0.30f), new(0.72f, 0.55f, 0.95f), new(0.50f, 0.72f, 0.95f), new(0.98f, 0.62f, 0.80f), new(0.55f, 0.85f, 0.70f), new(1f, 1f, 1f),
        };
        private static readonly Color[] Green =
        {
            new(0.45f, 0.82f, 0.45f), new(0.70f, 0.90f, 0.40f), new(1f, 0.85f, 0.35f), new(0.55f, 0.85f, 0.75f),
        };
        private static readonly Color ZColor = new(0.30f, 0.34f, 0.66f, 1f);

        private const float Gravity = 700f;
        private const float FallSpeed = 90f;
        private const float ZLife = 2.8f;
        private const float ZEvery = 0.9f;
        private const int MaxPieces = 160;

        private GameObject _root;
        private Canvas _canvas;
        private TMP_FontAsset _font;
        private readonly List<Piece> _pieces = new();
        private readonly Stack<Piece> _spare = new();
        private readonly List<Zee> _zees = new();
        private readonly Stack<Zee> _spareZees = new();
        private readonly List<(float At, int Count, Color[] Colors)> _bursts = new();
        private bool _sleepy;
        private float _nextZ;

        // a party for one of the plugin's celebration kinds: "commit", "merge" (the biggest) or "tests"
        public void Celebrate(string kind)
        {
            var now = Time.unscaledTime;
            switch (kind)
            {
                case "merge":
                    _bursts.Add((now, 45, Grand));
                    _bursts.Add((now + 0.35f, 40, Grand));
                    _bursts.Add((now + 0.7f, 40, Grand));
                    break;
                case "tests":
                    _bursts.Add((now, 32, Green));
                    break;
                default:
                    _bursts.Add((now, 40, Warm));
                    break;
            }
        }

        public bool Sleepy => _sleepy;

        public void SetSleepy(bool sleepy)
        {
            if (sleepy && !_sleepy) _nextZ = 0f;
            _sleepy = sleepy;
        }

        public void ClearConfetti()
        {
            _bursts.Clear();
            while (_pieces.Count > 0) Recycle(_pieces[^1]);
        }

        public void ClearZees()
        {
            _sleepy = false;
            while (_zees.Count > 0) Recycle(_zees[^1]);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _pieces.Clear();
            _spare.Clear();
            _zees.Clear();
            _spareZees.Clear();
            _bursts.Clear();
            _sleepy = false;
        }

        // Every frame. Each half throws on its own, so the caller can switch off one and keep the other.
        public void AnimateConfetti()
        {
            if (_pieces.Count == 0 && _bursts.Count == 0) return;
            if (!Prepare(new Vector3(0f, 2.2f, 0f), out var head)) return;
            var now = Time.unscaledTime;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (var i = _bursts.Count - 1; i >= 0; i--)
            {
                if (_bursts[i].At > now) continue;
                Spawn(_bursts[i].Count, _bursts[i].Colors, head, now);
                _bursts.RemoveAt(i);
            }
            var scale = _canvas.scaleFactor;
            for (var i = _pieces.Count - 1; i >= 0; i--)
            {
                var p = _pieces[i];
                var age = now - p.Born;
                if (age >= p.Life) { Recycle(p); continue; }
                // paper, not pebbles: it slows to a gentle fall and flutters sideways on the way down
                p.Velocity.y = Mathf.Max(p.Velocity.y - Gravity * dt, -FallSpeed);
                p.Velocity.x *= 1f - 1.5f * dt;
                p.Pos += (p.Velocity + new Vector2(Mathf.Sin(age * 5f + p.Flutter) * 40f, 0f)) * (dt * scale);
                p.Rect.position = new Vector3(p.Pos.x, p.Pos.y, 0f);
                p.Rect.Rotate(0f, 0f, p.Spin * dt);
                var c = p.Color;
                c.a = Mathf.Clamp01((p.Life - age) / 0.7f);
                p.Image.color = c;
            }
        }

        public void AnimateZees()
        {
            if (!_sleepy && _zees.Count == 0) return;
            // beside the head, away from the bubble above it
            if (!Prepare(new Vector3(1.3f, 1.0f, 0f), out var head)) return;
            var now = Time.unscaledTime;
            if (_sleepy && now >= _nextZ)
            {
                _nextZ = now + ZEvery;
                SpawnZee(now);
            }
            var scale = _canvas.scaleFactor;
            for (var i = _zees.Count - 1; i >= 0; i--)
            {
                var z = _zees[i];
                var t = (now - z.Born) / ZLife;
                if (t >= 1f) { Recycle(z); continue; }
                // drifts up and to the right, swaying, growing, fading in quickly and out slowly
                var x = 60f * t + Mathf.Sin(t * 6f + z.Sway) * 8f;
                var y = 45f * t;
                z.Rect.position = new Vector3(head.x + x * scale, head.y + y * scale, 0f);
                z.Text.fontSize = 22f + 18f * t;
                var c = ZColor;
                c.a = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
                z.Text.color = c;
            }
        }

        // The screen point at `offset` (world units) from the character, with the canvas made and scaled like the
        // game's own UI. False (and everything hidden) while there is no character to follow.
        private bool Prepare(Vector3 offset, out Vector3 screen)
        {
            screen = default;
            Ensure();
            var world = Singleton<World>.Instance;
            var cam = Camera.main;
            var character = world != null ? world.characterScript : null;
            if (cam == null || character == null || !character.gameObject.activeInHierarchy)
            {
                if (_root.activeSelf) _root.SetActive(false);
                return false;
            }
            if (!_root.activeSelf) _root.SetActive(true);
            var main = GameObject.Find("Canvas_Main");
            if (main != null) _canvas.scaleFactor = main.GetComponent<Canvas>().scaleFactor;
            screen = cam.WorldToScreenPoint(character.transform.position + offset);
            return true;
        }

        private void Spawn(int count, Color[] colors, Vector3 origin, float now)
        {
            for (var i = 0; i < count && _pieces.Count < MaxPieces; i++)
            {
                var p = _spare.Count > 0 ? _spare.Pop() : NewPiece();
                var angle = Random.Range(-60f, 60f) * Mathf.Deg2Rad;
                var speed = Random.Range(220f, 480f);
                p.Velocity = new Vector2(Mathf.Sin(angle) * speed, Mathf.Cos(angle) * speed);
                p.Pos = new Vector2(origin.x + Random.Range(-8f, 8f), origin.y);
                p.Spin = Random.Range(-540f, 540f);
                p.Flutter = Random.Range(0f, 6.28f);
                p.Born = now;
                p.Life = Random.Range(2.2f, 3.2f);
                p.Color = colors[Random.Range(0, colors.Length)];
                p.Image.color = p.Color;
                p.Rect.sizeDelta = Random.value < 0.3f ? new Vector2(7f, 7f) : new Vector2(Random.Range(5f, 8f), Random.Range(10f, 14f));
                p.Rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                p.Rect.position = new Vector3(p.Pos.x, p.Pos.y, 0f);
                p.Rect.gameObject.SetActive(true);
                _pieces.Add(p);
            }
        }

        private void SpawnZee(float now)
        {
            var z = _spareZees.Count > 0 ? _spareZees.Pop() : NewZee();
            z.Born = now;
            z.Sway = Random.Range(0f, 6.28f);
            z.Text.text = _zees.Count % 2 == 0 ? "Z" : "z";
            z.Text.color = new Color(ZColor.r, ZColor.g, ZColor.b, 0f);
            z.Rect.gameObject.SetActive(true);
            _zees.Add(z);
        }

        private Piece NewPiece()
        {
            var go = new GameObject("Confetti");
            go.transform.SetParent(_root.transform, false);
            var rect = go.AddComponent<RectTransform>();
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return new Piece { Rect = rect, Image = image };
        }

        private Zee NewZee()
        {
            var go = new GameObject("Zee");
            go.transform.SetParent(_root.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(60f, 60f);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) text.font = _font;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return new Zee { Rect = rect, Text = text };
        }

        private void Recycle(Piece p)
        {
            _pieces.Remove(p);
            p.Rect.gameObject.SetActive(false);
            _spare.Push(p);
        }

        private void Recycle(Zee z)
        {
            _zees.Remove(z);
            z.Rect.gameObject.SetActive(false);
            _spareZees.Push(z);
        }

        private void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("CozyClaude_Effects");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1490; // under the bubble
            var toast = Singleton<ToastManager>.Instance;
            if (toast != null && toast.multiplayText != null) _font = toast.multiplayText.font;
        }
    }
}
