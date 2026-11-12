using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheLastKing
{
    /// <summary>
    /// Presents coins already awarded by the rules. A coin's mass affects the
    /// actual Rigidbody2D assembly, never the authoritative Blood Coin count.
    /// Scene art, beam, pan joints, colliders and the coin prefab are serialized
    /// assets. Only awarded coin instances are created at runtime.
    /// </summary>
    public sealed class LastKingPhysics : MonoBehaviour
    {
        [Header("Editable scene references")]
        public Camera WorldCamera;
        public GameObject CoinPrefab;
        public Transform PickupPoint;
        public Rigidbody2D Beam;
        public Rigidbody2D[] Pans = new Rigidbody2D[2];
        public Collider2D[] PanSensors = new Collider2D[2];
        public Transform[] DepositPoints = new Transform[2];
        public Transform CoinRoot;

        [Header("Coin weight (physics only, ordinal 1 through 5)")]
        public float[] CoinMasses = { .10f, .12f, .09f, .14f, .11f };
        public Color[] OwnerTints = { new Color(1f, .91f, .68f), new Color(.89f, .65f, .40f) };

        [Header("Pickup and placement")]
        public bool EnableMouseInput = true;
        public int HumanSeat = 0;
        [Min(1f)] public float PlacementTimeout = 20f;
        [Min(0f)] public float AutoPlaceDelay = 1.1f;
        [Min(.05f)] public float AutoMoveSeconds = .5f;
        public Rect DragBounds = new Rect(-7.8f, -2.6f, 7.1f, 6.3f);
        public float RecoveryY = -3.1f;
        [Min(.01f)] public float PickupPadding = .14f;
        [Min(1f)] public float DragSpring = 32f;
        [Min(.01f)] public float MaximumDragForce = 18f;

        [Header("Settling and recovery")]
        [Min(.01f)] public float SettleSpeed = .23f;
        [Min(.05f)] public float SettleSeconds = .25f;
        [Min(.1f)] public float FallenCoinRecoverySeconds = 1.5f;
        [Min(.01f)] public float PanRestHeight = .16f;
        [Min(.01f)] public float CoinSpacing = .23f;

        /// <summary>Emitted exactly once per owner/ordinal, after physical placement.</summary>
        public event Action<int> OnDeposited;
        public bool IsBusy { get { return active != null; } }
        public bool IsHoldingCoin { get { return held; } }
        public int ActiveOwner { get { return active == null ? -1 : active.Owner; } }
        public int ActiveCoinIndex { get { return active == null ? 0 : active.Ordinal; } }
        public float SecondsRemaining { get { return active == null ? 0 : Mathf.Max(0, PlacementTimeout - placementAge); } }
        public int SpawnedCoinCount { get { return coins.Count; } }

        sealed class Coin
        {
            public int Owner, Ordinal;
            public Rigidbody2D Body;
            public Collider2D Collider;
            public bool Deposited;
            public float OutsideSeconds;
        }

        struct BodyPose
        {
            public Rigidbody2D Body;
            public Vector2 Position;
            public float Rotation;
            public bool Simulated;
        }

        readonly List<Coin> coins = new List<Coin>(10);
        readonly List<BodyPose> originalPoses = new List<BodyPose>(3);
        Coin active;
        bool held, autoMoving, autoRelease;
        Vector2 pointerTarget, grabOffset, autoFrom;
        float placementAge, settleAge, autoAge, autoReleaseAge;

        void Awake()
        {
            if (!WorldCamera) WorldCamera = Camera.main;
            RememberRig();
        }

        void OnValidate()
        {
            PlacementTimeout = Mathf.Max(1, PlacementTimeout);
            AutoMoveSeconds = Mathf.Max(.05f, AutoMoveSeconds);
            SettleSeconds = Mathf.Max(.05f, SettleSeconds);
            SettleSpeed = Mathf.Max(.01f, SettleSpeed);
            HumanSeat = Mathf.Clamp(HumanSeat, 0, 1);
            if (CoinMasses != null)
                for (int i = 0; i < CoinMasses.Length; i++) CoinMasses[i] = Mathf.Max(.001f, CoinMasses[i]);
        }

        /// <summary>
        /// Starts one presentation. coinIndex is the new logical count, 1..5.
        /// Rejects duplicates and overlapping presentations without emitting an event.
        /// </summary>
        public bool StartCoin(int owner, int coinIndex)
        {
            if (active != null || owner < 0 || owner > 1 || coinIndex < 1 || coinIndex > 5) return false;
            if (!HasRig(owner))
            {
                Debug.LogError("LastKingPhysics needs a camera, coin prefab, pickup point, and two physical pans with sensors/deposit points.", this);
                return false;
            }
            for (int i = 0; i < coins.Count; i++)
                if (coins[i].Owner == owner && coins[i].Ordinal == coinIndex) return false;

            RememberRig();
            GameObject instance = Instantiate(CoinPrefab, PickupPoint.position, Quaternion.identity, CoinRoot ? CoinRoot : transform);
            instance.name = (owner == 0 ? "Player" : "Opponent") + " Blood Coin " + coinIndex;
            instance.SetActive(true);
            Rigidbody2D body = instance.GetComponent<Rigidbody2D>();
            Collider2D shape = instance.GetComponentInChildren<Collider2D>();
            if (!body || !shape)
            {
                Debug.LogError("CoinPrefab requires a Rigidbody2D and Collider2D.", CoinPrefab);
                instance.SetActive(false);
                Destroy(instance);
                return false;
            }
            body.bodyType = RigidbodyType2D.Dynamic;
            body.useAutoMass = false;
            body.mass = CoinMasses != null && CoinMasses.Length >= coinIndex ? Mathf.Max(.001f, CoinMasses[coinIndex - 1]) : .1f;
            body.gravityScale = 1;
            body.linearDamping = .15f;
            body.angularDamping = 1f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0;
            body.simulated = true;
            shape.isTrigger = false;
            SpriteRenderer renderer = instance.GetComponentInChildren<SpriteRenderer>();
            if (renderer && OwnerTints != null && OwnerTints.Length > owner) renderer.color = OwnerTints[owner];

            active = new Coin { Owner = owner, Ordinal = coinIndex, Body = body, Collider = shape };
            coins.Add(active);
            held = autoMoving = autoRelease = false;
            placementAge = settleAge = autoAge = autoReleaseAge = 0;
            return true;
        }

        /// <summary>Accessible assistance: carry the existing coin, then release it into its own pan.</summary>
        public void AutoPlace()
        {
            if (active == null || autoMoving || !active.Body) return;
            held = false;
            settleAge = autoAge = autoReleaseAge = 0;
            autoRelease = false;
            autoMoving = true;
            autoFrom = active.Body.position;
            active.Body.linearVelocity = Vector2.zero;
            active.Body.angularVelocity = 0;
            active.Body.interpolation = RigidbodyInterpolation2D.None;
            active.Body.simulated = false;
        }

        /// <summary>Pointer-independent input commands are also usable by accessibility and play-mode checks.</summary>
        public bool TryGrabCoin(Vector2 worldPoint)
        {
            if (Time.timeScale <= 0 || active == null || !active.Body || active.Owner != HumanSeat || autoMoving || !CanPick(worldPoint)) return false;
            held = true;
            autoRelease = false;
            grabOffset = active.Body.position - worldPoint;
            active.Body.gravityScale = 0;
            active.Body.WakeUp();
            settleAge = 0;
            MoveHeldCoin(worldPoint);
            return true;
        }

        public void MoveHeldCoin(Vector2 worldPoint)
        {
            if (!held || active == null) return;
            Vector2 target = worldPoint + grabOffset;
            pointerTarget = new Vector2(Mathf.Clamp(target.x, DragBounds.xMin, DragBounds.xMax), Mathf.Clamp(target.y, DragBounds.yMin, DragBounds.yMax));
        }

        public void ReleaseCoin() { Release(); }

        /// <summary>Clears presentation state and restores the serialized beam/pan starting poses.</summary>
        public void ResetCoins()
        {
            RememberRig();
            active = null;
            held = autoMoving = autoRelease = false;
            placementAge = settleAge = autoAge = autoReleaseAge = 0;
            for (int i = 0; i < coins.Count; i++)
            {
                Rigidbody2D body = coins[i].Body;
                if (!body) continue;
                body.simulated = false;
                body.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(body.gameObject);
                else DestroyImmediate(body.gameObject);
            }
            coins.Clear();
            for (int i = 0; i < originalPoses.Count; i++)
            {
                BodyPose pose = originalPoses[i];
                if (!pose.Body) continue;
                pose.Body.simulated = false;
                SetPose(pose.Body, pose.Position, pose.Rotation);
                pose.Body.linearVelocity = Vector2.zero;
                pose.Body.angularVelocity = 0;
            }
            Physics2D.SyncTransforms();
            for (int i = 0; i < originalPoses.Count; i++)
                if (originalPoses[i].Body) originalPoses[i].Body.simulated = originalPoses[i].Simulated;
        }

        public int DepositedCount(int owner)
        {
            int count = 0;
            for (int i = 0; i < coins.Count; i++) if (coins[i].Owner == owner && coins[i].Deposited) count++;
            return count;
        }

        public Rigidbody2D GetCoinBody(int owner, int coinIndex)
        {
            for (int i = 0; i < coins.Count; i++)
                if (coins[i].Owner == owner && coins[i].Ordinal == coinIndex) return coins[i].Body;
            return null;
        }

        void Update()
        {
            if (Time.timeScale <= 0) return;
            RecoverDepositedCoins();
            if (active == null || !active.Body) return;

            placementAge += Time.deltaTime;
            if (autoMoving)
            {
                autoAge += Time.deltaTime;
                float t = Mathf.Clamp01(autoAge / AutoMoveSeconds);
                Vector2 target = DropPosition(active);
                Vector2 carried = Vector2.Lerp(autoFrom, target, Mathf.SmoothStep(0, 1, t));
                carried.y += Mathf.Sin(t * Mathf.PI) * .65f;
                SetPose(active.Body, carried, active.Body.rotation);
                if (t >= 1)
                {
                    SetPose(active.Body, target, 0);
                    active.Body.simulated = true;
                    active.Body.interpolation = RigidbodyInterpolation2D.Interpolate;
                    active.Body.gravityScale = 1;
                    active.Body.linearVelocity = Pans[active.Owner].GetPointVelocity(target);
                    active.Body.angularVelocity = 0;
                    active.Body.WakeUp();
                    autoMoving = false;
                    autoRelease = true;
                    autoReleaseAge = 0;
                }
                return;
            }

            if (EnableMouseInput && active.Owner == HumanSeat)
            {
                Vector2 pointer;
                if (TryPointer(out pointer))
                {
                    if (!held && Input.GetMouseButtonDown(0)) TryGrabCoin(pointer);
                    if (held)
                    {
                        MoveHeldCoin(pointer);
                        if (!Input.GetMouseButton(0)) Release();
                    }
                }
                if (Input.GetKeyDown(KeyCode.Space)) AutoPlace();
            }

            if (autoMoving) return;
            if ((active.Owner != HumanSeat && placementAge >= AutoPlaceDelay && !autoRelease) ||
                (placementAge >= PlacementTimeout && !autoRelease))
            {
                AutoPlace();
                return;
            }

            if (Escaped(active.Body.position))
            {
                ReturnToPickup();
                return;
            }

            if (autoRelease)
            {
                autoReleaseAge += Time.deltaTime;
                // A bounded recovery for a bouncing/escaped coin. The same coin
                // is seated inside its tray; no logical reward is created here.
                if (autoReleaseAge >= 4f)
                {
                    SeatInPan(active);
                    if (InsideOwnPan(active)) FinishDeposit();
                    return;
                }
            }

            if (!held && InsideOwnPan(active))
            {
                Vector2 panVelocity = Pans[active.Owner].GetPointVelocity(active.Body.position);
                if ((active.Body.linearVelocity - panVelocity).sqrMagnitude <= SettleSpeed * SettleSpeed)
                {
                    settleAge += Time.deltaTime;
                    if (settleAge >= SettleSeconds) FinishDeposit();
                    return;
                }
            }
            settleAge = 0;
        }

        void FixedUpdate()
        {
            if (Time.timeScale <= 0 || active == null || !held || autoMoving || !active.Body) return;
            float damping = 2f * Mathf.Sqrt(DragSpring * active.Body.mass);
            Vector2 force = DragSpring * (pointerTarget - active.Body.position) - damping * active.Body.linearVelocity;
            active.Body.AddForce(Vector2.ClampMagnitude(force, MaximumDragForce), ForceMode2D.Force);
        }

        bool HasRig(int owner)
        {
            return WorldCamera && CoinPrefab && PickupPoint && Pans != null && Pans.Length == 2 && Pans[owner] &&
                PanSensors != null && PanSensors.Length == 2 && PanSensors[owner] &&
                DepositPoints != null && DepositPoints.Length == 2 && DepositPoints[owner];
        }

        void RememberRig()
        {
            RememberBody(Beam);
            if (Pans != null) for (int i = 0; i < Pans.Length; i++) RememberBody(Pans[i]);
        }

        void RememberBody(Rigidbody2D body)
        {
            if (!body) return;
            for (int i = 0; i < originalPoses.Count; i++) if (originalPoses[i].Body == body) return;
            originalPoses.Add(new BodyPose { Body = body, Position = body.position, Rotation = body.rotation, Simulated = body.simulated });
        }

        bool TryPointer(out Vector2 point)
        {
            Ray ray = WorldCamera.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(Vector3.forward, Vector3.zero);
            float distance;
            if (plane.Raycast(ray, out distance)) { point = ray.GetPoint(distance); return true; }
            point = Vector2.zero;
            return false;
        }

        bool CanPick(Vector2 point)
        {
            if (active.Collider.OverlapPoint(point)) return true;
            return Vector2.Distance(active.Collider.ClosestPoint(point), point) <= PickupPadding;
        }

        void Release()
        {
            held = false;
            settleAge = 0;
            if (active != null && active.Body) active.Body.gravityScale = 1;
        }

        bool InsideOwnPan(Coin coin)
        {
            return coin.Body && PanSensors[coin.Owner] && PanSensors[coin.Owner].OverlapPoint(coin.Body.position);
        }

        bool Escaped(Vector2 position)
        {
            return position.y < RecoveryY || position.x < DragBounds.xMin - 1 || position.x > DragBounds.xMax + 1;
        }

        float SlotOffset(int ordinal)
        {
            // Center first, then alternate left/right. Five .22-diameter coins
            // fit in the builder's 1.25-unit pan without making a tall tower.
            int step = ordinal / 2;
            return ordinal == 1 ? 0 : (ordinal % 2 == 0 ? -step : step) * CoinSpacing;
        }

        Vector2 DropPosition(Coin coin)
        {
            Transform drop = DepositPoints[coin.Owner];
            return (Vector2)drop.position + (Vector2)drop.right * SlotOffset(coin.Ordinal);
        }

        void SeatInPan(Coin coin)
        {
            Rigidbody2D pan = Pans[coin.Owner];
            Vector2 position = pan.transform.TransformPoint(new Vector3(SlotOffset(coin.Ordinal), PanRestHeight, 0));
            SetPose(coin.Body, position, pan.rotation);
            coin.Body.gravityScale = 1;
            coin.Body.linearVelocity = pan.GetPointVelocity(position);
            coin.Body.angularVelocity = pan.angularVelocity;
            coin.Body.simulated = true;
            coin.Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            coin.Body.WakeUp();
            coin.OutsideSeconds = 0;
            Physics2D.SyncTransforms();
        }

        void ReturnToPickup()
        {
            if (active == null) return;
            held = autoMoving = autoRelease = false;
            settleAge = autoAge = autoReleaseAge = 0;
            SetPose(active.Body, PickupPoint.position, 0);
            active.Body.gravityScale = 1;
            active.Body.linearVelocity = Vector2.zero;
            active.Body.angularVelocity = 0;
            active.Body.simulated = true;
            active.Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            active.Body.WakeUp();
        }

        // Rigidbody interpolation owns a separate rendered Transform pose. An
        // instantaneous recovery must update both poses before SyncTransforms,
        // otherwise a stale rendered pose can overwrite the requested body pose.
        static void SetPose(Rigidbody2D body, Vector2 position, float rotation)
        {
            RigidbodyInterpolation2D interpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation2D.None;
            Vector3 visualPosition = body.transform.position;
            visualPosition.x = position.x;
            visualPosition.y = position.y;
            body.transform.SetPositionAndRotation(visualPosition, Quaternion.Euler(0, 0, rotation));
            body.position = position;
            body.rotation = rotation;
            Physics2D.SyncTransforms();
            body.interpolation = interpolation;
        }

        void FinishDeposit()
        {
            if (active == null || active.Deposited) return;
            int owner = active.Owner;
            active.Deposited = true;
            active.OutsideSeconds = 0;
            active = null;
            held = autoMoving = autoRelease = false;
            placementAge = settleAge = autoAge = autoReleaseAge = 0;
            // State is committed first, so a listener can safely start the next coin.
            OnDeposited?.Invoke(owner);
        }

        void RecoverDepositedCoins()
        {
            for (int i = 0; i < coins.Count; i++)
            {
                Coin coin = coins[i];
                if (!coin.Deposited || !coin.Body) continue;
                if (InsideOwnPan(coin)) { coin.OutsideSeconds = 0; continue; }
                coin.OutsideSeconds += Time.deltaTime;
                if (Escaped(coin.Body.position) || coin.OutsideSeconds >= FallenCoinRecoverySeconds) SeatInPan(coin);
            }
        }

        void OnApplicationFocus(bool hasFocus) { if (!hasFocus) Release(); }
        void OnDisable()
        {
            Release();
            if (active != null && active.Body && autoMoving)
            {
                active.Body.simulated = true;
                active.Body.gravityScale = 1;
                autoMoving = false;
            }
        }
    }
}
