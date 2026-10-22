using Il2Cppcozyhouse;
using Il2CppTMPro;
using Il2CppTS;
using UnityEngine;
using UnityEngine.UI;

namespace CozyClaude
{
    // A speech bubble that follows the character on screen. It lives on its own overlay canvas, so it is
    // never part of the room and never reaches the save file.
    internal sealed class Bubble
    {
        private GameObject _root;
        private RectTransform _panel;
        private TextMeshProUGUI _text;
        private Canvas _canvas;
        private Image _bg;
        private Outline _outline;
        private float _shownAt;
        private bool _emphasized;

        public bool Visible => _root != null && _root.activeSelf;

        // emphasized: the "waiting for you" reminder, opaque and bobbing. Otherwise a quieter status line.
        public void Show(string text, bool emphasized = true)
        {
            Ensure();
            if (_text.text != text) _text.text = text;
            if (emphasized != _emphasized || !_root.activeSelf)
            {
                _emphasized = emphasized;
                _bg.color = emphasized ? new Color(1f, 0.97f, 0.88f, 0.97f) : new Color(1f, 0.97f, 0.88f, 0.72f);
                _outline.enabled = emphasized;
                _text.fontSize = emphasized ? 15 : 13;
                _text.color = emphasized ? new Color(0.30f, 0.19f, 0.12f, 1f) : new Color(0.30f, 0.19f, 0.12f, 0.85f);
                _shownAt = Time.unscaledTime;
            }
            if (!_root.activeSelf) _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        // Call every frame while visible.
        public void Follow()
        {
            if (!Visible) return;
            var world = Singleton<World>.Instance;
            var cam = Camera.main;
            var character = world != null ? world.characterScript : null;
            if (cam == null || character == null || !character.gameObject.activeInHierarchy) { _panel.gameObject.SetActive(false); return; }
            _panel.gameObject.SetActive(true);
            var main = GameObject.Find("Canvas_Main");
            if (main != null) _canvas.scaleFactor = main.GetComponent<Canvas>().scaleFactor;
            var screen = cam.WorldToScreenPoint(character.transform.position + new Vector3(0f, 2.6f, 0f));
            // the reminder bobs gently so it catches the eye without flashing; the status line stays still
            var bob = _emphasized ? Mathf.Sin((Time.unscaledTime - _shownAt) * 3f) * 4f : 0f;
            _panel.position = new Vector3(screen.x, screen.y + bob * _canvas.scaleFactor, 0f);
        }

        private void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("CozyClaude_Bubble");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1500;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(_root.transform, false);
            _panel = panelGo.AddComponent<RectTransform>();
            _panel.pivot = new Vector2(0.5f, 0f);
            _bg = panelGo.AddComponent<Image>();
            _bg.color = new Color(1f, 0.97f, 0.88f, 0.97f);
            _bg.raycastTarget = false;
            _outline = panelGo.AddComponent<Outline>();
            _outline.effectColor = new Color(0.36f, 0.24f, 0.16f, 1f);
            _outline.effectDistance = new Vector2(2f, -2f);
            var fit = panelGo.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = panelGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset { left = 10, right = 10, top = 6, bottom = 6 };
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(panelGo.transform, false);
            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 15;
            _text.color = new Color(0.30f, 0.19f, 0.12f, 1f);
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            var toast = Singleton<ToastManager>.Instance;
            if (toast != null && toast.multiplayText != null) _text.font = toast.multiplayText.font;
            _root.SetActive(false);
        }
    }
}
