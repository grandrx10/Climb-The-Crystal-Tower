using System;
using System.Collections.Generic;
using TwoCT.Data;
using TwoCT.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Bottom-of-screen dialogue box (Undertale-style). The hierarchy is built by the scene tool
    /// (editable in the editor); if a scene doesn't have one placed, a runtime singleton is
    /// created as a fallback. Text types out; E completes the current line then advances. Two
    /// modes: LOCAL (solo, driven here) and EXTERNAL (group, lines pushed by the networked
    /// Interactable, E raises <see cref="ExternalSkipPressed"/> so one player advances everyone).
    /// </summary>
    public class DialogueBox : MonoBehaviour
    {
        public enum Mode { Local, External }

        [SerializeField] private GameObject panel;
        [SerializeField] private Text speaker;
        [SerializeField] private Text body;

        private static DialogueBox _instance;
        public static DialogueBox Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<DialogueBox>();
                    if (_instance == null)
                    {
                        var go = new GameObject("DialogueBox");
                        _instance = go.AddComponent<DialogueBox>(); // Awake builds + registers
                    }
                }
                return _instance;
            }
        }

        public bool IsVisible { get; private set; }
        public bool IsTyping { get; private set; }

        /// <summary>True while a dialogue box is on screen. Cheap (no singleton auto-create) so
        /// per-frame callers like free-roam movement can gate on it to freeze the player mid-convo.</summary>
        public static bool AnyOpen => _instance != null && _instance.IsVisible;
        public Mode CurrentMode { get; private set; }
        public event Action ExternalSkipPressed;

        private const float CharsPerSecond = 45f;
        private IList<DialogueLine> _lines;
        private int _index;
        private Action _onComplete;
        private string _fullText = "";
        private float _typedChars;
        private string _activeSpeaker = "";

        private void Awake()
        {
            if (_instance == null) _instance = this;
            if (panel == null) BuildInEditor();  // fallback if not placed via the scene tool
            if (panel != null) panel.SetActive(false);
        }

        // =====================================================================
        //  Public control
        // =====================================================================
        public void PlayLocal(IList<DialogueLine> lines, Action onComplete = null)
        {
            if (lines == null || lines.Count == 0) { onComplete?.Invoke(); return; }
            CurrentMode = Mode.Local;
            _lines = lines; _index = 0; _onComplete = onComplete;
            Show(); SetLine(lines[0]);
        }

        public void BeginExternal() { CurrentMode = Mode.External; Show(); }
        public void ShowExternalLine(string sp, string text) { CurrentMode = Mode.External; Show(); SetLine(sp, text); }
        public void EndExternal() => Hide();

        // =====================================================================
        //  Internals
        // =====================================================================
        private void SetLine(DialogueLine line)
        {
            line.FireActions();                 // swap sprites / remove walls the instant this line shows
            SetLine(line.speaker, line.text);
        }

        private void SetLine(string sp, string text)
        {
            if (speaker) speaker.text = sp ?? "";
            _activeSpeaker = sp ?? "";
            _fullText = text ?? "";
            _typedChars = 0f; IsTyping = true;
            if (body) body.text = "";
        }

        private void Show() { IsVisible = true; if (panel) panel.SetActive(true); }

        private void Hide()
        {
            IsVisible = false; IsTyping = false; _lines = null; _onComplete = null;
            if (panel) panel.SetActive(false);
        }

        private void Update()
        {
            if (!IsVisible) return;
            if (IsTyping)
            {
                _typedChars += CharsPerSecond * Time.unscaledDeltaTime;
                int n = Mathf.Min(_fullText.Length, Mathf.FloorToInt(_typedChars));
                if (body) body.text = _fullText.Substring(0, n);
                if (n >= _fullText.Length) IsTyping = false;
                // Wobble the speaking character (if a SpeakingJiggle is registered under this name).
                SpeakingJiggle.TalkByName(_activeSpeaker, 0.12f);
            }

            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame) OnSkipPressed();
        }

        private void OnSkipPressed()
        {
            // Group/synced dialogue: a press while MY text is still typing just finishes the type-out
            // locally (each client completes its own line — advancement is the only thing that must
            // stay in lockstep). Once my line is fully shown, a press advances EVERYONE via the
            // server. So a single E on a line you've already read moves the party on — no double-press.
            if (CurrentMode == Mode.External)
            {
                if (IsTyping) CompleteExternalLine();
                else ExternalSkipPressed?.Invoke();
                return;
            }

            // Solo/local: first press finishes the type-out, next advances.
            if (IsTyping) { if (body) body.text = _fullText; IsTyping = false; return; }
            _index++;
            if (_lines != null && _index < _lines.Count) SetLine(_lines[_index]);
            else { var cb = _onComplete; Hide(); cb?.Invoke(); }
        }

        /// <summary>Instantly finish typing the current external line locally — a player's first E on a
        /// still-typing group line completes it on their own screen (the next E advances everyone).</summary>
        public void CompleteExternalLine()
        {
            _typedChars = _fullText != null ? _fullText.Length : 0f;
            if (body) body.text = _fullText;
            IsTyping = false;
        }

        // =====================================================================
        //  Build (edit-time or runtime fallback)
        // =====================================================================
        public void BuildInEditor()
        {
            Ui.EnsureEventSystem();
            var canvas = Ui.Canvas("DialogueCanvas", transform, 500);
            var box = Ui.Panel("Panel", canvas.transform, new Color(0, 0, 0, 0.85f));
            Ui.Anchor(box.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1200, 180), new Vector2(0.5f, 0));
            panel = box.gameObject;

            speaker = Ui.Label(box.rectTransform, "", 24, FontStyle.Bold, new Color(1f, 0.9f, 0.5f), TextAnchor.UpperLeft);
            Ui.Anchor(speaker.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -12), new Vector2(-40, 34), new Vector2(0.5f, 1));
            speaker.rectTransform.offsetMin = new Vector2(24, speaker.rectTransform.offsetMin.y);

            body = Ui.Label(box.rectTransform, "", 22, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);
            Ui.Anchor(body.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -52), new Vector2(-48, 118), new Vector2(0.5f, 1));

            panel.SetActive(false);
        }
    }
}
