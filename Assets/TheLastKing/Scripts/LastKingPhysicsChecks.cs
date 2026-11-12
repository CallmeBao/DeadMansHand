using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheLastKing
{
    /// <summary>
    /// Play-mode checks for the serialized scale assembly. Invoke from a smoke
    /// coroutine before starting a match: yield return LastKingPhysicsChecks.Run(rig).
    /// No fake physics simulation or injected OnDeposited event is used.
    /// </summary>
    public static class LastKingPhysicsChecks
    {
        public static IEnumerator Run(LastKingPhysics rig, Action<string> note = null)
        {
            Require(rig && rig.Beam && rig.Pans != null && rig.Pans.Length == 2 && rig.Pans[0] && rig.Pans[1], "serialized beam and both pans exist");
            Require(rig.PanSensors != null && rig.PanSensors.Length == 2 && rig.PanSensors[0] && rig.PanSensors[1], "both pan sensors exist");
            Require(rig.DepositPoints != null && rig.DepositPoints.Length == 2 && rig.DepositPoints[0] && rig.DepositPoints[1], "both deposit points exist");
            Require(rig.Beam.GetComponent<HingeJoint2D>() && rig.Pans[0].GetComponent<HingeJoint2D>() && rig.Pans[1].GetComponent<HingeJoint2D>(), "beam and pans use actual hinge joints");

            bool previousMouse = rig.EnableMouseInput;
            int previousHuman = rig.HumanSeat;
            float previousTimeScale = Time.timeScale;
            int events = 0, lastOwner = -1;
            Action<int> listener = owner => { events++; lastOwner = owner; };
            rig.OnDeposited += listener;
            Time.timeScale = 1;
            rig.EnableMouseInput = false;
            rig.HumanSeat = 0;
            rig.ResetCoins();

            var bodies = new List<Rigidbody2D> { rig.Beam, rig.Pans[0], rig.Pans[1] };
            var initialPositions = new Vector2[bodies.Count];
            var initialAngles = new float[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                initialPositions[i] = bodies[i].position;
                initialAngles[i] = bodies[i].rotation;
            }

            try
            {
                Require(!rig.IsBusy && rig.SpawnedCoinCount == 0, "reset begins empty");
                Require(!rig.StartCoin(-1, 1) && !rig.StartCoin(2, 1) && !rig.StartCoin(0, 0) && !rig.StartCoin(0, 6), "invalid owner and ordinal rejected");
                Require(rig.StartCoin(0, 1), "first coin accepted");
                Rigidbody2D first = rig.GetCoinBody(0, 1);
                Require(first && first.bodyType == RigidbodyType2D.Dynamic && first.simulated, "coin uses dynamic physics");
                Require(Mathf.Abs(first.mass - rig.CoinMasses[0]) < .00001f, "Inspector mass applied without changing ordinal");
                Require(!rig.StartCoin(0, 1) && !rig.StartCoin(1, 1) && rig.SpawnedCoinCount == 1, "duplicate and simultaneous awards rejected");

                float beforePause = rig.SecondsRemaining;
                Time.timeScale = 0;
                yield return new WaitForSecondsRealtime(.15f);
                Require(Mathf.Abs(rig.SecondsRemaining - beforePause) < .001f, "pause freezes placement timeout");
                Time.timeScale = 1;

                Require(!rig.TryGrabCoin(first.position + Vector2.up * 2), "far pointer cannot grab a coin");
                Require(rig.TryGrabCoin(first.position) && rig.IsHoldingCoin, "pointer on the coin starts a grab");
                Vector2 beforeDrag = first.position;
                Vector2 dragTarget = beforeDrag + new Vector2(.1f, .5f);
                rig.MoveHeldCoin(dragTarget);
                Require(Vector2.Distance(first.position, beforeDrag) < .001f, "drag target does not teleport the body");
                float dragDeadline = Time.realtimeSinceStartup + 2;
                while (Vector2.Distance(first.position, dragTarget) > .06f && Time.realtimeSinceStartup < dragDeadline) yield return null;
                Require(Vector2.Distance(first.position, dragTarget) <= .06f, "spring force carries the grabbed body to its target");
                rig.ReleaseCoin();
                Require(!rig.IsHoldingCoin && first.gravityScale > 0, "releasing the coin restores gravity");

                // A released coin can physically sit in the opponent pan, but
                // that must never discharge its owner's placement obligation.
                PlaceAtRest(first, rig.Pans[1], rig.PanRestHeight);
                yield return new WaitForSeconds(.65f);
                Require(events == 0 && rig.IsBusy && rig.DepositedCount(0) == 0, "wrong pan does not complete a coin");

                SetTestPose(first, new Vector2(rig.PickupPoint.position.x, rig.RecoveryY - 1), 0);
                first.linearVelocity = Vector2.down * 3;
                Require(first.position.y < rig.RecoveryY, "fall test begins below recovery boundary: " + State(rig, first, events));
                float recoveryDeadline = Time.realtimeSinceStartup + .75f;
                do { yield return null; } while (first.position.y <= rig.RecoveryY && Time.realtimeSinceStartup < recoveryDeadline);
                Require(first && rig.GetCoinBody(0, 1) == first && rig.SpawnedCoinCount == 1, "fallen unplaced coin is reused, not recreated");
                Require(first.position.y > rig.RecoveryY && events == 0, "fall recovery returns coin without emitting deposit: " + State(rig, first, events));

                // This path deliberately does NOT call AutoPlace. It therefore
                // cannot use the bounded auto-seating fallback to pass the test.
                SetTestPose(first, rig.DepositPoints[0].position, 0);
                first.linearVelocity = Vector2.zero;
                first.angularVelocity = 0;
                first.gravityScale = 1;
                first.WakeUp();
                float dropHeight = rig.Pans[0].transform.InverseTransformPoint(first.position).y;
                float beamBeforeDrop = rig.Beam.rotation;
                float deadline = Time.realtimeSinceStartup + 5;
                while (events == 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Require(events == 1 && lastOwner == 0 && !rig.IsBusy, "unassisted gravity drop physically settles and completes once");
                Require(rig.PanSensors[0].OverlapPoint(first.position), "settled coin is in its own pan");
                Require(rig.Pans[0].transform.InverseTransformPoint(first.position).y < dropHeight - .05f, "coin fell before settling");
                Require(rig.DepositedCount(0) == 1 && rig.DepositedCount(1) == 0, "only the awarded owner's presentation count changes");
                yield return new WaitForSeconds(.6f);
                Require(Mathf.Abs(Mathf.DeltaAngle(beamBeforeDrop, rig.Beam.rotation)) > .05f, "physical coin load moves the hinged beam");
                Require(!rig.StartCoin(0, 1), "already deposited award cannot be replayed");
                yield return new WaitForSeconds(.2f);
                Require(events == 1, "stable trigger overlap does not emit repeatedly");
                note?.Invoke("PASS physics: mass, spring drag, wrong pan, pause, fall recovery, unassisted drop, beam load and exactly-once deposit.");

                SetTestPose(first, new Vector2(rig.PickupPoint.position.x, rig.RecoveryY - 1), 0);
                first.linearVelocity = Vector2.down;
                float depositedRecoveryDeadline = Time.realtimeSinceStartup + .75f;
                do { yield return null; } while (first.position.y <= rig.RecoveryY && Time.realtimeSinceStartup < depositedRecoveryDeadline);
                Require(rig.DepositedCount(0) == 1 && events == 1 && rig.GetCoinBody(0, 1) == first, "spilling a deposited coin does not erase or duplicate it");
                Require(rig.PanSensors[0].OverlapPoint(first.position), "deposited coin is recovered to its own pan");

                rig.ResetCoins();
                VerifyReset(rig, bodies, initialPositions, initialAngles);
                events = 0;

                // Maximum population on BOTH sides matters: it catches coin
                // spacing, pan wall, mass and recovery problems missed by a
                // one-coin happy-path test.
                for (int owner = 0; owner < 2; owner++)
                {
                    for (int ordinal = 1; ordinal <= 5; ordinal++)
                    {
                        int before = events;
                        Require(rig.StartCoin(owner, ordinal), "capacity accepts owner " + owner + " coin " + ordinal);
                        Rigidbody2D coin = rig.GetCoinBody(owner, ordinal);
                        Require(coin && Mathf.Abs(coin.mass - rig.CoinMasses[ordinal - 1]) < .00001f, "each ordinal retains its configured mass");
                        rig.AutoPlace();
                        float depositDeadline = Time.realtimeSinceStartup + 7;
                        while (rig.IsBusy && Time.realtimeSinceStartup < depositDeadline) yield return null;
                        Require(!rig.IsBusy && events == before + 1 && lastOwner == owner, "assisted drop completes exactly once within its deadline");
                        Require(rig.DepositedCount(owner) == ordinal, "pan owns all accepted coins");
                    }
                }
                Require(events == 10 && rig.SpawnedCoinCount == 10, "ten awarded coins produce exactly ten instances and events");
                Require(!rig.StartCoin(0, 6) && !rig.StartCoin(1, 6), "sixth coin cannot be spawned");
                yield return new WaitForSeconds(rig.FallenCoinRecoverySeconds + .4f);
                for (int owner = 0; owner < 2; owner++)
                    for (int ordinal = 1; ordinal <= 5; ordinal++)
                    {
                        Rigidbody2D coin = rig.GetCoinBody(owner, ordinal);
                        Require(coin && coin.simulated && coin.position.y > rig.RecoveryY, "deposited coin remains a live physical body");
                    }
                Require(events == 10 && rig.DepositedCount(0) == 5 && rig.DepositedCount(1) == 5, "resting and recovery preserve both five-coin totals");
                note?.Invoke("PASS physics: five physical coins per pan, distinct Inspector masses, accessible placement and persistent ownership.");

                rig.ResetCoins();
                VerifyReset(rig, bodies, initialPositions, initialAngles);
                Require(rig.StartCoin(0, 1), "new match can award ordinal one again");
                rig.AutoPlace();
                yield return null;
                rig.ResetCoins();
                VerifyReset(rig, bodies, initialPositions, initialAngles);
                Require(events == 10, "reset during auto transport cannot emit a late deposit");
                note?.Invoke("PASS physics: reset restores original beam/pan poses and velocities, cancels pending coins and clears award identity.");
            }
            finally
            {
                rig.OnDeposited -= listener;
                rig.ResetCoins();
                rig.EnableMouseInput = previousMouse;
                rig.HumanSeat = previousHuman;
                Time.timeScale = previousTimeScale;
            }
        }

        static void PlaceAtRest(Rigidbody2D coin, Rigidbody2D pan, float height)
        {
            Vector2 position = pan.transform.TransformPoint(new Vector3(0, height, 0));
            SetTestPose(coin, position, pan.rotation);
            coin.linearVelocity = pan.GetPointVelocity(position);
            coin.angularVelocity = pan.angularVelocity;
            coin.WakeUp();
            Physics2D.SyncTransforms();
        }

        static void SetTestPose(Rigidbody2D body, Vector2 position, float angle)
        {
            RigidbodyInterpolation2D interpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.transform.SetPositionAndRotation(new Vector3(position.x, position.y, body.transform.position.z), Quaternion.Euler(0, 0, angle));
            body.position = position;
            body.rotation = angle;
            Physics2D.SyncTransforms();
            body.interpolation = interpolation;
        }

        static string State(LastKingPhysics rig, Rigidbody2D body, int events)
        {
            return "position=" + body.position.ToString("F3") + ", transform=" + body.transform.position.ToString("F3") +
                ", events=" + events + ", busy=" + rig.IsBusy + ", enabled=" + rig.isActiveAndEnabled +
                ", human=" + rig.HumanSeat + ", activeOwner=" + rig.ActiveOwner + ", remaining=" + rig.SecondsRemaining +
                ", recoveryY=" + rig.RecoveryY + ", timeScale=" + Time.timeScale + ", simulated=" + body.simulated;
        }

        static void VerifyReset(LastKingPhysics rig, List<Rigidbody2D> bodies, Vector2[] positions, float[] angles)
        {
            Require(!rig.IsBusy && rig.SpawnedCoinCount == 0 && rig.DepositedCount(0) == 0 && rig.DepositedCount(1) == 0, "reset clears all coin state");
            for (int i = 0; i < bodies.Count; i++)
            {
                Require(Vector2.Distance(bodies[i].position, positions[i]) < .0001f, "reset restores body position " + i);
                Require(Mathf.Abs(Mathf.DeltaAngle(bodies[i].rotation, angles[i])) < .001f, "reset restores body angle " + i);
                Require(bodies[i].linearVelocity.sqrMagnitude < .000001f && Mathf.Abs(bodies[i].angularVelocity) < .001f, "reset clears body velocity " + i);
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("LAST KING PHYSICS CHECK FAILED: " + message);
        }
    }
}
