using System;
using System.Collections.Generic;

namespace RevolverMVP
{
    public enum CardKind { King, Citizen, Slave }

    public static class CardRules
    {
        public static List<CardKind> NewHand()
        {
            return new List<CardKind> { CardKind.King, CardKind.Citizen, CardKind.Citizen, CardKind.Citizen, CardKind.Slave };
        }

        public static int Compare(CardKind first, CardKind second)
        {
            Validate(first); Validate(second);
            if (first == second) return 0;
            return first == CardKind.King && second == CardKind.Citizen ||
                   first == CardKind.Citizen && second == CardKind.Slave ||
                   first == CardKind.Slave && second == CardKind.King ? 1 : -1;
        }

        // Call only after an equal reveal. Both remaining hands must have one
        // shared kind for an early draw; two different homogeneous hands are not a draw.
        public static bool IsEarlyDraw(IList<CardKind> first, IList<CardKind> second, int completedTurns)
        {
            if (first == null || second == null) throw new ArgumentNullException("Hands");
            if (first.Count != second.Count) throw new ArgumentException("Hand lengths must match.");
            if (completedTurns < 0 || completedTurns > 4) throw new ArgumentOutOfRangeException(nameof(completedTurns));
            if (completedTurns >= 4 || first.Count == 0) return true;
            CardKind kind = first[0];
            Validate(kind);
            for (int i = 0; i < first.Count; i++)
            {
                Validate(first[i]); Validate(second[i]);
                if (first[i] != kind || second[i] != kind) return false;
            }
            return true;
        }

        internal static void Validate(CardKind kind)
        {
            if (kind < CardKind.King || kind > CardKind.Slave)
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public sealed class PlayerState
    {
        // Logical slot IDs survive dealing and are independent of the visual angle.
        public readonly bool[] Chambers = new bool[6];
        public float Fear { get; private set; }
        public int BulletCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Chambers.Length; i++) if (Chambers[i]) count++;
                return count;
            }
        }
        public void Survive() { Fear = Math.Min(1f, Fear + .15f); }
        public void Reset() { Array.Clear(Chambers, 0, Chambers.Length); Fear = 0f; }
    }

    public static class CardAI
    {
        // Uses only its own hand and public opponent history. The controller must
        // call once at turn start, before enabling the player's selection buttons.
        public static int Choose(IList<CardKind> own, IList<CardKind> publicPlayerHistory, Random rng)
        {
            if (own == null || publicPlayerHistory == null || rng == null)
                throw new ArgumentNullException("AI inputs");
            if (own.Count == 0) throw new ArgumentException("AI needs a remaining card.");
            int[] unseen = { 1, 3, 1 };
            foreach (CardKind seen in publicPlayerHistory)
            {
                CardRules.Validate(seen);
                if (--unseen[(int)seen] < 0) throw new ArgumentException("Impossible public history.");
            }
            foreach (CardKind card in own) CardRules.Validate(card);
            // Exploration keeps repeated deals from becoming a fixed puzzle.
            if (rng.NextDouble() < .25) return rng.Next(own.Count);
            int best = int.MinValue;
            var candidates = new List<int>();
            for (int i = 0; i < own.Count; i++)
            {
                int score = 0;
                for (int kind = 0; kind < 3; kind++)
                    score += unseen[kind] * CardRules.Compare(own[i], (CardKind)kind);
                if (score > best) { best = score; candidates.Clear(); }
                if (score == best) candidates.Add(i);
            }
            return candidates[rng.Next(candidates.Count)];
        }
    }
}
