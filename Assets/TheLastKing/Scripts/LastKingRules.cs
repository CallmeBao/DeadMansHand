using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TheLastKing
{
    public enum CardKind { King, Citizen, Slave }
    public enum MatchEndReason { None, BloodCoins, SocialCollapse, EmptyHand, InvalidKingState }
    public enum TurnEventKind { CardRevealed, BloodCoinAdded, CardReturned, CardTransformed, CardDestroyed, CardDrawn, InitiativeTransferred, MatchFinished }

    // Cards retain their identity when their role changes. Snapshots never expose mutable lists.
    public sealed class Card
    {
        public int Id { get; private set; }
        public CardKind Kind { get; private set; }
        public Card(int id, CardKind kind) { Id = id; Kind = kind; }
        public override string ToString() { return Kind + " #" + Id; }
    }

    public sealed class PlayerState
    {
        public IReadOnlyList<Card> Hand { get; private set; }
        public int BloodCoins { get; private set; }
        public PlayerState(IEnumerable<Card> hand, int bloodCoins)
        {
            if (hand == null) throw new ArgumentNullException("hand");
            Hand = new List<Card>(hand).AsReadOnly();
            BloodCoins = bloodCoins;
        }
    }

    public sealed class MatchState
    {
        public IReadOnlyList<PlayerState> Players { get; private set; }
        public IReadOnlyList<Card> Deck { get; private set; }
        public IReadOnlyList<Card> DestroyedCards { get; private set; }
        public IReadOnlyList<Card> InPlayCards { get; private set; }
        public int DeckCount { get { return Deck.Count; } }
        public int DestroyedCount { get { return DestroyedCards.Count; } }
        public int InitiativeSeat { get; private set; }
        // Number of already resolved turns. The first selectable turn is TurnIndex + 1.
        public int TurnIndex { get; private set; }
        public bool IsFinished { get; private set; }
        public int WinnerSeat { get; private set; }
        public bool IsDraw { get { return IsFinished && WinnerSeat == -1; } }
        public MatchEndReason EndReason { get; private set; }

        // Public for save snapshots and deterministic scenario checks; LastKingMatch validates it.
        public MatchState(IEnumerable<PlayerState> players, IEnumerable<Card> deck,
            IEnumerable<Card> destroyedCards, int initiativeSeat = 1, int turnIndex = 0,
            IEnumerable<Card> inPlayCards = null, bool isFinished = false,
            int winnerSeat = -1, MatchEndReason endReason = MatchEndReason.None)
        {
            if (players == null || deck == null || destroyedCards == null)
                throw new ArgumentNullException("Snapshot collections");
            Players = new List<PlayerState>(players).AsReadOnly();
            Deck = new List<Card>(deck).AsReadOnly();
            DestroyedCards = new List<Card>(destroyedCards).AsReadOnly();
            InPlayCards = new List<Card>(inPlayCards ?? new Card[0]).AsReadOnly();
            InitiativeSeat = initiativeSeat; TurnIndex = turnIndex;
            IsFinished = isFinished; WinnerSeat = winnerSeat; EndReason = endReason;
        }
    }

    public sealed class TurnEvent
    {
        public TurnEventKind Kind { get; private set; }
        public int Seat { get; private set; }
        public Card BeforeCard { get; private set; }
        public Card AfterCard { get; private set; }
        public int Amount { get; private set; }
        public string Message { get; private set; }
        internal TurnEvent(TurnEventKind kind, int seat, Card before, Card after, int amount, string message)
        { Kind = kind; Seat = seat; BeforeCard = before; AfterCard = after; Amount = amount; Message = message; }
    }

    public sealed class TurnResult
    {
        public MatchState Before { get; private set; }
        public MatchState After { get; private set; }
        public IReadOnlyList<Card> PlayedCards { get; private set; }
        public int PairWinnerSeat { get; private set; }
        public int CoinRecipientSeat { get; private set; }
        public IReadOnlyList<TurnEvent> Events { get; private set; }
        public string Summary { get; private set; }
        internal TurnResult(MatchState before, MatchState after, Card[] played, int pairWinner,
            int coinRecipient, List<TurnEvent> events, string summary)
        {
            Before = before; After = after;
            PlayedCards = new List<Card>(played).AsReadOnly();
            PairWinnerSeat = pairWinner; CoinRecipientSeat = coinRecipient;
            Events = new List<TurnEvent>(events).AsReadOnly(); Summary = summary;
        }
    }

    public sealed class MatchOutcome
    {
        public bool IsFinished { get; private set; }
        public int WinnerSeat { get; private set; }
        public MatchEndReason Reason { get; private set; }
        internal MatchOutcome(bool finished, int winner, MatchEndReason reason)
        { IsFinished = finished; WinnerSeat = winner; Reason = reason; }
    }

    public static class LastKingRules
    {
        public const int HandLimit = 3;
        public const int BloodCoinLimit = 5;
        public const int TotalCards = 18;

        public static int Compare(CardKind first, CardKind second)
        {
            ValidateKind(first); ValidateKind(second);
            if (first == CardKind.King && second == CardKind.King)
                throw new InvalidOperationException("Two active Kings are an invalid game state.");
            if (first == second) return 0;
            return (first == CardKind.King && second == CardKind.Citizen) ||
                   (first == CardKind.Citizen && second == CardKind.Slave) ||
                   (first == CardKind.Slave && second == CardKind.King) ? 1 : -1;
        }

        public static MatchOutcome EvaluateBloodCoins(int seat0, int seat1)
        {
            if (seat0 < 0 || seat1 < 0) throw new ArgumentOutOfRangeException("Blood coins");
            bool dead0 = seat0 >= BloodCoinLimit, dead1 = seat1 >= BloodCoinLimit;
            return dead0 || dead1
                ? new MatchOutcome(true, dead0 && dead1 ? -1 : dead0 ? 1 : 0, MatchEndReason.BloodCoins)
                : new MatchOutcome(false, -1, MatchEndReason.None);
        }

        public static int ActiveCitizenCount(MatchState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            int count = 0;
            foreach (PlayerState p in state.Players) foreach (Card c in p.Hand) if (c.Kind == CardKind.Citizen) count++;
            foreach (Card c in state.Deck) if (c.Kind == CardKind.Citizen) count++;
            foreach (Card c in state.InPlayCards) if (c.Kind == CardKind.Citizen) count++;
            return count;
        }

        public static void ValidateState(MatchState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (state.Players.Count != 2) throw new ArgumentException("Exactly two seats are required.");
            if (state.InitiativeSeat < 0 || state.InitiativeSeat > 1 || state.TurnIndex < 0)
                throw new ArgumentException("Invalid initiative or turn index.");
            if (state.WinnerSeat < -1 || state.WinnerSeat > 1)
                throw new ArgumentException("Invalid winner seat.");
            if ((!state.IsFinished && (state.WinnerSeat != -1 || state.EndReason != MatchEndReason.None)) ||
                (state.IsFinished && state.EndReason == MatchEndReason.None))
                throw new ArgumentException("Outcome fields are inconsistent.");
            var ids = new HashSet<int>();
            int kings = 0;
            foreach (PlayerState player in state.Players)
            {
                if (player == null || player.Hand.Count > HandLimit || player.BloodCoins < 0)
                    throw new ArgumentException("Invalid player state.");
                if (!state.IsFinished && (player.Hand.Count == 0 || player.BloodCoins >= BloodCoinLimit))
                    throw new ArgumentException("An empty hand or lethal coin count must be resolved before another turn.");
                foreach (Card c in player.Hand) { AddCard(ids, c); if (c.Kind == CardKind.King) kings++; }
            }
            if (state.DeckCount > 12) throw new ArgumentException("The common deck has at most twelve Citizens.");
            foreach (Card c in state.Deck)
            {
                AddCard(ids, c);
                if (c.Kind != CardKind.Citizen) throw new ArgumentException("The common deck contains only Citizens.");
            }
            foreach (Card c in state.DestroyedCards) AddCard(ids, c);
            foreach (Card c in state.InPlayCards) { AddCard(ids, c); if (c.Kind == CardKind.King) kings++; }
            if (ids.Count != TotalCards) throw new ArgumentException("Exactly eighteen unique card identities must be conserved.");
            if (kings != 1 && !(state.IsFinished && state.EndReason == MatchEndReason.InvalidKingState && state.WinnerSeat == -1))
                throw new ArgumentException("Exactly one active King must exist.");
            if (!state.IsFinished && state.InPlayCards.Count != 0)
                throw new ArgumentException("No unresolved played cards may remain at turn start.");
            if (!state.IsFinished && ActiveCitizenCount(state) == 0)
                throw new ArgumentException("Social Collapse must be resolved before another turn.");
        }

        static void AddCard(HashSet<int> ids, Card card)
        {
            if (card == null || card.Id < 0 || !ids.Add(card.Id))
                throw new ArgumentException("Card identities must be non-negative and unique.");
            ValidateKind(card.Kind);
        }
        internal static void ValidateKind(CardKind kind)
        {
            if (kind < CardKind.King || kind > CardKind.Slave) throw new ArgumentOutOfRangeException("kind");
        }
    }

    public sealed class LastKingMatch
    {
        public MatchState State { get; private set; }
        public LastKingMatch() : this(CreateInitialState()) { }
        public LastKingMatch(MatchState state) { LastKingRules.ValidateState(state); State = state; }

        public static MatchState CreateInitialState()
        {
            var deck = new List<Card>();
            for (int id = 7; id <= 18; id++) deck.Add(new Card(id, CardKind.Citizen));
            return new MatchState(new[] {
                new PlayerState(new[] { new Card(1, CardKind.King), new Card(2, CardKind.Citizen), new Card(3, CardKind.Citizen) }, 0),
                new PlayerState(new[] { new Card(4, CardKind.Slave), new Card(5, CardKind.Citizen), new Card(6, CardKind.Citizen) }, 0)
            }, deck, new Card[0]);
        }

        public TurnResult ResolveTurn(int seat0CardIndex, int seat1CardIndex)
        {
            if (State.IsFinished) throw new InvalidOperationException("The match is already finished.");
            LastKingRules.ValidateState(State);
            if (seat0CardIndex < 0 || seat0CardIndex >= State.Players[0].Hand.Count ||
                seat1CardIndex < 0 || seat1CardIndex >= State.Players[1].Hand.Count)
                throw new ArgumentOutOfRangeException("Selected card index");
            MatchState before = State;
            var turn = new Resolution(before);
            Card[] played = { turn.Hands[0][seat0CardIndex], turn.Hands[1][seat1CardIndex] };
            turn.Hands[0].RemoveAt(seat0CardIndex); turn.Hands[1].RemoveAt(seat1CardIndex);
            turn.InPlay.AddRange(played);
            turn.Add(TurnEventKind.CardRevealed, 0, played[0], played[0], 0, "Seat 0 revealed " + played[0].Kind + ".");
            turn.Add(TurnEventKind.CardRevealed, 1, played[1], played[1], 0, "Seat 1 revealed " + played[1].Kind + ".");
            int comparison = LastKingRules.Compare(played[0].Kind, played[1].Kind);
            int pairWinner = comparison == 0 ? -1 : comparison > 0 ? 0 : 1;
            int coinRecipient = pairWinner < 0 ? -1 : 1 - pairWinner;
            bool tax = played[0].Kind == CardKind.Citizen && played[1].Kind == CardKind.Citizen;
            if (tax)
                for (int seat = 0; seat < 2; seat++)
                    foreach (Card card in turn.Hands[seat])
                        if (card.Kind == CardKind.King) coinRecipient = seat;

            if (coinRecipient >= 0)
            {
                turn.Coins[coinRecipient]++;
                turn.Add(TurnEventKind.BloodCoinAdded, coinRecipient, null, null, 1,
                    tax ? "Blood Tax: the King holder receives one Blood Coin." : "The losing seat receives one Blood Coin.");
            }
            MatchOutcome blood = LastKingRules.EvaluateBloodCoins(turn.Coins[0], turn.Coins[1]);
            if (blood.IsFinished)
            {
                // Exact v1.1 priority: lethal damage stops before conversion, destruction or drawing.
                turn.Finish(blood.WinnerSeat, blood.Reason);
                return Commit(before, turn, played, pairWinner, coinRecipient, tax);
            }

            if (pairWinner < 0)
            {
                turn.Destroy(0, played[0]); turn.Destroy(1, played[1]);
            }
            else
            {
                int other = 1 - pairWinner;
                Card winning = played[pairWinner], losing = played[other];
                if (winning.Kind == CardKind.King)
                {
                    turn.Return(pairWinner, winning);
                    turn.Transform(other, losing, CardKind.Slave);
                }
                else if (winning.Kind == CardKind.Slave)
                {
                    turn.Destroy(other, losing);
                    turn.Transform(pairWinner, winning, CardKind.King);
                }
                else
                {
                    turn.Destroy(other, losing);
                    turn.Return(pairWinner, winning);
                }
            }
            turn.InPlay.Clear();

            int citizens = turn.Deck.Count;
            int kingSeat = -1, kings = 0;
            for (int seat = 0; seat < 2; seat++) foreach (Card c in turn.Hands[seat])
            {
                if (c.Kind == CardKind.Citizen) citizens++;
                if (c.Kind == CardKind.King) { kingSeat = seat; kings++; }
            }
            if (citizens == 0)
            {
                turn.Finish(kings == 1 ? 1 - kingSeat : -1,
                    kings == 1 ? MatchEndReason.SocialCollapse : MatchEndReason.InvalidKingState);
                return Commit(before, turn, played, pairWinner, coinRecipient, tax);
            }

            // Draw only here; interaction descriptions are not additional draw operations.
            while (turn.Deck.Count > 0)
            {
                bool need0 = turn.Hands[0].Count < LastKingRules.HandLimit;
                bool need1 = turn.Hands[1].Count < LastKingRules.HandLimit;
                if (!need0 && !need1) break;
                if (need0 && need1 && turn.Deck.Count == 1)
                {
                    int recipient = turn.InitiativeSeat;
                    turn.Draw(recipient); turn.InitiativeSeat = 1 - recipient;
                    turn.Add(TurnEventKind.InitiativeTransferred, turn.InitiativeSeat, null, null, 0,
                        "The final Citizen was allocated by initiative; the token changes seats.");
                }
                else
                {
                    if (need0) turn.Draw(0);
                    if (need1 && turn.Deck.Count > 0) turn.Draw(1);
                }
            }
            if (turn.Deck.Count == 0)
            {
                bool empty0 = turn.Hands[0].Count == 0, empty1 = turn.Hands[1].Count == 0;
                if (empty0 || empty1) turn.Finish(empty0 && empty1 ? -1 : empty0 ? 1 : 0, MatchEndReason.EmptyHand);
            }
            return Commit(before, turn, played, pairWinner, coinRecipient, tax);
        }

        TurnResult Commit(MatchState before, Resolution turn, Card[] played, int winner, int recipient, bool tax)
        {
            MatchState after = turn.Snapshot();
            LastKingRules.ValidateState(after);
            State = after;
            string summary = winner < 0 ? "Hai lá hòa nhau." : "Seat " + winner + " thắng lượt.";
            if (tax) summary += " Người giữ Vua chịu Thuế Máu.";
            if (after.IsFinished) summary += after.IsDraw ? " Trận đấu hòa." : " Seat " + after.WinnerSeat + " thắng trận.";
            return new TurnResult(before, after, played, winner, recipient, turn.Events, summary);
        }

        sealed class Resolution
        {
            public readonly List<Card>[] Hands;
            public readonly List<Card> Deck, Destroyed, InPlay = new List<Card>();
            public readonly int[] Coins;
            public readonly List<TurnEvent> Events = new List<TurnEvent>();
            public int InitiativeSeat, TurnIndex, WinnerSeat = -1;
            public bool Finished;
            public MatchEndReason EndReason;
            public Resolution(MatchState state)
            {
                Hands = new[] { new List<Card>(state.Players[0].Hand), new List<Card>(state.Players[1].Hand) };
                Coins = new[] { state.Players[0].BloodCoins, state.Players[1].BloodCoins };
                Deck = new List<Card>(state.Deck); Destroyed = new List<Card>(state.DestroyedCards);
                InitiativeSeat = state.InitiativeSeat; TurnIndex = state.TurnIndex + 1;
            }
            public void Add(TurnEventKind kind, int seat, Card before, Card after, int amount, string message)
            { Events.Add(new TurnEvent(kind, seat, before, after, amount, message)); }
            public void Return(int seat, Card card)
            {
                Hands[seat].Add(card);
                Add(TurnEventKind.CardReturned, seat, card, card, 0, card.Kind + " returns to its owner.");
            }
            public void Transform(int seat, Card card, CardKind kind)
            {
                var changed = new Card(card.Id, kind); Hands[seat].Add(changed);
                Add(TurnEventKind.CardTransformed, seat, card, changed, 0, card.Kind + " becomes " + kind + ".");
            }
            public void Destroy(int seat, Card card)
            {
                Destroyed.Add(card);
                Add(TurnEventKind.CardDestroyed, seat, card, null, 0, card.Kind + " is destroyed.");
            }
            public void Draw(int seat)
            {
                Card card = Deck[0]; Deck.RemoveAt(0); Hands[seat].Add(card);
                Add(TurnEventKind.CardDrawn, seat, null, card, 0, "Draw one Citizen.");
            }
            public void Finish(int winner, MatchEndReason reason)
            {
                Finished = true; WinnerSeat = winner; EndReason = reason;
                Add(TurnEventKind.MatchFinished, winner, null, null, 0, "Match finished: " + reason + ".");
            }
            public MatchState Snapshot()
            {
                return new MatchState(new[] { new PlayerState(Hands[0], Coins[0]), new PlayerState(Hands[1], Coins[1]) },
                    Deck, Destroyed, InitiativeSeat, TurnIndex, InPlay, Finished, WinnerSeat, EndReason);
            }
        }
    }

    public static class LastKingAI
    {
        // Only the immutable public state enters this method. It cannot inspect a pending player choice.
        public static int ChooseCard(MatchState state, int seat, Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            LastKingRules.ValidateState(state);
            if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException("seat");
            if (state.IsFinished) throw new InvalidOperationException("Cannot select after the match ends.");
            int count = state.Players[seat].Hand.Count;
            if (random.NextDouble() < .12) return random.Next(count);
            double best = double.NegativeInfinity;
            var choices = new List<int>();
            for (int mine = 0; mine < count; mine++)
            {
                double total = 0, worst = double.PositiveInfinity;
                int opponentCount = state.Players[1 - seat].Hand.Count;
                for (int theirs = 0; theirs < opponentCount; theirs++)
                {
                    var simulation = new LastKingMatch(state);
                    MatchState after = seat == 0 ? simulation.ResolveTurn(mine, theirs).After : simulation.ResolveTurn(theirs, mine).After;
                    double value = Evaluate(after, seat);
                    total += value; worst = Math.Min(worst, value);
                }
                // Weigh expected public-hand outcomes and the danger of the worst response.
                double score = .65 * total / opponentCount + .35 * worst;
                if (score > best + .00001) { best = score; choices.Clear(); choices.Add(mine); }
                else if (Math.Abs(score - best) < .00001) choices.Add(mine);
            }
            return choices[random.Next(choices.Count)];
        }
        static double Evaluate(MatchState state, int seat)
        {
            if (state.IsFinished) return state.IsDraw ? 0 : state.WinnerSeat == seat ? 10000 : -10000;
            int own = state.Players[seat].BloodCoins, other = state.Players[1 - seat].BloodCoins;
            return (other * 8 + other * other * 2) - (own * 8 + own * own * 2) +
                   1.25 * (state.Players[seat].Hand.Count - state.Players[1 - seat].Hand.Count);
        }
    }
}
