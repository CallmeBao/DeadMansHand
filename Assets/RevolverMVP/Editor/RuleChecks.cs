using System;
using System.Collections.Generic;

namespace RevolverMVP
{
    public static class RuleChecks
    {
        public static void Run()
        {
            int[,] expected = { { 0, 1, -1 }, { -1, 0, 1 }, { 1, -1, 0 } };
            for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
                Require(CardRules.Compare((CardKind)a, (CardKind)b) == expected[a, b], "All nine card pairings");
            var first = CardRules.NewHand();
            var second = CardRules.NewHand();
            Require(first.Count == 5 && first.FindAll(c => c == CardKind.Citizen).Count == 3, "Deck composition");
            first.RemoveAt(0);
            Require(second.Count == 5, "Hands must not share storage");
            Require(!CardRules.IsEarlyDraw(second, second, 0), "Mixed hand is not an early draw");
            var citizens = new List<CardKind> { CardKind.Citizen, CardKind.Citizen };
            var slaves = new List<CardKind> { CardKind.Slave, CardKind.Slave };
            Require(CardRules.IsEarlyDraw(citizens, citizens, 3), "Shared uniform kind draws early");
            Require(!CardRules.IsEarlyDraw(citizens, slaves, 3), "Different uniform kinds must play");
            Require(CardRules.IsEarlyDraw(new List<CardKind> { CardKind.King }, new List<CardKind> { CardKind.King }, 4), "Fourth draw stops selection");

            // Enumerate every legal order of draws. After four matching reveals,
            // the only remaining pair must match too, since both decks are equal.
            VerifyDrawSequences(CardRules.NewHand(), CardRules.NewHand(), 0);
            var player = new PlayerState();
            player.Chambers[2] = true; player.Chambers[5] = true;
            player.Survive();
            Require(player.BulletCount == 2 && Math.Abs(player.Fear - .15f) < .0001f, "Survival retains bullets and raises fear");
            for (int i = 0; i < 20; i++) player.Survive();
            Require(player.Fear == 1f, "Fear clamp");
            player.Reset();
            Require(player.BulletCount == 0 && player.Fear == 0f, "New match resets persistence");
            var rng = new Random(37);
            var history = new List<CardKind>();
            var aiHand = CardRules.NewHand();
            for (int i = 0; i < 5; i++)
            {
                int selected = CardAI.Choose(aiHand, history, rng);
                Require(selected >= 0 && selected < aiHand.Count, "AI returns a live card index");
                history.Add(aiHand[selected]); aiHand.RemoveAt(selected);
            }
        }

        static void VerifyDrawSequences(List<CardKind> first, List<CardKind> second, int turns)
        {
            if (CardRules.IsEarlyDraw(first, second, turns))
            {
                foreach (CardKind a in first) foreach (CardKind b in second)
                    Require(a == b, "Early draw cannot hide a decisive pair");
                return;
            }
            for (int a = 0; a < first.Count; a++)
            for (int b = 0; b < second.Count; b++)
            {
                if (first[a] != second[b]) continue;
                var nextFirst = new List<CardKind>(first); nextFirst.RemoveAt(a);
                var nextSecond = new List<CardKind>(second); nextSecond.RemoveAt(b);
                VerifyDrawSequences(nextFirst, nextSecond, turns + 1);
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Rule check failed: " + message);
        }
    }
}
