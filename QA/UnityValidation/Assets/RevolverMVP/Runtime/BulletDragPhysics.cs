using UnityEngine;

namespace RevolverMVP
{
    /// <summary>Real Rigidbody2D spring dragging; the view never sets position during a drag.</summary>
    public sealed class BulletDragPhysics : MonoBehaviour
    {
        [Min(1)] public float baseSpring = 90f;
        [Min(0)] public float baseTremor = .6f;
        [Min(0)] public float tremorFrequency = 7f;
        public Rigidbody2D Body { get; private set; }
        public bool IsHeld { get; private set; }
        public Vector2 Position { get { return Body ? Body.position : Vector2.zero; } }
        public float BeatsPerMinute { get { return Mathf.Lerp(70, 160, bulletCount / 5f); } }
        public float SpringStrength { get { return baseSpring * (1f - .14f * bulletCount); } }
        public int HeartbeatCount { get; private set; }
        public bool Active { get; private set; }
        int bulletCount;
        Vector2 target, tray;
        float clock, nextHeartbeat;
        PhysicsMaterial2D material;
        GameObject physicsRoot;

        public void Initialize()
        {
            if (Body) return;
            physicsRoot = new GameObject("Bullet loading physics");
            physicsRoot.transform.SetParent(transform, false);
            material = new PhysicsMaterial2D("Brass on tabletop") { friction = .5f, bounciness = .3f };
            MakeWall("Tabletop", new Vector2(0, -3.1f), new Vector2(15, .2f));
            MakeWall("Left tray edge", new Vector2(-7.4f, 0), new Vector2(.15f, 10));
            MakeWall("Right tray edge", new Vector2(7.4f, 0), new Vector2(.15f, 10));
            var bullet = new GameObject("Loose brass bullet");
            bullet.transform.SetParent(physicsRoot.transform, false);
            Body = bullet.AddComponent<Rigidbody2D>();
            Body.mass = 1;
            Body.gravityScale = 1;
            Body.linearDamping = .1f;
            Body.angularDamping = 1f;
            Body.constraints = RigidbodyConstraints2D.FreezeRotation;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var shape = bullet.AddComponent<CircleCollider2D>();
            shape.radius = .14f;
            shape.sharedMaterial = material;
            Body.simulated = false;
        }

        void MakeWall(string wallName, Vector2 position, Vector2 size)
        {
            var wall = new GameObject(wallName);
            wall.transform.SetParent(physicsRoot.transform, false);
            wall.transform.position = position;
            var collider = wall.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.sharedMaterial = material;
        }

        public void Begin(int existingBulletCount, Vector2 spawn)
        {
            Initialize();
            bulletCount = Mathf.Clamp(existingBulletCount, 0, 5);
            tray = spawn;
            Active = true;
            clock = 0;
            nextHeartbeat = 60f / BeatsPerMinute;
            HeartbeatCount = 0;
            ResetToTray();
        }

        public void ResetToTray()
        {
            Initialize();
            IsHeld = false;
            Body.simulated = false;
            Body.position = tray;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            Body.gravityScale = 1;
            Body.simulated = Active;
        }

        public void Grab(Vector2 mouseTarget)
        {
            if (!Active) return;
            IsHeld = true;
            MoveTarget(mouseTarget);
            Body.gravityScale = 0;
            Body.WakeUp();
        }

        public void MoveTarget(Vector2 mouseTarget)
        {
            target = new Vector2(Mathf.Clamp(mouseTarget.x, -7.1f, 7.1f), Mathf.Clamp(mouseTarget.y, -2.9f, 3.5f));
        }

        public void Release()
        {
            IsHeld = false;
            if (Body) Body.gravityScale = 1;
        }

        public void Hide()
        {
            IsHeld = false;
            Active = false;
            if (Body) { Body.linearVelocity = Vector2.zero; Body.simulated = false; }
        }

        void FixedUpdate()
        {
            if (!Active || !Body) return;
            // Only a recovery for a numerical escape, never a shortcut during normal dragging.
            if (Body.position.y < -4.5f || Mathf.Abs(Body.position.x) > 8.5f) ResetToTray();
            clock += Time.fixedDeltaTime;
            bool heartbeat = clock >= nextHeartbeat;
            if (heartbeat) { nextHeartbeat += 60f / BeatsPerMinute; HeartbeatCount++; }
            if (!IsHeld) return;
            float spring = SpringStrength;
            float damping = 1.6f * Mathf.Sqrt(spring * Body.mass);
            float amplitude = baseTremor * bulletCount * bulletCount;
            Vector2 noise = new Vector2(Mathf.PerlinNoise(tremorFrequency * clock, .37f) - .5f,
                Mathf.PerlinNoise(.83f, tremorFrequency * clock) - .5f);
            Vector2 force = spring * (target - Body.position) - damping * Body.linearVelocity + amplitude * noise;
            Body.AddForce(force, ForceMode2D.Force);
            if (heartbeat)
            {
                Vector2 direction = new Vector2(Mathf.Sin(HeartbeatCount * 2.4f), Mathf.Cos(HeartbeatCount * 1.7f));
                Body.AddForce(direction * (.025f + .025f * bulletCount), ForceMode2D.Impulse);
            }
        }

        void OnDisable() { Hide(); }
        void OnDestroy()
        {
            if (!material) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}
