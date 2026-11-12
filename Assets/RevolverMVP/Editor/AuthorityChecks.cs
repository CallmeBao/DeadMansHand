using System;

namespace RevolverMVP
{
    public static class AuthorityChecks
    {
        public static void Run()
        {
            var authority = new HostRevolverAuthority();
            bool[] slots = { true, false, false, false, false, false };
            authority.BeginPenalty("player", 1, slots);
            var input = new SpinInput { OwnerId = "player", TurnId = 1, RequestId = 12, SlotIndex = 1, FlickImpulse = 22, SpinSeed = 42 };
            Reject(() => authority.Resolve("attacker", input), "Sender authentication");
            input.TurnId = 0; Reject(() => authority.Resolve("player", input), "Old turn"); input.TurnId = 1;
            input.SlotIndex = 0; Reject(() => authority.Resolve("player", input), "Occupied slot"); input.SlotIndex = 1;
            input.SlotIndex = 6; Reject(() => authority.Resolve("player", input), "Invalid slot"); input.SlotIndex = 1;
            input.FlickImpulse = double.NaN; Reject(() => authority.Resolve("player", input), "NaN impulse");
            input.FlickImpulse = double.PositiveInfinity; Reject(() => authority.Resolve("player", input), "Infinite impulse");
            input.FlickImpulse = 81; Reject(() => authority.Resolve("player", input), "Oversized impulse"); input.FlickImpulse = 22;
            // Mutating the source after BeginPenalty cannot alter the host snapshot.
            slots[5] = true;
            RevolverResult result = authority.Resolve("player", input);
            Require(result.ChamberMask == 3, "Host-owned snapshot and one new bullet");
            Require(result.FinalSlotAt12h >= 0 && result.FinalSlotAt12h < 6, "Final slot bounds");
            Require(result.IsFired == ((result.ChamberMask & (1 << result.FinalSlotAt12h)) != 0), "Verdict matches final chamber");
            Require(object.ReferenceEquals(result, authority.Resolve("player", input)), "Idempotent retransmission");
            input.FlickImpulse = 23; Reject(() => authority.Resolve("player", input), "Altered duplicate"); input.FlickImpulse = 22;
            input.RequestId = 13; Reject(() => authority.Resolve("player", input), "Second spin");
            Reject(() => authority.BeginPenalty("player", 1, new bool[6]), "Monotonic turn IDs");
            authority.BeginPenalty("player", 2, new bool[6]);
            Reject(() => authority.Resolve("player", input), "Replay after next penalty");

            // Six bullets are allowed during the last penalty and must fire;
            // only the state before the next loading is limited to five.
            var full = new HostRevolverAuthority();
            full.BeginPenalty("ai", 1, new[] { true, true, true, true, true, false });
            var last = full.Resolve("ai", new SpinInput { OwnerId = "ai", TurnId = 1, RequestId = 0, SlotIndex = 5, FlickImpulse = -60 });
            Require(last.ChamberMask == 63 && last.IsFired, "Sixth bullet is a guaranteed ending");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Authority check failed: " + message);
        }

        static void Reject(Action operation, string message)
        {
            bool rejected = false;
            try { operation(); }
            catch (ArgumentException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, message);
        }
    }
}
