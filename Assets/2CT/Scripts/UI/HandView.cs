using System.Collections;
using System.Collections.Generic;
using TwoCT.Combat;
using TwoCT.Core;
using TwoCT.Data;
using UnityEngine;
using UnityEngine.UI;

namespace TwoCT.UI
{
    /// <summary>
    /// The local player's hand + End Turn button + ally target-picker. The canvas, hand row and
    /// button are built by the scene tool (editable in the editor); cards are instantiated from
    /// the editable <b>Card prefab</b>. Only the local player sees a hand, and only during Attack.
    /// </summary>
    public class HandView : MonoBehaviour
    {
        [Header("Auto-wired (built by 2CT ▸ Scenes tool)")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform handRoot;
        [SerializeField] private Button endTurnButton;
        [Tooltip("Editable Card prefab; if null, a basic card is built at runtime.")]
        [SerializeField] private CardView cardPrefab;

        [SerializeField] private float cardSpacing = 160f;

        // Where drawn cards fly from / revealed cards pop from (handRoot-local, matching card slots).
        private static readonly Vector2 DeckOrigin = new Vector2(620f, 30f);

        private readonly List<CardView> _cards = new List<CardView>();
        private readonly List<string> _prevHandIds = new List<string>();
        private GameObject _targetPicker;
        private RectTransform _deckPile;

        public float CanvasScale => canvas != null ? canvas.scaleFactor : 1f;

        private void Awake()
        {
            if (canvas == null) BuildInEditor();
            EnsureDeckPile();
            if (endTurnButton != null) endTurnButton.onClick.AddListener(EndTurn);
            SetHandVisible(false);
        }

        private void OnEnable()
        {
            CombatEvents.HandUpdated += OnHandUpdated;
            CombatEvents.PhaseChanged += OnPhaseChanged;
            CombatEvents.CardRevealed += OnCardRevealed;
        }

        private void OnDisable()
        {
            CombatEvents.HandUpdated -= OnHandUpdated;
            CombatEvents.PhaseChanged -= OnPhaseChanged;
            CombatEvents.CardRevealed -= OnCardRevealed;
        }

        private void OnPhaseChanged(CombatPhase phase)
        {
            SetHandVisible(phase == CombatPhase.Attack);
            if (phase != CombatPhase.Attack) _prevHandIds.Clear();   // fresh deal-in next attack turn
        }

        private void SetHandVisible(bool visible)
        {
            if (handRoot) handRoot.gameObject.SetActive(visible);
            if (endTurnButton) endTurnButton.gameObject.SetActive(visible);
            if (!visible) CloseTargetPicker();
        }

        private void OnHandUpdated(string[] cardIds)
        {
            var registry = ContentRegistry.Instance;
            var list = new List<CardData>();
            var ids = new List<string>();          // raw tokens (a trailing '*' marks a Copy) — used for the diff
            var isCopyFlags = new List<bool>();
            foreach (var token in cardIds)
            {
                bool copy = token.EndsWith("*");
                string id = copy ? token.Substring(0, token.Length - 1) : token;
                var card = registry != null ? registry.GetCard(id) : null;
                if (card != null) { list.Add(card); ids.Add(token); isCopyFlags.Add(copy); }
            }

            // Some plays send the hand twice with identical contents (e.g. Vine Strike: once from
            // its draw effect, once at end of play). Rebuilding on the second, redundant update
            // would destroy the just-dealt card and snap it home, cancelling its draw-in animation.
            // If the card sequence is unchanged, the hand is already correct — leave it alone.
            if (SameSequence(ids, _prevHandIds)) return;

            foreach (var c in _cards) if (c) Destroy(c.gameObject);
            _cards.Clear();

            // Which cards are freshly drawn (vs already in hand last update)? Multiset diff by id, so
            // playing a card just repositions the rest while genuinely new cards fly in from the deck.
            var prevCounts = new Dictionary<string, int>();
            foreach (var id in _prevHandIds) { prevCounts.TryGetValue(id, out var c); prevCounts[id] = c + 1; }
            var isNew = new bool[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                if (prevCounts.TryGetValue(ids[i], out var c) && c > 0) { prevCounts[ids[i]] = c - 1; isNew[i] = false; }
                else isNew[i] = true;
            }

            for (int i = 0; i < list.Count; i++) _cards.Add(CreateCard(i, list[i], isCopyFlags[i]));
            LayoutHand();

            int newOrder = 0;
            for (int i = 0; i < _cards.Count; i++)
                if (isNew[i]) _cards[i].DealInFrom(DeckOrigin, 0.05f * newOrder++);   // staggered fly-in

            _prevHandIds.Clear();
            _prevHandIds.AddRange(ids);

            // A fresh hand means it's this player's turn again — re-enable End Turn (unless dead).
            var local = PlayerRegistry.Local;
            SetEndTurnInteractable(local == null || local.IsAlive);
        }

        private void SetEndTurnInteractable(bool on)
        {
            if (endTurnButton != null) endTurnButton.interactable = on;
        }

        // Order-sensitive id comparison used to drop redundant identical hand refreshes.
        private static bool SameSequence(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // A random-cast card (e.g. Mask of Wild Magic): if it's the local player's, pop the pulled
        // card up from the deck, hold it so it can be read, then let it drift away.
        private void OnCardRevealed(int casterSlot, string cardId)
        {
            var local = PlayerRegistry.Local;
            if (local == null || local.Slot.Value != casterSlot) return;
            var card = ContentRegistry.Instance != null ? ContentRegistry.Instance.GetCard(cardId) : null;
            if (card == null || handRoot == null) return;
            StartCoroutine(RevealRoutine(card));
        }

        private IEnumerator RevealRoutine(CardData card)
        {
            var cv = CreateCard(-1, card);
            cv.SetExternalControl(true);
            var rt = (RectTransform)cv.transform;
            rt.SetAsLastSibling();
            var cg = cv.GetComponent<CanvasGroup>();
            if (cg != null) cg.blocksRaycasts = false;

            Vector2 mid = new Vector2(0f, 300f);   // reveal spot above the hand
            rt.anchoredPosition = DeckOrigin;
            rt.localScale = Vector3.one * 0.55f;

            float t = 0f, rise = 0.22f;             // fly up from the deck, growing
            while (t < rise)
            {
                t += Time.deltaTime; float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / rise), 3f);
                rt.anchoredPosition = Vector2.Lerp(DeckOrigin, mid, e);
                rt.localScale = Vector3.Lerp(Vector3.one * 0.55f, Vector3.one * 1.35f, e);
                yield return null;
            }
            rt.anchoredPosition = mid; rt.localScale = Vector3.one * 1.35f;

            yield return new WaitForSeconds(0.7f);  // hold so the player can read it

            t = 0f; float fade = 0.3f; Vector2 end = mid + new Vector2(0f, 90f);
            while (t < fade)
            {
                t += Time.deltaTime; float p = Mathf.Clamp01(t / fade);
                rt.anchoredPosition = Vector2.Lerp(mid, end, p);
                if (cg != null) cg.alpha = 1f - p;
                yield return null;
            }
            Destroy(cv.gameObject);
        }

