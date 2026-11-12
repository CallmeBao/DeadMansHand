using System;
using UnityEngine;

namespace RevolverMVP
{
    public static class PhysicsChecks
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Physics check failed: " + message);
        }

        public static void Run()
        {
            var opposite = new CylinderDynamics(new[] { true, false, false, true, false, false });
            for (int i = 0; i < 60; i++) Check(Math.Abs(opposite.GravityTorque(i * .1)) < 1e-10, "opposite weights cancel");
            var source = new[] { true, false, false, false, false, false };
            var one = new CylinderDynamics(source);
            source[1] = true;
            Check(!one.IsLoaded(1), "defensive chamber copy");
            Check(Math.Abs(one.Inertia - (CylinderDynamics.BaseInertia + 1.5625)) < 1e-10, "bullet inertia");
            Check(one.GravityTorque(-Math.PI / 2) < 0, "right-hand weight falls clockwise");
            var heavy = new CylinderDynamics(new[] { true, true, true, true, true, true });
            one.Start(30); heavy.Start(30);
            Check(heavy.AngularVelocity < one.AngularVelocity, "weight reduces initial angular speed");

            // Every mask, both flick directions, zero input, and all six starting detents.
            for (int mask = 0; mask < 64; mask++)
            for (int start = 0; start < 6; start++)
            foreach (double impulse in new[] { -80.0, 0.0, 18.0, 80.0 })
            {
                var chambers = new bool[6];
                for (int i = 0; i < 6; i++) chambers[i] = (mask & (1 << i)) != 0;
                var spin = new CylinderDynamics(chambers, start * Math.PI / 3);
                spin.Start(impulse);
                for (int i = 0; i < 1201 && !spin.Settled; i++) spin.Step(CylinderDynamics.FixedStep);
                Check(spin.Settled && spin.FinalSlot >= 0 && spin.FinalSlot < 6, "bounded settlement");
                Check(Math.Abs(Math.Cos(CylinderDynamics.SlotAngle(spin.Angle, spin.FinalSlot))) < 1e-8,
                    "firing slot aligned vertically");
                Check(Math.Sin(CylinderDynamics.SlotAngle(spin.Angle, spin.FinalSlot)) > .999999,
                    "firing slot at twelve, not six");
            }
            var a = new CylinderDynamics(new[] { true, true, false, false, false, false }, .4);
            var b = new CylinderDynamics(new[] { true, true, false, false, false, false }, .4);
            a.Start(42); b.Start(42);
            for (int i = 0; i < 600; i++) a.Step(1.0 / 60);
            for (int i = 0; i < 300; i++) b.Step(1.0 / 30);
            Check(a.FinalSlot == b.FinalSlot && a.Angle == b.Angle, "frame chunk independence");
            bool rejected = false;
            try { new CylinderDynamics(new bool[6]).Start(double.NaN); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "reject non-finite input");
            Debug.Log("REVOLVER MVP: cylinder checks passed (1536 trajectories, inertia, gravity and timing).");
        }
    }
}
