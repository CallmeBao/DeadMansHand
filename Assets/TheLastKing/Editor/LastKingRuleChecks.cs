using System;
using System.Collections.Generic;
using System.Text;
using Random = System.Random;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace TheLastKing
{
    public static class LastKingRuleChecks
    {
        static int assertions;

#if UNITY_EDITOR
        [MenuItem(TheLastKing.Editor.LastKingSetup.GameTitle + "/Run Rule Checks")]
        public static void RunMenu() { Debug.Log(RunAll()); }
#endif

        public static string RunAll()
        {
            assertions = 0;
            CheckInitialAndSnapshots();
            CheckAllPairings();
            CheckBloodThresholdAndPriority();
            CheckTaxAndSlaves();
            CheckLastDrawAndExhaustion();
            CheckSocialCollapseAndEmptyHands();
            CheckInvalidInput();
            CheckAIUsesPublicState();
            string simulations = CheckRandomMatches();
            return "PASS: The Last King v1.1 — " + assertions + " assertions. " + simulations +
                " All pairings, role transfers, blood tax, lethal priority, draw priority, card conservation, terminal states and AI snapshot isolation passed.";
        }

        static void Require(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("LAST KING RULE CHECK FAILED: " + message);
        }
        static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (ArgumentException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, message);
        }

        static void CheckInitialAndSnapshots()
        {
            var match = new LastKingMatch(); MatchState initial = match.State;
            Require(initial.Players.Count == 2 && initial.Players[0].Hand.Count == 3 && initial.Players[1].Hand.Count == 3, "two three-card hands");
            Require(initial.Players[0].Hand[0].Kind == CardKind.King && initial.Players[1].Hand[0].Kind == CardKind.Slave, "asymmetric opening roles");
            Require(initial.DeckCount == 12 && initial.DestroyedCount == 0 && initial.InitiativeSeat == 1, "initial public resources and priority");
            Require(initial.TurnIndex == 0 && !initial.IsFinished && !initial.IsDraw, "initial turn numbering");
            Require(LastKingRules.ActiveCitizenCount(initial) == 16, "sixteen active Citizens at setup");
            Card oldCitizen = initial.Players[1].Hand[1];
            TurnResult result = match.ResolveTurn(0, 1);
            Require(object.ReferenceEquals(result.Before, initial) && object.ReferenceEquals(result.After, match.State), "result snapshots are explicit");
            Require(initial.Players[1].BloodCoins == 0 && initial.Players[1].Hand[1].Kind == CardKind.Citizen, "resolving cannot mutate the old snapshot");
            Require(oldCitizen.Kind == CardKind.Citizen, "transformation never mutates old card objects");
            Require(Find(match.State.Players[1].Hand, oldCitizen.Id).Kind == CardKind.Slave, "the transformed card preserves its identity");
            Require(result.After.TurnIndex == 1, "one resolve advances exactly one turn");
            bool readOnly = false;
            try { ((IList<Card>)initial.Players[0].Hand).Add(new Card(50, CardKind.Citizen)); }
            catch (NotSupportedException) { readOnly = true; }
            Require(readOnly, "public hand snapshots cannot be edited by callers");
        }

        static void CheckAllPairings()
        {
            // Each fixture preserves the one-King invariant while exercising a selected pair.
            CardKind[][] a = {
                Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen),
                Kinds(CardKind.Citizen, CardKind.King, CardKind.Citizen),
                Kinds(CardKind.Citizen, CardKind.King, CardKind.Citizen),
                Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.Slave, CardKind.King, CardKind.Citizen)
            };
            CardKind[][] b = {
                Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen),
                Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen),
                Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen),
                Kinds(CardKind.Citizen, CardKind.King, CardKind.Citizen),
                Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen)
            };
            int[] expectedWinner = { 0, 1, 1, -1, 0, 0, 1, -1 };
            int[] expectedRecipient = { 1, 0, 0, 0, 1, 1, 0, -1 };
            int[] expectedDraws = { 0, 1, 0, 2, 1, 1, 1, 2 };
            for (int i = 0; i < a.Length; i++)
            {
                var match = Scenario(a[i], b[i], 12);
                TurnResult r = match.ResolveTurn(0, 0);
                string label = a[i][0] + " versus " + b[i][0];
                Require(r.PairWinnerSeat == expectedWinner[i], label + " winner");
                Require(r.CoinRecipientSeat == expectedRecipient[i], label + " Blood Coin recipient");
                Require(r.After.DeckCount == 12 - expectedDraws[i], label + " exact refill count");
                Require(r.After.Players[0].Hand.Count == 3 && r.After.Players[1].Hand.Count == 3, label + " returns and refills to three");
                Require(!r.After.IsFinished, label + " does not end a fresh match");
                Require(CountEvents(r, TurnEventKind.BloodCoinAdded) == (expectedRecipient[i] < 0 ? 0 : 1), label + " one logical coin at most");
                LastKingRules.ValidateState(r.After);

                if (r.PairWinnerSeat >= 0)
                {
                    int winner = r.PairWinnerSeat, loser = 1 - winner;
                    Card winning = r.PlayedCards[winner], losing = r.PlayedCards[loser];
                    if (winning.Kind == CardKind.King)
                    {
                        Require(Find(r.After.Players[winner].Hand, winning.Id).Kind == CardKind.King, "winning King returns");
                        Require(Find(r.After.Players[loser].Hand, losing.Id).Kind == CardKind.Slave, "defeated Citizen becomes Slave");
                        Require(r.After.DestroyedCount == 0, "King over Citizen destroys no cards");
                    }
                    else
                    {
                        Require(Find(r.After.DestroyedCards, losing.Id) != null, "the defeated King or Slave is destroyed");
                        Require(Find(r.After.Players[winner].Hand, winning.Id).Kind == (winning.Kind == CardKind.Slave ? CardKind.King : CardKind.Citizen), "winning role returns or inherits kingship");
                        Require(r.After.DestroyedCount == 1, "exactly one card destroyed in a decisive non-King win");
                    }
                }
                else Require(r.After.DestroyedCount == 2, "tied Citizens or Slaves both destroyed");
            }
            Reject(() => LastKingRules.Compare(CardKind.King, CardKind.King), "ninth pairing King/King is invalid");
            Reject(() => Scenario(Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen), Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen), 12), "two active Kings rejected before a turn");
        }

        static void CheckBloodThresholdAndPriority()
        {
            MatchOutcome below = LastKingRules.EvaluateBloodCoins(4, 4);
            Require(!below.IsFinished, "four coins is not terminal");
            Require(LastKingRules.EvaluateBloodCoins(5, 4).WinnerSeat == 1, "five coins defeats seat zero");
            Require(LastKingRules.EvaluateBloodCoins(4, 6).WinnerSeat == 0, "threshold is at least five, not exactly five");
            MatchOutcome both = LastKingRules.EvaluateBloodCoins(5, 5);
            Require(both.IsFinished && both.WinnerSeat == -1, "simultaneous lethal counts draw");

            var match = Scenario(Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen), Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen), 12, 4, 0);
            TurnResult r = match.ResolveTurn(0, 0);
            Require(r.After.IsFinished && r.After.WinnerSeat == 1 && r.After.EndReason == MatchEndReason.BloodCoins, "losing fifth coin ends match");
            Require(r.After.DeckCount == 12 && r.After.DestroyedCount == 0 && r.After.InPlayCards.Count == 2, "lethal damage stops before conversion or drawing while preserving played cards");
            Require(CountEvents(r, TurnEventKind.CardTransformed) == 0 && CountEvents(r, TurnEventKind.CardDestroyed) == 0 && CountEvents(r, TurnEventKind.CardDrawn) == 0, "terminal event order excludes later rule phases");
            Require(r.Events[r.Events.Count - 1].Kind == TurnEventKind.MatchFinished, "terminal event is final");
            Reject(() => match.ResolveTurn(0, 0), "finished match cannot resolve again");

            // Without the lethal coin, converting the last Citizen would kill the King holder instead.
            match = Scenario(Kinds(CardKind.King), Kinds(CardKind.Citizen), 0, 0, 4);
            r = match.ResolveTurn(0, 0);
            Require(r.After.WinnerSeat == 0 && r.After.EndReason == MatchEndReason.BloodCoins, "blood threshold takes priority over opposing Social Collapse outcome");
        }

        static void CheckTaxAndSlaves()
        {
            var match = Scenario(Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen), Kinds(CardKind.Citizen, CardKind.King, CardKind.Citizen), 12);
            TurnResult r = match.ResolveTurn(0, 0);
            Require(r.PairWinnerSeat == -1 && r.CoinRecipientSeat == 1 && r.After.Players[1].BloodCoins == 1, "tax follows current King ownership, not initial seat");
            match = Scenario(Kinds(CardKind.Slave, CardKind.King, CardKind.Citizen), Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen), 12);
            r = match.ResolveTurn(0, 0);
            Require(r.CoinRecipientSeat == -1 && r.After.Players[0].BloodCoins == 0, "Slave tie never taxes a retained King");
            match = Scenario(Kinds(CardKind.Citizen, CardKind.King, CardKind.Citizen), Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen), 12, 4, 0);
            r = match.ResolveTurn(0, 0);
            Require(r.After.IsFinished && r.After.EndReason == MatchEndReason.BloodCoins && r.After.DestroyedCount == 0, "fifth tax coin stops before tied-card destruction");
        }

        static void CheckLastDrawAndExhaustion()
        {
            for (int priority = 0; priority < 2; priority++)
            {
                var match = Scenario(Kinds(CardKind.Citizen, CardKind.King, CardKind.Citizen), Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen), 1, 0, 0, priority);
                TurnResult r = match.ResolveTurn(0, 0);
                Require(r.After.Players[priority].Hand.Count == 3 && r.After.Players[1 - priority].Hand.Count == 2, "initiative assigns the disputed final Citizen");
                Require(r.After.InitiativeSeat == 1 - priority && CountEvents(r, TurnEventKind.InitiativeTransferred) == 1, "initiative transfers once when used");
                Require(r.After.DeckCount == 0 && !r.After.IsFinished, "empty common deck alone does not end a game");
            }
            var single = Scenario(Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen), Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen), 1, 0, 0, 1);
            TurnResult assassination = single.ResolveTurn(0, 0);
            Require(assassination.After.Players[0].Hand.Count == 3 && assassination.After.InitiativeSeat == 1, "sole draw recipient gets last card without spending initiative");
            var two = Scenario(Kinds(CardKind.Citizen, CardKind.King, CardKind.Citizen), Kinds(CardKind.Citizen, CardKind.Slave, CardKind.Citizen), 2);
            TurnResult even = two.ResolveTurn(0, 0);
            Require(even.After.Players[0].Hand.Count == 3 && even.After.Players[1].Hand.Count == 3 && even.After.InitiativeSeat == 1, "two available Citizens fill both hands without token transfer");
        }

        static void CheckSocialCollapseAndEmptyHands()
        {
            var match = Scenario(Kinds(CardKind.King), Kinds(CardKind.Citizen), 0);
            TurnResult r = match.ResolveTurn(0, 0);
            Require(r.PairWinnerSeat == 0 && r.After.WinnerSeat == 1 && r.After.EndReason == MatchEndReason.SocialCollapse, "winning the last Citizen costs the King holder the match");
            Require(LastKingRules.ActiveCitizenCount(r.After) == 0, "conversion counts toward Social Collapse");

            match = Scenario(Kinds(CardKind.King), Kinds(CardKind.Slave, CardKind.Citizen), 0);
            r = match.ResolveTurn(0, 0);
            Require(r.After.Players[0].Hand.Count == 0 && r.After.WinnerSeat == 1 && r.After.EndReason == MatchEndReason.EmptyHand, "cannot continue with an empty hand and no draw");

            match = Scenario(Kinds(CardKind.King, CardKind.Citizen), Kinds(CardKind.Citizen), 0);
            r = match.ResolveTurn(1, 0);
            Require(r.After.Players[1].Hand.Count == 0 && r.After.WinnerSeat == 1 && r.After.EndReason == MatchEndReason.SocialCollapse, "Social Collapse takes precedence even when the other seat has no cards");
        }

        static void CheckInvalidInput()
        {
            var match = new LastKingMatch(); MatchState before = match.State;
            Reject(() => match.ResolveTurn(-1, 0), "negative selection rejected");
            Reject(() => match.ResolveTurn(0, 3), "out-of-range selection rejected");
            Require(object.ReferenceEquals(match.State, before) && match.State.TurnIndex == 0, "invalid input changes no state");
            Reject(() => LastKingRules.Compare((CardKind)99, CardKind.Citizen), "unknown role rejected");
            var duplicated = new MatchState(before.Players, before.Deck, new[] { before.Deck[0] });
            Reject(() => new LastKingMatch(duplicated), "duplicate card identity rejected");
            var badDeck = new List<Card>(before.Deck); badDeck[0] = new Card(badDeck[0].Id, CardKind.Slave);
            Reject(() => new LastKingMatch(new MatchState(before.Players, badDeck, before.DestroyedCards)), "non-Citizen common deck rejected");
            Reject(() => Scenario(Kinds(CardKind.King, CardKind.Citizen, CardKind.Citizen), Kinds(CardKind.Slave, CardKind.Citizen, CardKind.Citizen), 12, 5, 0), "unresolved lethal state cannot begin another turn");
        }

        static void CheckAIUsesPublicState()
        {
            var match = new LastKingMatch(); MatchState before = match.State;
            var rng = new Random(348);
            for (int i = 0; i < 50; i++)
            {
                int seat = i % 2;
                int choice = LastKingAI.ChooseCard(before, seat, rng);
                Require(choice >= 0 && choice < before.Players[seat].Hand.Count, "AI selects a currently owned card");
            }
            Require(before.TurnIndex == 0 && before.DeckCount == 12 && before.DestroyedCount == 0 && before.Players[0].BloodCoins == 0 && before.Players[1].BloodCoins == 0, "AI lookahead never mutates public match state");
            Require(object.ReferenceEquals(match.State, before), "AI cannot replace caller state");
        }

        static string CheckRandomMatches()
        {
            int totalTurns = 0, longest = 0, shortest = int.MaxValue;
            int bloodEnds = 0, collapseEnds = 0, emptyEnds = 0;
            for (int seed = 0; seed < 1500; seed++)
            {
                var match = new LastKingMatch(); var random = new Random(seed * 17 + 31);
                while (!match.State.IsFinished && match.State.TurnIndex < 100)
                {
                    MatchState before = match.State;
                    int a = random.Next(before.Players[0].Hand.Count), b = random.Next(before.Players[1].Hand.Count);
                    TurnResult r = match.ResolveTurn(a, b);
                    LastKingRules.ValidateState(r.After);
                    int coins = r.After.Players[0].BloodCoins + r.After.Players[1].BloodCoins;
                    int oldCoins = before.Players[0].BloodCoins + before.Players[1].BloodCoins;
                    Require(coins - oldCoins == (r.CoinRecipientSeat < 0 ? 0 : 1), "one coin per resolved penalty; no visual placement can add a second rule coin");
                    Require(r.After.DeckCount <= before.DeckCount && r.After.DestroyedCount >= before.DestroyedCount, "resources never regenerate");
                    Require(r.After.TurnIndex == before.TurnIndex + 1, "turn progress is monotonic");
                }
                Require(match.State.IsFinished, "random legal game terminates within guard limit");
                int turns = match.State.TurnIndex; totalTurns += turns; longest = Math.Max(longest, turns); shortest = Math.Min(shortest, turns);
                if (match.State.EndReason == MatchEndReason.BloodCoins) bloodEnds++;
                if (match.State.EndReason == MatchEndReason.SocialCollapse) collapseEnds++;
                if (match.State.EndReason == MatchEndReason.EmptyHand) emptyEnds++;
            }
            return "1500 deterministic random-bot games: " + shortest + "–" + longest + " turns, mean " + (totalTurns / 1500.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "; endings blood=" + bloodEnds + ", collapse=" + collapseEnds + ", empty=" + emptyEnds + ". This checks consistency, not strategic balance.";
        }

        static CardKind[] Kinds(params CardKind[] kinds) { return kinds; }
        static LastKingMatch Scenario(CardKind[] first, CardKind[] second, int deckCount, int blood0 = 0, int blood1 = 0, int priority = 1)
        {
            int id = 1;
            var hand0 = new List<Card>(); foreach (CardKind k in first) hand0.Add(new Card(id++, k));
            var hand1 = new List<Card>(); foreach (CardKind k in second) hand1.Add(new Card(id++, k));
            var deck = new List<Card>(); for (int i = 0; i < deckCount; i++) deck.Add(new Card(id++, CardKind.Citizen));
            var destroyed = new List<Card>(); while (id <= LastKingRules.TotalCards) destroyed.Add(new Card(id++, CardKind.Citizen));
            return new LastKingMatch(new MatchState(new[] { new PlayerState(hand0, blood0), new PlayerState(hand1, blood1) }, deck, destroyed, priority));
        }
        static Card Find(IReadOnlyList<Card> cards, int id)
        { foreach (Card card in cards) if (card.Id == id) return card; return null; }
        static int CountEvents(TurnResult result, TurnEventKind kind)
        { int count = 0; foreach (TurnEvent e in result.Events) if (e.Kind == kind) count++; return count; }

#if LAST_KING_STANDALONE_TESTS
        public static int Main()
        {
            try { Console.WriteLine(RunAll()); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
#endif
    }
}