        private void EnsureDeckPile()
        {
            if (_deckPile != null || handRoot == null) return;
            var rt = Ui.New("DeckPile", handRoot, out var go);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.18f, 0.26f, 0.9f);
            img.raycastTarget = false;
            rt.sizeDelta = new Vector2(96f, 132f);
            rt.anchoredPosition = DeckOrigin;
            var label = Ui.Label(rt, "DECK", 16, FontStyle.Bold, new Color(0.75f, 0.82f, 1f), TextAnchor.MiddleCenter);
            Ui.Stretch(label.rectTransform);
            _deckPile = rt;
        }

        private CardView CreateCard(int index, CardData card, bool isCopy = false)
        {
            CardView cv;
            if (cardPrefab != null) cv = Instantiate(cardPrefab, handRoot);
            else
            {
                var go = new GameObject($"Card_{index}", typeof(RectTransform));
                go.transform.SetParent(handRoot, false);
                cv = go.AddComponent<CardView>();
            }
            cv.Bind(this, index, card, isCopy);
            return cv;
        }

        private void LayoutHand()
        {
            int n = _cards.Count;
            float totalWidth = (n - 1) * cardSpacing;
            for (int i = 0; i < n; i++)
            {
                float x = -totalWidth * 0.5f + i * cardSpacing;
                float fan = n > 1 ? Mathf.Lerp(6f, -6f, (float)i / (n - 1)) : 0f;
                float lift = Mathf.Abs(i - (n - 1) * 0.5f);
                _cards[i].SetHome(new Vector2(x, 30 - lift * 6f), fan);
            }
        }

