using System;

namespace RevolverMVP
{
    /// <summary>
    /// Host-owned rotational model; presentation only reads the resulting trajectory.
    /// Angle is an offset: at zero, slot 0 points UP. This makes the six detent
    /// minima line up with the firing pin (the source specification mixes origins).
    /// Fixed steps are repeatable on one host/runtime, not a cross-platform lockstep promise.
    /// </summary>
    public sealed class CylinderDynamics
    {
        public const double Radius = 1.25;
        public const double BulletMass = 1.0;
        public const double BaseInertia = 1.4;
        public const double MaxImpulse = 80.0;
        public const double FixedStep = 1.0 / 120.0;
        public const double MaxDuration = 10.0;
        const double SlotSpacing = Math.PI / 3.0;
        const double Gravity = 9.81;
        const double AxleFriction = 2.4;
        const double DetentStrength = 9.0;
        readonly bool[] chambers;
        double accumulator, restingTime;
        int steps;
        bool started;

        public double Angle { get; private set; }
        public double AngularVelocity { get; private set; }
        public bool Settled { get; private set; }
        public int FinalSlot { get; private set; } = -1;
        public double Elapsed { get { return steps * FixedStep; } }
        public double Inertia { get; private set; }
        public bool UsedSettleFallback { get; private set; }

        public CylinderDynamics(bool[] loadedChambers, double initialAngle = 0)
        {
            if (loadedChambers == null || loadedChambers.Length != 6)
                throw new ArgumentException("A cylinder requires exactly six chambers.", nameof(loadedChambers));
            RequireFinite(initialAngle, nameof(initialAngle));
            chambers = (bool[])loadedChambers.Clone();
            Angle = initialAngle % (2 * Math.PI);
            Inertia = BaseInertia;
            foreach (bool loaded in chambers)
                if (loaded) Inertia += BulletMass * Radius * Radius;
        }

        public bool IsLoaded(int slot)
        {
            if (slot < 0 || slot >= 6) throw new ArgumentOutOfRangeException(nameof(slot));
            return chambers[slot];
        }

        public static double SlotAngle(double rotation, int slot)
        {
            return Math.PI / 2 + rotation + slot * SlotSpacing;
        }

        public double GravityTorque(double rotation)
        {
            double torque = 0;
            for (int i = 0; i < 6; i++)
                if (chambers[i]) torque -= BulletMass * Gravity * Radius * Math.Cos(SlotAngle(rotation, i));
            return torque;
        }

        public void Start(double angularImpulse)
        {
            RequireFinite(angularImpulse, nameof(angularImpulse));
            if (started) throw new InvalidOperationException("A cylinder spin can only be started once.");
            started = true;
            AngularVelocity = Math.Max(-MaxImpulse, Math.Min(MaxImpulse, angularImpulse)) / Inertia;
        }

        public void Step(double dt)
        {
            RequireFinite(dt, nameof(dt));
            if (dt < 0 || dt > 60) throw new ArgumentOutOfRangeException(nameof(dt));
            if (!started || Settled) return;
            accumulator += dt;
            while (accumulator + 1e-12 >= FixedStep && !Settled)
            {
                accumulator -= FixedStep;
                Integrate();
            }
        }

        void Integrate()
        {
            // A progressively engaging mechanical brake bounds the interaction time.
            // At ten seconds the stop catches the nearest chamber if natural settling
            // has not completed. UsedSettleFallback makes this design choice observable.
            double brake = Elapsed > 6 ? (Elapsed - 6) * 5.0 : 0;
            double torque = GravityTorque(Angle) - (AxleFriction + brake) * AngularVelocity
                - DetentStrength * Math.Sin(6 * Angle);
            double acceleration = torque / Inertia;
            AngularVelocity += acceleration * FixedStep;
            Angle += AngularVelocity * FixedStep;
            steps++;
            bool slow = Math.Abs(AngularVelocity) < .10 && Math.Abs(acceleration) < .65;
            restingTime = slow ? restingTime + FixedStep : 0;
            if ((Elapsed > .65 && restingTime >= .3) || Elapsed >= MaxDuration)
            {
                UsedSettleFallback = restingTime < .3;
                long detent = (long)Math.Round(Angle / SlotSpacing, MidpointRounding.AwayFromZero);
                Angle = detent * SlotSpacing;
                FinalSlot = (int)(((-detent % 6) + 6) % 6);
                AngularVelocity = 0;
                Settled = true;
            }
        }

        static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name);
        }
    }
}
