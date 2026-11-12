using System;

namespace RevolverMVP
{
    [Serializable]
    public sealed class SpinInput
    {
        public string OwnerId;
        public int TurnId;
        public long RequestId;
        public int SlotIndex;
        public double FlickImpulse;
        // Reserved for future seeded cosmetic noise; the physical integrator
        // currently uses no random numbers, so seed cannot change the outcome.
        public int SpinSeed;
    }

    [Serializable]
    public sealed class RevolverResult
    {
        public readonly string OwnerId;
        public readonly int TurnId;
        public readonly long RequestId;
        public readonly int SpinSeed;
        public readonly int ChamberMask;
        public readonly int FinalSlotAt12h;
        public readonly double FinalAngle;
        public readonly double SimulationSeconds;
        public readonly bool IsFired;
        public readonly bool UsedSettleFallback;

        internal RevolverResult(SpinInput input, int mask, CylinderDynamics cylinder)
        {
            OwnerId = input.OwnerId; TurnId = input.TurnId; RequestId = input.RequestId;
            SpinSeed = input.SpinSeed; ChamberMask = mask;
            FinalSlotAt12h = cylinder.FinalSlot; FinalAngle = cylinder.Angle;
            SimulationSeconds = cylinder.Elapsed;
            IsFired = (mask & (1 << FinalSlotAt12h)) != 0;
            UsedSettleFallback = cylinder.UsedSettleFallback;
        }
    }

    /// <summary>
    /// Transport-independent host adapter, not a LAN implementation. A server
    /// calls BeginPenalty from trusted match state, then Resolve with the
    /// connection's authenticated sender ID (never an ID supplied by the client).
    /// Broadcast the accepted result; clients animate it rather than adjudicate.
    /// </summary>
    public sealed class HostRevolverAuthority
    {
        string ownerId;
        int turnId = -1;
        bool[] hostChambers;
        double angle;
        RevolverResult accepted;
        int acceptedSlot;
        double acceptedImpulse;
        int acceptedSeed;

        public void BeginPenalty(string owner, int turn, bool[] chambers, double initialAngle = 0)
        {
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("Owner is required.");
            if (turn < 0 || turn <= turnId) throw new ArgumentException("Penalty IDs must increase.");
            if (chambers == null || chambers.Length != 6) throw new ArgumentException("Six host slots are required.");
            if (!Finite(initialAngle)) throw new ArgumentException("Initial angle must be finite.");
            int count = 0;
            foreach (bool loaded in chambers) if (loaded) count++;
            if (count >= 6) throw new InvalidOperationException("A full cylinder cannot start another penalty.");
            ownerId = owner; turnId = turn; angle = initialAngle;
            hostChambers = (bool[])chambers.Clone(); accepted = null;
        }

        public RevolverResult Resolve(string authenticatedSender, SpinInput input)
        {
            if (hostChambers == null) throw new InvalidOperationException("No active host penalty.");
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (authenticatedSender != ownerId || input.OwnerId != ownerId)
                throw new InvalidOperationException("Only the penalized player can spin.");
            if (input.TurnId != turnId) throw new InvalidOperationException("Stale or future penalty input.");
            if (input.RequestId < 0) throw new ArgumentException("Request ID must be nonnegative.");
            if (input.SlotIndex < 0 || input.SlotIndex >= 6) throw new ArgumentOutOfRangeException(nameof(input.SlotIndex));
            if (!Finite(input.FlickImpulse) || Math.Abs(input.FlickImpulse) > 80)
                throw new ArgumentException("Flick impulse must be finite and between -80 and 80.");
            if (accepted != null)
            {
                // Exact retransmissions return the original verdict. Altered or
                // second requests cannot reroll, add another bullet or spin twice.
                if (input.RequestId == accepted.RequestId && input.SlotIndex == acceptedSlot &&
                    input.FlickImpulse == acceptedImpulse && input.SpinSeed == acceptedSeed)
                    return accepted;
                throw new InvalidOperationException("Penalty has already been resolved.");
            }
            if (hostChambers[input.SlotIndex]) throw new InvalidOperationException("Cannot load an occupied slot.");
            var loadedChambers = (bool[])hostChambers.Clone();
            loadedChambers[input.SlotIndex] = true;
            var cylinder = new CylinderDynamics(loadedChambers, angle);
            cylinder.Start(input.FlickImpulse);
            for (int step = 0; !cylinder.Settled && step < 2400; step++) cylinder.Step(1.0 / 120.0);
            if (!cylinder.Settled) throw new InvalidOperationException("Host simulation exceeded its safety limit.");
            int mask = 0;
            for (int i = 0; i < 6; i++) if (loadedChambers[i]) mask |= 1 << i;
            acceptedSlot = input.SlotIndex; acceptedImpulse = input.FlickImpulse; acceptedSeed = input.SpinSeed;
            accepted = new RevolverResult(input, mask, cylinder);
            return accepted;
        }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
