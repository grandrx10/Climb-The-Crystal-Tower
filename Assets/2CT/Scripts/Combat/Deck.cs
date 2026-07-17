using System.Collections.Generic;
using TwoCT.Data;

namespace TwoCT.Combat
{
    /// <summary>
    /// A player's runtime deck. Server-authoritative: the host owns the full pile and only the
    /// owning client is told its hand. Implements the doc's draw/shuffle rules:
    ///  - draw N per round (bounded by cards remaining; empty = "Deck is empty"),
    ///  - at end of round, played AND unplayed cards are shuffled to the bottom.
    /// A parallel <see cref="_handIsCopy"/> flags Copy-created duplicates: they render tinted and are
    /// TEMPORARY — they never rejoin the draw pile (played, discarded, or left over, a copy just vanishes).
    /// </summary>
    public class Deck
    {
        private readonly List<CardData> _drawPile = new List<CardData>();
        private readonly List<CardData> _hand = new List<CardData>();
        private readonly List<bool> _handIsCopy = new List<bool>();       // parallel to _hand
        private readonly List<CardData> _usedThisRound = new List<CardData>();
        private readonly List<CardData> _stored = new List<CardData>();   // Bubble Storage (returns next turn)
        private readonly List<bool> _storedIsCopy = new List<bool>();
        private System.Random _rng;

        public IReadOnlyList<CardData> Hand => _hand;
        public IReadOnlyList<bool> HandIsCopy => _handIsCopy;
        public int DrawPileCount => _drawPile.Count;
        public bool IsEmpty => _drawPile.Count == 0;

        public void Initialize(IEnumerable<CardData> startingDeck, int seed)
        {
            _drawPile.Clear();
            _hand.Clear();
            _handIsCopy.Clear();
            _usedThisRound.Clear();
            _stored.Clear();
            _storedIsCopy.Clear();
            _rng = new System.Random(seed);
            if (startingDeck != null)
                foreach (var c in startingDeck)
                    if (c != null) _drawPile.Add(c);
            Shuffle(_drawPile);
        }

        /// <summary>Draw up to <paramref name="count"/> cards into the hand. Returns how many were actually drawn.</summary>
        public int DrawCards(int count)
        {
            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                if (_drawPile.Count == 0) break;   // "Deck is empty" — stop, don't error
                var top = _drawPile[0];
                _drawPile.RemoveAt(0);
                _hand.Add(top);
                _handIsCopy.Add(false);
                drawn++;
            }
            return drawn;
        }

        /// <summary>Remove a card from the hand as "played"; returns false if not in hand. A played
        /// copy vanishes (not added to the used pile), so it can't rejoin the deck.</summary>
        public bool PlayCardAt(int handIndex)
        {
            if (handIndex < 0 || handIndex >= _hand.Count) return false;
            var card = _hand[handIndex];
            bool isCopy = handIndex < _handIsCopy.Count && _handIsCopy[handIndex];
            _hand.RemoveAt(handIndex);
            _handIsCopy.RemoveAt(handIndex);
            if (!isCopy) _usedThisRound.Add(card);
            return true;
        }

        public CardData PeekHand(int handIndex) =>
            (handIndex >= 0 && handIndex < _hand.Count) ? _hand[handIndex] : null;

        /// <summary>Add a card to the hand (Copy). <paramref name="isCopy"/> flags it for a tinted look.</summary>
        public void AddCardToHand(CardData card, bool isCopy)
        {
            if (card == null) return;
            _hand.Add(card);
            _handIsCopy.Add(isCopy);
        }

        /// <summary>Remove the last <paramref name="count"/> cards from the hand and return them (for the
        /// caller to fire activate-on-discard triggers). Non-copies go to the used pile; copies vanish.</summary>
        public List<CardData> RemoveLastHandCards(int count)
        {
            var taken = new List<CardData>();
            for (int i = 0; i < count && _hand.Count > 0; i++)
            {
                int last = _hand.Count - 1;
                var card = _hand[last];
                bool isCopy = _handIsCopy[last];
                _hand.RemoveAt(last);
                _handIsCopy.RemoveAt(last);
                if (!isCopy) _usedThisRound.Add(card);
                taken.Add(card);
            }
            return taken;
        }

        /// <summary>Remove the whole hand and return it (for triggers). Non-copies go to used; copies vanish.</summary>
        public List<CardData> RemoveWholeHand()
        {
            var taken = new List<CardData>(_hand);
            for (int i = 0; i < _hand.Count; i++)
                if (i >= _handIsCopy.Count || !_handIsCopy[i]) _usedThisRound.Add(_hand[i]);
            _hand.Clear();
            _handIsCopy.Clear();
            return taken;
        }

        /// <summary>Discard the entire hand into the used pile (copies vanish); returns how many were discarded.</summary>
        public int DiscardHand()
        {
            int n = _hand.Count;
            for (int i = 0; i < _hand.Count; i++)
                if (i >= _handIsCopy.Count || !_handIsCopy[i]) _usedThisRound.Add(_hand[i]);
            _hand.Clear();
            _handIsCopy.Clear();
            return n;
        }

        /// <summary>Bubble Storage: set the current hand aside (NOT a discard) to be returned next turn.</summary>
        public void StoreHand()
        {
            _stored.AddRange(_hand);
            _storedIsCopy.AddRange(_handIsCopy);
            _hand.Clear();
            _handIsCopy.Clear();
        }

        /// <summary>Return previously-stored cards to the hand (on top of the normal draw), preserving
        /// their copy flags. Called at the start of the turn after drawing.</summary>
        public void ReturnStoredToHand()
        {
            for (int i = 0; i < _stored.Count; i++)
            {
                _hand.Add(_stored[i]);
                _handIsCopy.Add(i < _storedIsCopy.Count && _storedIsCopy[i]);
            }
            _stored.Clear();
            _storedIsCopy.Clear();
        }

        /// <summary>Draw a random card straight out of the draw pile (Mask of Wild Magic). Null if empty.</summary>
        public CardData DrawRandomFromPile()
        {
            if (_drawPile.Count == 0) return null;
            int idx = _rng.Next(_drawPile.Count);
            var card = _drawPile[idx];
            _drawPile.RemoveAt(idx);
            _usedThisRound.Add(card);
            return card;
        }

        /// <summary>End the attack turn: shuffle played + unplayed cards and place them at the bottom.
        /// Leftover copies are dropped (temporary), never rejoining the pile.</summary>
        public void EndRound()
        {
            var returning = new List<CardData>(_hand.Count + _usedThisRound.Count);
            for (int i = 0; i < _hand.Count; i++)
                if (i >= _handIsCopy.Count || !_handIsCopy[i]) returning.Add(_hand[i]);
            returning.AddRange(_usedThisRound);
            _hand.Clear();
            _handIsCopy.Clear();
            _usedThisRound.Clear();
            Shuffle(returning);
            _drawPile.AddRange(returning); // bottom of pile
        }

        /// <summary>Add a card permanently to the deck (card-pack reward). Goes to the bottom.</summary>
        public void AddCard(CardData card)
        {
            if (card != null) _drawPile.Add(card);
        }

        private void Shuffle(List<CardData> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
