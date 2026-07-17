using TwoCT.Combat;
using TwoCT.Core;
using TwoCT.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwoCT.UI
{
    /// <summary>
    /// A single hand card. Lives on the editable <b>Card prefab</b> (built by the scene tool) —
    /// HandView instantiates one per card and calls <see cref="Bind"/>. Emulates the Hearthstone
    /// "physical" feel: eases toward its fanned home slot when idle, tilts toward drag velocity
    /// while held, springs back. Dropping in the upper play zone plays it.
    /// </summary>
    public class CardView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Image bg;
        [SerializeField] private Image artworkImage;
        [SerializeField] private Text nameText;
        [SerializeField] private Text costText;
        [SerializeField] private Text rulesText;
        [SerializeField] private CanvasGroup canvasGroup;

        public int HandIndex { get; private set; }
        public CardData Card { get; private set; }

        private HandView _hand;
        private RectTransform _rt;
        private Vector2 _home;
        private float _homeRotation;
        private bool _dragging;
        private Vector2 _lastPos;
        private float _wiggle;
        private bool _dealing;      // flying in from the deck
        private float _dealDelay;   // stagger before this card starts flying
        private bool _external;     // HandView drives the transform directly (reveal animation)
        private bool _shuffling;    // flying back into the deck on end-turn
        private float _shuffleDelay;
        private Vector2 _shuffleTarget;
        private const float PlayZoneY = 0.32f;
        private const float IdleEase = 14f;   // snappy settle for repositioning / spring-back
        private const float DealEase = 5.5f;  // slower, readable fly-in from the deck on draw
        private const float ShuffleEase = 9f; // fly-back-into-deck on end-turn

        /// <summary>Builds the card's child visuals if missing. Used to author the prefab AND as a runtime fallback.</summary>
        public void EnsureVisuals()
        {
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(150, 210);
            if (bg == null) bg = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (artworkImage == null)
            {
                var art = Ui.New("Art", rt, out _);
                artworkImage = art.gameObject.AddComponent<Image>();
                artworkImage.raycastTarget = false;
                artworkImage.preserveAspect = true;
                Ui.Anchor(art, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -52), new Vector2(128, 84), new Vector2(0.5f, 1));
                artworkImage.enabled = false;
            }
            if (nameText == null)
            {
                nameText = Ui.Label(rt, "", 20, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);
                Ui.Anchor(nameText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(138, 40), new Vector2(0.5f, 1));
            }
            if (costText == null)
            {
                costText = Ui.Label(rt, "", 26, FontStyle.Bold, new Color(0.6f, 0.85f, 1f), TextAnchor.UpperLeft);
                Ui.Anchor(costText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -6), new Vector2(40, 40), new Vector2(0.5f, 1));
            }
            if (rulesText == null)
            {
                rulesText = Ui.Label(rt, "", 15, FontStyle.Normal, new Color(0.9f, 0.9f, 0.9f), TextAnchor.LowerCenter);
                Ui.Anchor(rulesText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(134, 130), new Vector2(0.5f, 0));
            }
        }

        public void Bind(HandView hand, int index, CardData card, bool isCopy = false)
        {
            _hand = hand; HandIndex = index; Card = card;
            _rt = (RectTransform)transform;
            EnsureVisuals();
            if (nameText) nameText.text = card.cardName;
            if (costText)
            {
                // Show the discounted cost (Flawless / Bubble Power) so the number matches what you'll pay.
                var local = PlayerRegistry.Local;
                int cost = local != null ? local.EffectiveCost(card) : card.manaCost;
                costText.text = cost.ToString();
                costText.color = cost < card.manaCost ? new Color(0.5f, 1f, 0.6f) : new Color(0.6f, 0.85f, 1f);
            }
            if (rulesText) rulesText.text = card.RulesText;
            if (bg)
            {
                var c = ColorForRarity(card.rarity);
                // Copies get a slight lavender shade so you can tell them apart from real cards.
                if (isCopy) c = Color.Lerp(c, new Color(0.75f, 0.6f, 1f), 0.35f);
                bg.color = c;
            }
            if (artworkImage) { artworkImage.sprite = card.artwork; artworkImage.enabled = card.artwork != null; }
        }

        public void SetHome(Vector2 home, float rotation)
        {
            _home = home; _homeRotation = rotation;
            if (!_dragging && !_dealing && _rt != null) _rt.anchoredPosition = home;
        }

        /// <summary>When true, HandView animates this card's transform directly (e.g. the reveal
        /// of a randomly-cast card) and the idle easing below is suppressed.</summary>
        public void SetExternalControl(bool on) => _external = on;

        /// <summary>Start this card off at the deck position (small) so the idle easing flies it into
        /// its hand slot. <paramref name="delay"/> staggers multi-card deals.</summary>
        public void DealInFrom(Vector2 deckLocal, float delay)
        {
            if (_rt == null) _rt = (RectTransform)transform;
            _dealing = true; _dealDelay = delay;
            _rt.anchoredPosition = deckLocal;
            _rt.localScale = Vector3.one * 0.55f;
            _rt.localRotation = Quaternion.identity;
        }

        /// <summary>Fly this card back into the deck pile (end-of-turn shuffle), shrinking + fading.
        /// It stops responding to input; HandView destroys it on the next deal.</summary>
        public void ShuffleOutTo(Vector2 deckLocal, float delay)
        {
            if (_rt == null) _rt = (RectTransform)transform;
            _shuffling = true; _dealing = false; _dragging = false;
            _shuffleTarget = deckLocal; _shuffleDelay = delay;
            if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
        }

        private void Update()
        {
            if (_rt == null || _external) return;
            if (_shuffling)
            {
                if (_shuffleDelay > 0f) { _shuffleDelay -= Time.deltaTime; return; }   // staggered
                float k = ShuffleEase * Time.deltaTime;
                _rt.anchoredPosition = Vector2.Lerp(_rt.anchoredPosition, _shuffleTarget, k);
                _rt.localScale = Vector3.Lerp(_rt.localScale, Vector3.one * 0.5f, k);
                _rt.localRotation = Quaternion.Lerp(_rt.localRotation, Quaternion.identity, k);
                if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 0f, k);
                return;
            }
            if (_dragging)
            {
                Vector2 pos = _rt.anchoredPosition;
                float vx = pos.x - _lastPos.x;
                _wiggle = Mathf.Lerp(_wiggle, Mathf.Clamp(-vx * 1.5f, -18f, 18f), 12f * Time.deltaTime);
                _rt.localRotation = Quaternion.Euler(0, 0, _wiggle);
                _lastPos = pos;
            }
            else
            {
                float ease = IdleEase;
                if (_dealing)
                {
                    if (_dealDelay > 0f) { _dealDelay -= Time.deltaTime; return; }   // wait at the deck
                    ease = DealEase;                                                 // slower, visible draw
                    if (((Vector2)_rt.anchoredPosition - _home).sqrMagnitude < 4f) _dealing = false;
                }
                _rt.anchoredPosition = Vector2.Lerp(_rt.anchoredPosition, _home, ease * Time.deltaTime);
                _rt.localRotation = Quaternion.Lerp(_rt.localRotation, Quaternion.Euler(0, 0, _homeRotation), ease * Time.deltaTime);
                _rt.localScale = Vector3.Lerp(_rt.localScale, Vector3.one, ease * Time.deltaTime);
            }
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _dragging = true; _lastPos = _rt.anchoredPosition;
            if (canvasGroup) canvasGroup.blocksRaycasts = false;
            _rt.localScale = Vector3.one * 1.15f;
            _rt.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData e) => _rt.anchoredPosition += e.delta / _hand.CanvasScale;

        public void OnEndDrag(PointerEventData e)
        {
            _dragging = false;
            if (canvasGroup) canvasGroup.blocksRaycasts = true;
            _rt.localRotation = Quaternion.identity;
            if (e.position.y > Screen.height * PlayZoneY) _hand.TryPlay(this);
        }

        public void FlashInvalid() { if (bg) bg.color = new Color(1f, 0.5f, 0.5f); }

        private static Color ColorForRarity(CardRarity r) => r switch
        {
            CardRarity.Rare => new Color(0.45f, 0.2f, 0.5f),
            CardRarity.Uncommon => new Color(0.2f, 0.4f, 0.35f),
            _ => new Color(0.25f, 0.28f, 0.34f)
        };
    }
}
