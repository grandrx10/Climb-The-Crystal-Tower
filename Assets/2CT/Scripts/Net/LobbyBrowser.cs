// Gated until the UGS multiplayer package is installed AND the UGS_LOBBY scripting define is set.
#if UGS_LOBBY
using System.Collections.Generic;
using TwoCT.UI;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace TwoCT.Net
{
    /// <summary>
    /// Online lobby browser overlay: create a named session (host via Relay) or scroll a live list
    /// of open sessions and join one. Self-building (no scene wiring) — <see cref="LobbyUI"/> calls
    /// <see cref="Show"/>. All UGS calls go through <see cref="UgsLobbyService"/>; once the SDK
    /// connects NGO, LobbyUI swaps to the in-lobby roster automatically.
    /// </summary>
    public class LobbyBrowser : MonoBehaviour
    {
        private static readonly Color Panel = new Color(0.10f, 0.11f, 0.17f, 0.98f);
        private static readonly Color Row = new Color(0.17f, 0.18f, 0.25f);
        private static readonly Color Gold = new Color(0.96f, 0.80f, 0.35f);
        private static readonly Color Dim = new Color(0.7f, 0.72f, 0.8f);

        private GameObject _root;
        private RectTransform _listContent;
        private InputField _nameField;
        private Text _status;
        private bool _busy;

        public static LobbyBrowser Create(Transform parent)
        {
            var go = new GameObject("LobbyBrowser");
            go.transform.SetParent(parent, false);
            var lb = go.AddComponent<LobbyBrowser>();
            lb.Build();
            lb.Hide();
            return lb;
        }

        public void Show() { if (_root) _root.SetActive(true); Refresh(); }
        public void Hide() { if (_root) _root.SetActive(false); }

        // Once the session connects (create/join succeeded and NGO started), close the browser so
        // the in-lobby roster shows through.
        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer)) Hide();
        }

        // =====================================================================
        //  Actions (async void — UI handlers; guarded against double-clicks)
        // =====================================================================
        private async void Refresh()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Loading lobbies…");
            try
            {
                var sessions = await UgsLobbyService.QuerySessionsAsync();
                PopulateList(sessions);
                SetStatus(sessions.Count == 0 ? "No open lobbies. Create one!" : $"{sessions.Count} lobb{(sessions.Count == 1 ? "y" : "ies")} found.");
            }
            catch (System.Exception e) { SetStatus("Couldn't list lobbies: " + e.Message); }
            finally { _busy = false; }
        }

        private async void CreateSession()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Creating lobby…");
            try { await UgsLobbyService.CreateSessionAsync(_nameField != null ? _nameField.text : "Crystal Tower Run"); }
            catch (System.Exception e) { SetStatus("Create failed: " + e.Message); _busy = false; }
            // On success the SDK starts hosting; LobbyUI's Update flips to the in-lobby view.
        }

        private async void Join(string sessionId)
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Joining…");
            try { await UgsLobbyService.JoinSessionAsync(sessionId); }
            catch (System.Exception e) { SetStatus("Join failed: " + e.Message); _busy = false; }
        }

        private void SetStatus(string s) { if (_status) _status.text = s; }

        // =====================================================================
        //  List
        // =====================================================================
        private void PopulateList(IList<ISessionInfo> sessions)
        {
            if (_listContent == null) return;
            foreach (Transform c in _listContent) Destroy(c.gameObject);

            foreach (var s in sessions)
            {
                int players = Mathf.Max(0, s.MaxPlayers - s.AvailableSlots);
                bool full = s.AvailableSlots <= 0;

                var rowImg = Ui.Panel("Lobby", _listContent, Row);
                var rt = rowImg.rectTransform;
                rt.sizeDelta = new Vector2(0, 64);
                var le = rowImg.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 64; le.preferredHeight = 64;

                var label = Ui.Label(rt, $"<b>{s.Name}</b>   <size=16>{players} / {s.MaxPlayers}</size>",
                    22, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
                label.rectTransform.anchorMin = new Vector2(0, 0); label.rectTransform.anchorMax = new Vector2(1, 1);
                label.rectTransform.offsetMin = new Vector2(18, 0); label.rectTransform.offsetMax = new Vector2(-150, 0);

                var join = Ui.Btn(rt, full ? "Full" : "Join", full ? new Color(0.3f, 0.3f, 0.34f) : Gold, Color.black, 20);
                var jrt = join.GetComponent<RectTransform>();
                jrt.anchorMin = new Vector2(1, 0.5f); jrt.anchorMax = new Vector2(1, 0.5f); jrt.pivot = new Vector2(1, 0.5f);
                jrt.anchoredPosition = new Vector2(-12, 0); jrt.sizeDelta = new Vector2(120, 48);
                join.interactable = !full;
                string id = s.Id;
                join.onClick.AddListener(() => Join(id));
            }
        }

        // =====================================================================
        //  Build
        // =====================================================================
        private void Build()
        {
            var canvas = Ui.Canvas("LobbyBrowserCanvas", transform, 200);
            var dim = Ui.Panel("Dim", canvas.transform, new Color(0, 0, 0, 0.6f));
            Ui.Stretch(dim.rectTransform);
            _root = dim.gameObject;

            var panel = Ui.Panel("Panel", dim.rectTransform, Panel);
            Ui.Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 640));

            var head = Ui.Label(panel.rectTransform, "ONLINE LOBBIES", 30, FontStyle.Bold, Gold, TextAnchor.UpperCenter);
            Ui.Anchor(head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(700, 40));

            _nameField = MakeInput(panel.rectTransform, "My lobby name", out var fieldRt);
            Ui.Anchor(fieldRt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -80), new Vector2(460, 52), new Vector2(0, 1));
            var create = Ui.Btn(panel.rectTransform, "Create Lobby", Gold, Color.black, 22);
            var crt = create.GetComponent<RectTransform>();
            Ui.Anchor(crt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -80), new Vector2(220, 52), new Vector2(1, 1));
            create.onClick.AddListener(CreateSession);

            var scrollGo = Ui.New("Scroll", panel.rectTransform, out var scrollObj);
            Ui.Anchor(scrollGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(700, 380));
            var scrollImg = scrollObj.AddComponent<Image>(); scrollImg.color = new Color(0, 0, 0, 0.25f);
            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;

            var viewport = Ui.New("Viewport", scrollGo, out var viewportObj);
            Ui.Stretch(viewport);
            viewportObj.AddComponent<RectMask2D>();

            _listContent = Ui.New("Content", viewport, out _);
            _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1); _listContent.pivot = new Vector2(0.5f, 1);
            _listContent.offsetMin = new Vector2(0, 0); _listContent.offsetMax = new Vector2(0, 0);
            var vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8; vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            var fitter = _listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = _listContent;

            _status = Ui.Label(panel.rectTransform, "", 18, FontStyle.Italic, Dim, TextAnchor.MiddleCenter);
            Ui.Anchor(_status.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 96), new Vector2(700, 28));

            var refresh = Ui.Btn(panel.rectTransform, "Refresh", new Color(0.2f, 0.22f, 0.3f), Color.white, 22);
            Ui.Anchor(refresh.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-150, 40), new Vector2(240, 54));
            refresh.onClick.AddListener(Refresh);

            var back = Ui.Btn(panel.rectTransform, "Back", new Color(0.4f, 0.2f, 0.24f), Color.white, 22);
            Ui.Anchor(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(150, 40), new Vector2(240, 54));
            back.onClick.AddListener(Hide);
        }

        private InputField MakeInput(Transform parent, string placeholder, out RectTransform rt)
        {
            var img = Ui.Panel("Input", parent, new Color(0.17f, 0.18f, 0.25f));
            rt = img.rectTransform;
            var field = img.gameObject.AddComponent<InputField>();
            var text = Ui.Label(rt, "", 22, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
            text.raycastTarget = true;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(14, 4); text.rectTransform.offsetMax = new Vector2(-14, -4);
            var ph = Ui.Label(rt, placeholder, 22, FontStyle.Italic, Dim, TextAnchor.MiddleLeft);
            ph.rectTransform.anchorMin = Vector2.zero; ph.rectTransform.anchorMax = Vector2.one;
            ph.rectTransform.offsetMin = new Vector2(14, 4); ph.rectTransform.offsetMax = new Vector2(-14, -4);
            field.textComponent = text; field.placeholder = ph; field.targetGraphic = img;
            return field;
        }
    }
}
#endif