        // =====================================================================
        //  Playing cards
        // =====================================================================
        public void TryPlay(CardView view)
        {
            var local = PlayerRegistry.Local;
            if (local == null || CombatManager.Instance == null) return;
            if (local.ManaValue < view.Card.manaCost) { view.FlashInvalid(); CombatEvents.RaiseToast("Not enough mana"); return; }
            if (view.Card.NeedsTargetSelection) OpenTargetPicker(view);
            else CombatManager.Instance.PlayCardServerRpc(view.HandIndex, -1);
        }

        private void OpenTargetPicker(CardView view)
        {
            CloseTargetPicker();
            bool wantDead = view.Card.targetType == TargetType.DeadAlly;
            var candidates = new List<PlayerCombatant>();
            foreach (var p in PlayerRegistry.All)
                if (p != null && (wantDead ? !p.IsAlive : p.IsAlive)) candidates.Add(p);
            if (candidates.Count == 0) { CombatEvents.RaiseToast("No valid target"); return; }

            var panel = Ui.Panel("TargetPicker", canvas.transform, new Color(0, 0, 0, 0.85f));
            Ui.Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280, 70 + candidates.Count * 60));
            _targetPicker = panel.gameObject;
            var head = Ui.Label(panel.rectTransform, "Choose a target", 22, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);
            Ui.Anchor(head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(260, 30));

            for (int i = 0; i < candidates.Count; i++)
            {
                var target = candidates[i];
                var btn = Ui.Btn(panel.rectTransform, $"Player {target.Slot.Value + 1}  ({target.HP.Value} HP)", new Color(0.25f, 0.5f, 0.8f), Color.white, 18);
                Ui.Anchor(btn.GetComponent<RectTransform>(), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -52 - i * 58), new Vector2(240, 48));
                int handIndex = view.HandIndex; int slot = target.Slot.Value;
                btn.onClick.AddListener(() => { CombatManager.Instance.PlayCardServerRpc(handIndex, slot); CloseTargetPicker(); });
            }
        }

        private void CloseTargetPicker() { if (_targetPicker) Destroy(_targetPicker); _targetPicker = null; }

        private void EndTurn()
        {
            CloseTargetPicker();
            // Shuffle the hand back into the deck (fly to the pile, shrink, fade) and grey the button.
            // We then DESTROY those cards ourselves after the animation, rather than leaving them for
            // the next hand refresh to clear. Otherwise, when you're the LAST player to end, the phase
            // flips to Defend the same instant — deactivating the hand mid-animation — and the frozen
            // cards reappear (and only then get cleared) at the start of your next turn.
            if (_cards.Count > 0)
            {
                var retiring = new List<CardView>(_cards);
                for (int i = 0; i < retiring.Count; i++)
                    if (retiring[i]) retiring[i].ShuffleOutTo(DeckOrigin, 0.04f * i);
                _cards.Clear();
                StartCoroutine(RetireCards(retiring));
            }
            SetEndTurnInteractable(false);
            if (CombatManager.Instance != null) CombatManager.Instance.EndTurnServerRpc();
        }

        // Destroy the shuffled-out cards after their animation. Runs on HandView (which stays active),
        // so the cards are cleared even if the hand root gets deactivated by the phase change first.
        private IEnumerator RetireCards(List<CardView> retiring)
        {
            yield return new WaitForSeconds(0.6f);
            foreach (var c in retiring) if (c) Destroy(c.gameObject);
        }

        // =====================================================================
        //  Build (edit-time or runtime fallback)
        // =====================================================================
        public void BuildInEditor()
        {
            Ui.EnsureEventSystem();
            canvas = Ui.Canvas("HandCanvas", transform, 90);

            handRoot = Ui.New("HandRoot", canvas.transform, out _);
            Ui.Anchor(handRoot, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(1200, 240), new Vector2(0.5f, 0));

            endTurnButton = Ui.Btn(canvas.transform, "END TURN", new Color(0.85f, 0.75f, 0.2f), Color.black, 24);
            Ui.Anchor(endTurnButton.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-120, 40), new Vector2(200, 64), new Vector2(0.5f, 0));
        }
    }
}
