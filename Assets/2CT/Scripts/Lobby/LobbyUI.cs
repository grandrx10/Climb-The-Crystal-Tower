using System.Collections.Generic;
using TwoCT.Core;
using TwoCT.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TwoCT.Lobby
{
    /// <summary>
    /// Styled, code-generated uGUI lobby (no prefab wiring needed). Two states:
    ///  - Connect: Create Lobby / Join (address) buttons.
    ///  - Lobby: live roster, selectable character cards (unique per player), Ready + Start.
    /// Driven by the networked LobbyController; refreshes on its NetworkList changes.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        // Palette
        private static readonly Color Bg = new Color(0.06f, 0.06f, 0.11f);
        private static readonly Color Panel = new Color(0.12f, 0.13f, 0.19f, 0.96f);
        private static readonly Color PanelSoft = new Color(0.17f, 0.18f, 0.25f);
        private static readonly Color Gold = new Color(0.96f, 0.80f, 0.35f);
        private static readonly Color TextDim = new Color(0.7f, 0.72f, 0.8f);
        private static readonly Color BtnNormal = new Color(0.20f, 0.22f, 0.30f);
        private static readonly Color BtnHover = new Color(0.28f, 0.31f, 0.42f);

        private Font _font;
        [Header("Auto-wired (built by 2CT ▸ Scenes tool; editable in the scene)")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _connectPanel, _lobbyPanel, _rosterList, _cardRow;
        [SerializeField] private InputField _addressField;
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _readyButton, _startButton;
        [SerializeField] private Button _hostButton, _joinButton, _leaveButton;
        [SerializeField] private Button _browseButton;
        [SerializeField] private Text _readyLabel;

        private readonly List<CharacterCard> _cards = new List<CharacterCard>();
#if UGS_LOBBY
        private LobbyBrowser _browser;
#endif
        private bool _hooked;

        private class CharacterCard { public int index; public GameObject go; public GameObject highlight; public GameObject takenVeil; public Button button; }

        // =====================================================================
        //  Build
        // =====================================================================
        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_canvas == null) BuildInEditor();   // fallback if the scene wasn't built with the tool
            WireButtons();
        }

        // Listeners are added here (runtime) rather than at build time, so they work whether the
        // hierarchy was baked in the editor or built at runtime.
        private void WireButtons()
        {
            if (_browseButton) _browseButton.onClick.AddListener(OpenBrowser);
            if (_hostButton) _hostButton.onClick.AddListener(() => ConnectionManager.Instance?.StartHost());
            if (_joinButton) _joinButton.onClick.AddListener(() => ConnectionManager.Instance?.StartClient(_addressField != null ? _addressField.text : "127.0.0.1"));
            if (_readyButton) _readyButton.onClick.AddListener(ToggleReady);
            if (_startButton) _startButton.onClick.AddListener(StartRun);
            if (_leaveButton) _leaveButton.onClick.AddListener(() => { LeaveUgsLobby(); ConnectionManager.Instance?.Shutdown(); });
        }

        /// <summary>Builds the lobby UI hierarchy. Called by the scene tool at edit time (bakes it
        /// into the scene, editable) or from Awake as a runtime fallback.</summary>
        public void BuildInEditor()
        {
            if (_canvas != null) return;
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();
            BuildConnectPanel();
            BuildLobbyPanel();
        }

        private void OnDestroy() => Unhook();

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return; // reliable at edit time too
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        private void BuildCanvas()
        {
            var go = new GameObject("LobbyCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f; // balance width/height so nothing runs off a non-16:9 view
            go.AddComponent<GraphicRaycaster>();

            MakeImage("Backdrop", _canvas.transform, Bg, Stretch());
            var title = Label(_canvas.transform, "CLIMBING THE CRYSTAL TOWER", 52, FontStyle.Bold, Gold, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(1400, 70));
            var sub = Label(_canvas.transform, "Co-op Lobby", 24, FontStyle.Normal, TextDim, TextAnchor.MiddleCenter);
            Anchor(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -140), new Vector2(1000, 40));
        }

        private void BuildConnectPanel()
        {
            _connectPanel = MakeImage("ConnectPanel", _canvas.transform, Panel, default).rectTransform;
            Anchor(_connectPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(560, 440));

            var head = Label(_connectPanel, "Start or Join a Run", 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Anchor(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(500, 44));

            // Primary path: online lobby browser (UGS Relay).
            _browseButton = MakeButton(_connectPanel, "Browse Online Lobbies", Gold, Color.black);
            Anchor(_browseButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(440, 66));

            var orLabel = Label(_connectPanel, "— or connect by direct IP —", 18, FontStyle.Normal, TextDim, TextAnchor.MiddleCenter);
            Anchor(orLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -178), new Vector2(440, 30));

            _hostButton = MakeButton(_connectPanel, "Host (Direct IP)", BtnNormal, Color.white);
            Anchor(_hostButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -226), new Vector2(440, 54));

            _addressField = InputFieldControl(_connectPanel, "127.0.0.1", out var fieldRT);
            Anchor(fieldRT, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -288), new Vector2(440, 50));

            _joinButton = MakeButton(_connectPanel, "Join (Direct IP)", BtnNormal, Color.white);
            Anchor(_joinButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -348), new Vector2(440, 54));
        }

        private void LeaveUgsLobby()
        {
#if UGS_LOBBY
            UgsLobbyService.Leave();
#endif
        }

        private void OpenBrowser()
        {
#if UGS_LOBBY
            if (_browser == null) _browser = LobbyBrowser.Create(_canvas.transform);
            _browser.Show();
#else
            if (_statusText != null) _statusText.text = "Online lobbies need the UGS Multiplayer package (see setup notes).";
            Debug.LogWarning("[2CT] Online lobbies require the UGS Multiplayer package + the UGS_LOBBY define.");
#endif
        }

        private void BuildLobbyPanel()
        {
            _lobbyPanel = MakeImage("LobbyPanel", _canvas.transform, new Color(0, 0, 0, 0), Stretch()).rectTransform;

            // Roster (left column)
            var rosterBg = MakeImage("Roster", _lobbyPanel, Panel, default);
            Anchor(rosterBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(210, -10), new Vector2(360, 520));
            var rHead = Label(rosterBg.rectTransform, "PARTY", 24, FontStyle.Bold, Gold, TextAnchor.UpperLeft);
            Anchor(rHead.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(170, -18), new Vector2(300, 34));
            _rosterList = Empty("RosterList", rosterBg.rectTransform);
            Anchor(_rosterList, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -64), new Vector2(0, 0));
            _rosterList.offsetMin = new Vector2(16, 16); _rosterList.offsetMax = new Vector2(-16, -64);

            // Character selection (centred on screen; roster sits to its left)
            var chooseHead = Label(_lobbyPanel, "CHOOSE YOUR CHARACTER", 26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Anchor(chooseHead.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 210), new Vector2(820, 40));
            _cardRow = Empty("CardRow", _lobbyPanel);
            Anchor(_cardRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(820, 300));

            // Footer: status + ready + start
            _statusText = Label(_lobbyPanel, "", 20, FontStyle.Normal, TextDim, TextAnchor.MiddleCenter);
            Anchor(_statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 170), new Vector2(1000, 30));

            _readyButton = MakeButton(_lobbyPanel, "Ready", BtnNormal, Color.white);
            Anchor(_readyButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170, 90), new Vector2(300, 62));
            _readyLabel = _readyButton.GetComponentInChildren<Text>();

            _startButton = MakeButton(_lobbyPanel, "START RUN", Gold, Color.black);
            Anchor(_startButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(170, 90), new Vector2(300, 62));

            _leaveButton = MakeButton(_lobbyPanel, "Leave", new Color(0.4f, 0.2f, 0.24f), Color.white);
            Anchor(_leaveButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(100, 45), new Vector2(160, 46));

            _lobbyPanel.gameObject.SetActive(false);
        }

        // =====================================================================
        //  State / refresh
        // =====================================================================
        private void Update()
        {
            var nm = NetworkManager.Singleton;
            bool connected = nm != null && (nm.IsClient || nm.IsServer);
            _connectPanel.gameObject.SetActive(!connected);
            _lobbyPanel.gameObject.SetActive(connected);

            if (connected && LobbyController.Instance != null && !_hooked) Hook();
            if (!connected && _hooked) Unhook();
        }

        private void Hook()
        {
            BuildCharacterCards();
            LobbyController.Instance.Slots.OnListChanged += OnSlotsChanged;
            _hooked = true;
            Refresh();
        }

        private void Unhook()
        {
            if (_hooked && LobbyController.Instance != null)
                LobbyController.Instance.Slots.OnListChanged -= OnSlotsChanged;
            _hooked = false;
        }

        private void OnSlotsChanged(NetworkListEvent<LobbySlot> _) => Refresh();

        private void BuildCharacterCards()
        {
            foreach (Transform c in _cardRow) Destroy(c.gameObject);
            _cards.Clear();
            var reg = ContentRegistry.Instance;
            if (reg == null) return;

            int n = reg.characters.Count;
            const float cardW = 240, cardH = 290, gap = 30;
            float totalW = n * cardW + (n - 1) * gap;
            for (int i = 0; i < n; i++)
            {
                var ch = reg.characters[i];
                float x = -totalW / 2f + i * (cardW + gap) + cardW / 2f;

                var highlight = MakeImage($"HL{i}", _cardRow, Gold, default);
                Anchor(highlight.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(cardW + 10, cardH + 10));

                var tint = ch.tint; tint.a = 1f;
                var card = MakeImage($"Card{i}", _cardRow, Color.Lerp(PanelSoft, tint, 0.35f), default);
                Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(cardW, cardH));

                // Portrait: the character's own sprite (untinted). Falls back to a theme-colour
                // swatch when the character has no baseSprite assigned yet. Honours the flip flags.
                var portrait = MakeImage($"Sw{i}", card.rectTransform, ch.baseSprite != null ? Color.white : tint, default);
                if (ch.baseSprite != null) { portrait.sprite = ch.baseSprite; portrait.preserveAspect = true; }
                Anchor(portrait.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -72), new Vector2(110, 110));
                portrait.rectTransform.localScale = new Vector3(ch.flipX ? -1f : 1f, ch.flipY ? -1f : 1f, 1f);

                var name = Label(card.rectTransform, ch.characterName, 26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                Anchor(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(cardW - 20, 34));

                var stats = Label(card.rectTransform, $"{ch.maxHP} HP\n{ch.startingDeck.Count} cards\n{ch.manaPerRound} mana / round", 18, FontStyle.Normal, TextDim, TextAnchor.UpperCenter);
                Anchor(stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -216), new Vector2(cardW - 30, 90));

                var veil = MakeImage($"Veil{i}", card.rectTransform, new Color(0, 0, 0, 0.65f), Stretch());
                var taken = Label(veil.rectTransform, "TAKEN", 24, FontStyle.Bold, new Color(1f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
                Anchor(taken.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cardW, 40));

                int index = i;
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card;
                var colors = btn.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); btn.colors = colors;
                btn.onClick.AddListener(() => LobbyController.Instance.SelectCharacterServerRpc(index));

                _cards.Add(new CharacterCard { index = i, go = card.gameObject, highlight = highlight.gameObject, takenVeil = veil.gameObject, button = btn });
            }
        }

        private void Refresh()
        {
            var lobby = LobbyController.Instance;
            var nm = NetworkManager.Singleton;
            if (lobby == null || nm == null) return;

            // Roster rows
            foreach (Transform c in _rosterList) Destroy(c.gameObject);
            var reg = ContentRegistry.Instance;
            int row = 0;
            foreach (var s in lobby.Slots)
            {
                string cname = reg != null && s.character >= 0 && s.character < reg.characters.Count ? reg.characters[s.character].characterName : "choosing…";
                string you = s.clientId == nm.LocalClientId ? "  (you)" : "";
                var rowText = Label(_rosterList, $"P{s.clientId}{you}\n<size=16>{cname}   {(s.ready ? "<color=#7CFC7C>READY</color>" : "")}</size>", 20, FontStyle.Bold, Color.white, TextAnchor.UpperLeft);
                rowText.supportRichText = true;
                var rt = rowText.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
                rt.anchoredPosition = new Vector2(0, -row * 66);
                rt.sizeDelta = new Vector2(0, 60);
                row++;
            }

            // Character card states
            int myChar = -1; bool myReady = false;
            foreach (var s in lobby.Slots) if (s.clientId == nm.LocalClientId) { myChar = s.character; myReady = s.ready; }

            foreach (var card in _cards)
            {
                bool takenByOther = false;
                foreach (var s in lobby.Slots) if (s.character == card.index && s.clientId != nm.LocalClientId) takenByOther = true;
                card.highlight.SetActive(myChar == card.index);
                card.takenVeil.SetActive(takenByOther);
                card.button.interactable = !takenByOther && !myReady;
            }

            // Footer
            _readyLabel.text = myReady ? "Unready" : "Ready";
            _readyButton.interactable = myChar >= 0;
            _startButton.gameObject.SetActive(nm.IsHost);
            _startButton.interactable = lobby.AllReady();
            _statusText.text = lobby.AllReady()
                ? (nm.IsHost ? "Everyone's ready — press START RUN." : "Waiting for the host to start…")
                : "Pick a character and ready up. All players must be ready.";
        }

        // =====================================================================
        //  Actions
        // =====================================================================
        private void ToggleReady()
        {
            var lobby = LobbyController.Instance; var nm = NetworkManager.Singleton;
            if (lobby == null || nm == null) return;
            bool ready = false;
            foreach (var s in lobby.Slots) if (s.clientId == nm.LocalClientId) ready = s.ready;
            lobby.SetReadyServerRpc(!ready);
        }

        private void StartRun() => LobbyController.Instance?.StartRunServerRpc();

        // =====================================================================
        //  uGUI helpers
        // =====================================================================
        private static RectTransform Empty(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private Image MakeImage(string name, Transform parent, Color color, Rect stretch)
        {
            var rt = Empty(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (stretch == Stretch()) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
            return img;
        }

        private Text Label(Transform parent, string text, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var rt = Empty("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // No onClick here: listeners are added at runtime in WireButtons() so they survive an
        // editor-baked hierarchy (code-added listeners don't serialize).
        private Button MakeButton(Transform parent, string text, Color bg, Color fg)
        {
            var img = MakeImage("Button", parent, bg, default);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            btn.colors = colors;
            var label = Label(img.rectTransform, text, 24, FontStyle.Bold, fg, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return btn;
        }

        private InputField InputFieldControl(Transform parent, string initial, out RectTransform rt)
        {
            var img = MakeImage("Input", parent, PanelSoft, default);
            rt = img.rectTransform;
            var field = img.gameObject.AddComponent<InputField>();
            var textComp = Label(img.rectTransform, "", 22, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
            textComp.raycastTarget = true; textComp.supportRichText = false;
            textComp.rectTransform.anchorMin = Vector2.zero; textComp.rectTransform.anchorMax = Vector2.one;
            textComp.rectTransform.offsetMin = new Vector2(14, 4); textComp.rectTransform.offsetMax = new Vector2(-14, -4);
            var placeholder = Label(img.rectTransform, "address…", 22, FontStyle.Italic, TextDim, TextAnchor.MiddleLeft);
            placeholder.rectTransform.anchorMin = Vector2.zero; placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(14, 4); placeholder.rectTransform.offsetMax = new Vector2(-14, -4);
            field.textComponent = textComp; field.placeholder = placeholder; field.targetGraphic = img;
            field.text = initial;
            return field;
        }

        private static void Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
        }

        private static Rect Stretch() => new Rect(-999, -999, 0, 0); // sentinel meaning "full-stretch"
    }
}
