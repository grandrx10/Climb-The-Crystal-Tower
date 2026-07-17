using System.Collections.Generic;
using TwoCT.Combat;
using TwoCT.Core;
using TwoCT.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TwoCT.UI
{
    /// <summary>
    /// Post-victory reward picker. A self-building, scene-persistent singleton (no scene wiring):
    /// on <see cref="CombatEvents.RewardOffered"/> it shows a dimmed page with a "pick N of M"
    /// title and the offered cards as clickable panels. Clicking submits the choice to the server
    /// and shows a brief waiting state until the next offer (or completion) arrives. Rolls are
    /// independent per player, so each client only ever sees and picks its own offers.
    /// </summary>
    public class RewardScreen : MonoBehaviour
    {
        private static RewardScreen _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("RewardScreen");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<RewardScreen>();
        }

        private GameObject _root;          // the dim full-screen page (toggled on/off)
        private RectTransform _cardRow;
        private Text _title;
        private Text _status;
        private readonly List<GameObject> _cards = new List<GameObject>();

        private void Awake()
        {
            Build();
            SetVisible(false);
            CombatEvents.RewardOffered += OnOffered;
            CombatEvents.RewardsComplete += OnComplete;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            CombatEvents.RewardOffered -= OnOffered;
            CombatEvents.RewardsComplete -= OnComplete;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // Safety: always close when a new scene loads (e.g. the timeout path returned to the level
        // while this client was still deciding).
        private void OnSceneLoaded(Scene s, LoadSceneMode m) { CancelInvoke(nameof(Hide)); ClearCards(); SetVisible(false); }

        // =====================================================================
        //  Event handlers
        // =====================================================================
        private void OnOffered(int pickIndex, int totalPicks, string[] cardIds)
        {
            CancelInvoke(nameof(Hide));
            SetVisible(true);
            if (_status) _status.gameObject.SetActive(false);
            if (_title) _title.text = $"Choose a reward  —  pick {pickIndex + 1} of {totalPicks}";
            BuildCards(cardIds);
        }

        private void OnComplete()
        {
            ClearCards();
            if (_title) _title.text = "Rewards claimed!";
            if (_status) { _status.gameObject.SetActive(true); _status.text = "Returning to the tower..."; }
            CancelInvoke(nameof(Hide));
            Invoke(nameof(Hide), 1.5f);   // scene load usually hides us first; this is a fallback
        }

        private void OnPick(string cardId)
        {
            // Show the "locked" state BEFORE submitting: on a host the server's response RPCs run
            // synchronously inside the Submit call below (OnOffered for the next pick, or OnComplete).
            // If we cleared/relabelled AFTER submitting we'd wipe the freshly-offered next cards.
            ClearCards();
            if (_title) _title.text = "Choice locked in";
            if (_status) { _status.gameObject.SetActive(true); _status.text = "Waiting for the next card..."; }

            var cm = CombatManager.Instance;
            if (cm != null) cm.SubmitRewardChoiceServerRpc(cardId);
        }

        private void Hide() => SetVisible(false);
        private void SetVisible(bool on) { if (_root) _root.SetActive(on); }

        // =====================================================================
        //  Card panels
        // =====================================================================
        private void BuildCards(string[] ids)
        {
            ClearCards();
            var reg = ContentRegistry.Instance;
            foreach (var id in ids)
            {
                var card = reg != null ? reg.GetCard(id) : null;
                if (card != null) _cards.Add(MakeCardPanel(card));
            }
        }

        private void ClearCards()
        {
            foreach (var g in _cards) if (g) Destroy(g);
            _cards.Clear();
        }

        private GameObject MakeCardPanel(CardData card)
        {
            var panel = Ui.Panel("RewardCard", _cardRow, ColorForRarity(card.rarity));
            var rt = panel.rectTransform;
            rt.sizeDelta = new Vector2(240, 340);

            var btn = panel.gameObject.AddComponent<Button>();
            btn.targetGraphic = panel;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            btn.colors = colors;
            string id = card.Id;
            btn.onClick.AddListener(() => OnPick(id));

            var name = Ui.Label(rt, card.cardName, 24, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);
            Ui.Anchor(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -14), new Vector2(-16, 52), new Vector2(0.5f, 1));

            var cost = Ui.Label(rt, $"{card.manaCost} MP", 20, FontStyle.Bold, new Color(0.6f, 0.85f, 1f), TextAnchor.UpperLeft);
            Ui.Anchor(cost.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -70), new Vector2(120, 26), new Vector2(0, 1));

            var cat = Ui.Label(rt, card.category.ToString(), 18, FontStyle.Italic, CategoryColor(card.category), TextAnchor.UpperRight);
            Ui.Anchor(cat.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -70), new Vector2(150, 26), new Vector2(1, 1));

            var rules = Ui.Label(rt, card.RulesText, 18, FontStyle.Normal, new Color(0.92f, 0.92f, 0.92f), TextAnchor.MiddleCenter);
            Ui.Anchor(rules.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, -12), new Vector2(-24, -108), new Vector2(0.5f, 0.5f));

            return panel.gameObject;
        }

        // =====================================================================
        //  Build (runtime, self-contained)
        // =====================================================================
        private void Build()
        {
            Ui.EnsureEventSystem();
            var canvas = Ui.Canvas("RewardCanvas", transform, 800);   // above HUD, below ScreenFader (999)
            var dim = Ui.Panel("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.82f));
            Ui.Stretch(dim.rectTransform);
            _root = dim.gameObject;

            _title = Ui.Label(dim.rectTransform, "Choose a reward", 40, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);
            Ui.Anchor(_title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1400, 64));

            Ui.New("CardRow", dim.rectTransform, out var rowGo);
            _cardRow = (RectTransform)rowGo.transform;
            Ui.Anchor(_cardRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960, 360));
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 44;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            layout.childControlWidth = false; layout.childControlHeight = false;

            _status = Ui.Label(dim.rectTransform, "Waiting...", 28, FontStyle.Bold, new Color(0.8f, 0.85f, 1f), TextAnchor.LowerCenter);
            Ui.Anchor(_status.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(900, 40));
            _status.gameObject.SetActive(false);
        }

        private static Color ColorForRarity(CardRarity r) => r switch
        {
            CardRarity.Rare => new Color(0.45f, 0.2f, 0.5f),
            CardRarity.Uncommon => new Color(0.2f, 0.4f, 0.35f),
            _ => new Color(0.25f, 0.28f, 0.34f)
        };

        private static Color CategoryColor(CardCategory c) => c switch
        {
            CardCategory.Incineration => new Color(1f, 0.5f, 0.3f),
            CardCategory.Life => new Color(0.5f, 1f, 0.55f),
            CardCategory.Wild => new Color(0.85f, 0.6f, 1f),
            CardCategory.Severance => new Color(0.9f, 0.35f, 0.4f),
            CardCategory.Bubble => new Color(0.45f, 0.85f, 1f),
            _ => new Color(0.8f, 0.82f, 0.9f)
        };
    }
}
