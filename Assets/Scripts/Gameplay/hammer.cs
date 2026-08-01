using UnityEngine;

using System.Collections.Generic;
using GlassShooter.Gameplay;
using PolygonRendering;

public class Hammer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D pendulum;

    [Header("Pendulum")]
    [SerializeField] private float radius = 2f;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        ConstrainPendulum();

        bool isClockwise = GetIsClockwise();

        // 確認用
        // Debug.Log(isClockwise ? "Clockwise" : "Counterclockwise");
    }


    [SerializeField, Min(0f)]
    private float pendulumDrag = 0.5f;

    private void ConstrainPendulum()
    {
        float deltaTime = Time.fixedDeltaTime;

        Vector2 hammerPosition = rb.position;
        Vector2 hammerVelocity = rb.linearVelocity;

        Vector2 radialDirection =
            (pendulum.position - hammerPosition).normalized;

        if (radialDirection.sqrMagnitude < 0.000001f)
        {
            radialDirection = Vector2.down;
        }

        // ハンマーに対する振り子の相対速度
        Vector3 relativeVelocity =
            pendulum.linearVelocity - hammerVelocity;

        // 半径方向成分を除き、接線方向の速度だけ残す
        Vector2 tangentialVelocity =
            Vector3.ProjectOnPlane(relativeVelocity, radialDirection);

        // 角速度に比例する抵抗
        float damping =
            Mathf.Exp(-pendulumDrag * deltaTime);

        tangentialVelocity *= damping;

        // 次の位置を予測
        Vector2 predictedPosition =
            pendulum.position
            + (hammerVelocity + tangentialVelocity) * deltaTime;

        // 半径radiusの円周上へ戻す
        Vector2 predictedDirection =
            predictedPosition - hammerPosition;

        if (predictedDirection.sqrMagnitude < 0.000001f)
        {
            predictedDirection = radialDirection;
        }

        Vector2 constrainedPosition =
            hammerPosition
            + predictedDirection.normalized * radius;

        // 拘束後の半径方向
        Vector2 constrainedRadial =
            (constrainedPosition - hammerPosition).normalized;

        // 接線速度を新しい半径方向に沿わせ直す
        tangentialVelocity =
            Vector3.ProjectOnPlane(
                tangentialVelocity,
                constrainedRadial
            );

        pendulum.linearVelocity =
            hammerVelocity + tangentialVelocity;

        pendulum.MovePosition(constrainedPosition);
    }

    private bool GetIsClockwise()
    {
        Vector2 radialDirection =
            pendulum.position - rb.position;

        Vector2 velocity = pendulum.linearVelocity - rb.linearVelocity;

        // XY平面上の外積Z成分
        float crossZ =
            radialDirection.x * velocity.y
            - radialDirection.y * velocity.x;

        // UnityのXY平面を正面から見た場合、
        // crossZ < 0 が時計回り
        return crossZ < 0f;
    }
}

namespace GlassShooter.Gameplay
{
    [DisallowMultipleComponent]
    internal sealed class BouncingProjectile : MonoBehaviour
    {
        private const float SeparationDistance = 0.02f;
        private const float ReferenceFixedDeltaTime = 0.02f;
        private readonly HashSet<string> attackedThisStep = new();

        private Rigidbody2D body;
        private BulletStatus status;
        private PlayerShooterController player;
        private Vector2 direction;
        private float speed;
        private bool initialized;

        public void Initialize(
            BulletStatus bulletStatus, Vector2 initialDirection, PlayerShooterController owner)
        {
            body = GetComponent<Rigidbody2D>();
            status = bulletStatus;
            player = owner;
            direction = initialDirection.sqrMagnitude > 0.000001f
                ? initialDirection.normalized
                : Vector2.up;
            speed = status != null ? status.CurrentVelocity.magnitude : 3.5f;
            initialized = true;
        }

        public void ApplyStatus(float mass, float newSpeed, float efficiency)
        {
            status ??= GetComponent<BulletStatus>();
            speed = Mathf.Max(0f, newSpeed);
            status.ApplyGrowthStatus(mass, speed, 0f, efficiency, 1f);
            status.SetCurrentVelocity(direction * speed);
        }

        private void FixedUpdate()
        {
            attackedThisStep.Clear();
            if (!initialized || body == null || status == null)
            {
                return;
            }
            Vector2 next = body.position + direction * speed * Time.fixedDeltaTime;
            ResolveScreenReflection(ref next);
            Vector2 velocity = direction * speed;
            status.SetCurrentVelocity(velocity);
            body.linearVelocity = velocity;
            body.MovePosition(next);
        }

        private void ResolveScreenReflection(ref Vector2 position)
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.orthographic)
            {
                Vector2 min = player != null ? player.MoveLimitMin : new Vector2(-15f, -8.5f);
                Vector2 max = player != null ? player.MoveLimitMax : new Vector2(15f, 8.5f);
                ReflectBounds(ref position, min, max);
                return;
            }
            float vertical = camera.orthographicSize;
            float horizontal = vertical * camera.aspect;
            Vector2 center = camera.transform.position;
            ReflectBounds(
                ref position,
                center - new Vector2(horizontal, vertical),
                center + new Vector2(horizontal, vertical));
        }

        private void ReflectBounds(ref Vector2 position, Vector2 min, Vector2 max)
        {
            bool reflectX = position.x < min.x || position.x > max.x;
            bool reflectY = position.y < min.y || position.y > max.y;
            if (reflectX)
            {
                direction.x = -direction.x;
                position.x = Mathf.Clamp(position.x, min.x, max.x);
            }
            if (reflectY)
            {
                direction.y = -direction.y;
                position.y = Mathf.Clamp(position.y, min.y, max.y);
            }
        }

        private void OnTriggerEnter2D(Collider2D other) => HandleGlassContact(other, 1f, true);

        private void OnTriggerStay2D(Collider2D other)
        {
            HandleGlassContact(other, Time.fixedDeltaTime / ReferenceFixedDeltaTime, false);
        }

        private void HandleGlassContact(Collider2D other, float energyScale, bool reflect)
        {
            if (other == null || status == null)
            {
                return;
            }
            CrackProcessingComponent crack = other.GetComponentInParent<CrackProcessingComponent>();
            GlassFragment fragment = crack == null ? other.GetComponentInParent<GlassFragment>() : null;
            Object target = crack != null ? (Object)crack : fragment;
            if (target == null)
            {
                return;
            }
            string id = target.GetEntityId().ToString();
            if (!attackedThisStep.Add(id))
            {
                return;
            }

            Vector2 impactPosition = other.ClosestPoint(transform.position);
            ImpactEnergyContext source = ImpactEnergyContext.FromBullet(impactPosition, status);
            ImpactEnergyContext context = new(
                source.WorldPosition,
                source.ImpactVelocity,
                source.ImpactEnergy * Mathf.Max(0f, energyScale),
                source.WeaponEfficiency,
                1f);
            if (crack != null)
            {
                crack.HandleImpact(context);
            }
            else
            {
                fragment.ConsumeImpact(context);
            }

            if (!reflect)
            {
                return;
            }
            Vector2 normal = (Vector2)transform.position - impactPosition;
            if (normal.sqrMagnitude <= 0.000001f)
            {
                normal = (Vector2)transform.position - (Vector2)other.bounds.center;
            }
            if (normal.sqrMagnitude <= 0.000001f)
            {
                normal = -direction;
            }
            normal.Normalize();
            direction = Vector2.Reflect(direction, normal).normalized;
            if (body != null)
            {
                body.position += normal * SeparationDistance;
            }
        }
    }

    [DisallowMultipleComponent]
    internal sealed class HammerPendulumWeapon : MonoBehaviour
    {
        private const float RotationRadius = 2f;

        private Rigidbody2D centerBody;
        private Rigidbody2D headBody;
        private RegularPolygonLineRenderer polygon;
        private PolygonCollider2D headCollider;
        private HammerHeadImpact impact;
        private float reach = 1f;
        private float drag = 0.8f;

        public static HammerPendulumWeapon Create(PlayerShooterController player)
        {
            GameObject root = new("HammerWeapon");
            HammerPendulumWeapon weapon = root.AddComponent<HammerPendulumWeapon>();
            weapon.centerBody = player.GetComponent<Rigidbody2D>();
            weapon.CreateHead(player.transform.position);
            return weapon;
        }

        public void Configure(float mass, float newReach, float newDrag, float efficiency)
        {
            reach = Mathf.Max(0f, newReach);
            drag = Mathf.Max(0f, newDrag);
            float size = reach * 0.5f;
            polygon.color = new Color(1f, 0.75f, 0.12f, 1f);
            polygon.VertexCount = 5;
            polygon.IsPlayerSide = true;
            polygon.PointVertexVertically = true;
            polygon.Size = size;
            headCollider.points = BuildRegularPolygon(5, size);
            headBody.mass = Mathf.Max(0.0001f, mass);
            impact.Configure(mass, efficiency);
        }

        private void CreateHead(Vector2 centerPosition)
        {
            GameObject head = new("HammerHead");
            head.transform.SetParent(transform, true);
            head.transform.position = centerPosition + Vector2.down * (RotationRadius + 0.5f);
            polygon = head.AddComponent<RegularPolygonLineRenderer>();
            LineRenderer line = head.GetComponent<LineRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.material = new Material(shader);
            }
            headCollider = head.AddComponent<PolygonCollider2D>();
            headCollider.isTrigger = true;
            headBody = head.AddComponent<Rigidbody2D>();
            headBody.bodyType = RigidbodyType2D.Kinematic;
            headBody.gravityScale = 0f;
            headBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            headBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            impact = head.AddComponent<HammerHeadImpact>();
            impact.Bind(headBody);
        }

        private void FixedUpdate()
        {
            if (centerBody == null || headBody == null)
            {
                return;
            }
            float dt = Time.fixedDeltaTime;
            Vector2 center = centerBody.position;
            Vector2 radial = headBody.position - center;
            if (radial.sqrMagnitude <= 0.000001f)
            {
                radial = Vector2.down;
            }
            Vector2 radialDirection = radial.normalized;
            Vector2 tangent = headBody.linearVelocity - centerBody.linearVelocity;
            tangent -= Vector2.Dot(tangent, radialDirection) * radialDirection;
            tangent *= Mathf.Exp(-drag * dt);
            Vector2 predicted = headBody.position + (centerBody.linearVelocity + tangent) * dt;
            Vector2 predictedDirection = predicted - center;
            if (predictedDirection.sqrMagnitude <= 0.000001f)
            {
                predictedDirection = radialDirection;
            }
            Vector2 constrained = center + predictedDirection.normalized * (RotationRadius + reach * 0.5f);
            Vector2 newRadial = (constrained - center).normalized;
            tangent -= Vector2.Dot(tangent, newRadial) * newRadial;
            headBody.linearVelocity = centerBody.linearVelocity + tangent;
            headBody.MovePosition(constrained);
        }

        private static Vector2[] BuildRegularPolygon(int count, float size)
        {
            Vector2[] points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float angle = (90f + 360f * i / count) * Mathf.Deg2Rad;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size;
            }
            return points;
        }
    }

    [DisallowMultipleComponent]
    internal sealed class HammerHeadImpact : MonoBehaviour
    {
        private readonly HashSet<string> attackedThisStep = new();
        private Rigidbody2D headBody;
        private float mass;
        private float efficiency;

        public void Bind(Rigidbody2D body) => headBody = body;

        public void Configure(float newMass, float newEfficiency)
        {
            mass = Mathf.Max(0f, newMass);
            efficiency = Mathf.Max(0f, newEfficiency);
        }

        private void FixedUpdate()
        {
            attackedThisStep.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null || headBody == null)
            {
                return;
            }
            CrackProcessingComponent crack = other.GetComponentInParent<CrackProcessingComponent>();
            GlassFragment fragment = other.GetComponentInParent<GlassFragment>();
            Object target = crack != null ? (Object)crack : fragment;
            if (target == null)
            {
                return;
            }
            string id = target.GetEntityId().ToString();
            if (!attackedThisStep.Add(id))
            {
                return;
            }

            Rigidbody2D targetBody = other.attachedRigidbody;
            Vector2 targetVelocity = targetBody != null ? targetBody.linearVelocity : Vector2.zero;
            Vector2 relativeVelocity = headBody.linearVelocity - targetVelocity;
            Vector2 impactPosition = other.ClosestPoint(transform.position);
            ImpactEnergyContext context = new(
                impactPosition,
                relativeVelocity,
                0.5f * mass * relativeVelocity.sqrMagnitude,
                efficiency,
                1f);
            if (crack != null)
            {
                crack.HandleImpact(context);
            }
            else if (fragment != null)
            {
                fragment.ConsumeImpact(new ImpactEnergyContext(
                    impactPosition,
                    relativeVelocity,
                    context.ImpactEnergy,
                    efficiency,
                    0f));
            }
            if (fragment != null && targetBody != null && targetBody.bodyType == RigidbodyType2D.Dynamic)
            {
                targetBody.AddForce(relativeVelocity * mass, ForceMode2D.Impulse);
            }
        }
    }
}

namespace GlassShooter.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class AdditionalWeaponController : MonoBehaviour
    {
        private const float ShotgunSpreadDegrees = 30f;
        private readonly List<BouncingProjectile> bouncingProjectiles = new();

        private PlayerShooterController player;
        private GrowthStatusComponent growth;
        private HammerPendulumWeapon hammer;
        private float nextShotgunTime;
        private float nextSineTime;

        public void Configure(PlayerShooterController owner, GrowthStatusComponent status)
        {
            player = owner;
            growth = status;
            EnsurePersistentWeapons();
        }

        private void Update()
        {
            if (player == null || growth == null || player.ProjectilePrefab == null ||
                player.BulletStatus == null || !Input.GetKey(KeyCode.Space))
            {
                return;
            }

            if (growth.GetLevel(GrowthStatId.ShotgunUnlock) > 0 && Time.time >= nextShotgunTime)
            {
                FireShotgun();
                float rate = growth.GetValue(GrowthStatId.ShotgunFireRate);
                nextShotgunTime = Time.time + (rate > 0f ? 1f / rate : 5f);
            }
            if (growth.GetLevel(GrowthStatId.SineUnlock) > 0 && Time.time >= nextSineTime)
            {
                FireSineProjectile();
                float rate = growth.GetValue(GrowthStatId.SineFireRate);
                nextSineTime = Time.time + (rate > 0f ? 1f / rate : 1f);
            }
        }

        private void OnDestroy()
        {
            if (hammer != null)
            {
                Destroy(hammer.gameObject);
            }
            foreach (BouncingProjectile projectile in bouncingProjectiles)
            {
                if (projectile != null)
                {
                    Destroy(projectile.gameObject);
                }
            }
            bouncingProjectiles.Clear();
        }

        private void EnsurePersistentWeapons()
        {
            if (player == null || growth == null)
            {
                return;
            }

            if (growth.GetLevel(GrowthStatId.HammerUnlock) > 0)
            {
                if (hammer == null)
                {
                    hammer = HammerPendulumWeapon.Create(player);
                }
                hammer.Configure(
                    SharedMass * growth.GetValue(GrowthStatId.HammerMassRatio),
                    growth.GetValue(GrowthStatId.HammerReach),
                    growth.GetValue(GrowthStatId.HammerDrag),
                    SharedEfficiency);
            }
            else if (hammer != null)
            {
                Destroy(hammer.gameObject);
                hammer = null;
            }

            bouncingProjectiles.RemoveAll(projectile => projectile == null);
            int desiredCount = growth.GetLevel(GrowthStatId.BounceUnlock) > 0
                ? Mathf.Clamp(Mathf.RoundToInt(growth.GetValue(GrowthStatId.BounceCount)), 1, 3)
                : 0;
            while (bouncingProjectiles.Count > desiredCount)
            {
                int last = bouncingProjectiles.Count - 1;
                Destroy(bouncingProjectiles[last].gameObject);
                bouncingProjectiles.RemoveAt(last);
            }
            while (bouncingProjectiles.Count < desiredCount)
            {
                int index = bouncingProjectiles.Count;
                float angle = desiredCount > 1 ? 360f * index / desiredCount : 0f;
                BouncingProjectile projectile = SpawnBouncingProjectile(
                    Quaternion.Euler(0f, 0f, angle) * Vector2.up);
                if (projectile == null)
                {
                    break;
                }
                bouncingProjectiles.Add(projectile);
            }

            float mass = SharedMass * growth.GetValue(GrowthStatId.BounceMassRatio);
            float speed = growth.GetValue(GrowthStatId.BounceSpeed);
            foreach (BouncingProjectile projectile in bouncingProjectiles)
            {
                projectile.ApplyStatus(mass, speed, SharedEfficiency);
            }
        }

        private void FireShotgun()
        {
            int count = Mathf.Clamp(
                Mathf.RoundToInt(growth.GetValue(GrowthStatId.ShotgunPelletCount)), 1, 8);
            float mass = SharedMass * growth.GetValue(GrowthStatId.ShotgunPelletMassRatio);
            float speed = growth.GetValue(GrowthStatId.ShotgunSpeed);
            float rate = growth.GetValue(GrowthStatId.ShotgunFireRate);
            for (int i = 0; i < count; i++)
            {
                float angle = count == 1
                    ? 0f
                    : -ShotgunSpreadDegrees * 0.5f + ShotgunSpreadDegrees * i / (count - 1f);
                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * Vector2.up;
                Projectile projectile = Instantiate(
                    player.ProjectilePrefab, player.FireOrigin, Quaternion.Euler(0f, 0f, angle));
                ConfigureStatus(projectile.gameObject, mass, speed, rate, direction);
            }
        }

        private void FireSineProjectile()
        {
            Projectile projectile = Instantiate(
                player.ProjectilePrefab, player.FireOrigin, Quaternion.identity);
            projectile.enabled = false;
            float speed = growth.GetValue(GrowthStatId.SineSpeed);
            BulletStatus status = ConfigureStatus(
                projectile.gameObject,
                SharedMass * growth.GetValue(GrowthStatId.SineMassRatio),
                speed,
                growth.GetValue(GrowthStatId.SineFireRate),
                Vector2.up);
            SineWaveProjectile sine = projectile.gameObject.AddComponent<SineWaveProjectile>();
            sine.Initialize(
                status, player.FireOrigin, Vector2.up, speed,
                Random.Range(0.1f, 1.5f), Random.Range(0.1f, 2f), projectile.DestroyY);
        }

        private BouncingProjectile SpawnBouncingProjectile(Vector2 direction)
        {
            if (player.ProjectilePrefab == null)
            {
                return null;
            }
            Projectile projectile = Instantiate(
                player.ProjectilePrefab, player.FireOrigin, Quaternion.identity);
            projectile.enabled = false;
            BulletStatus status = ConfigureStatus(
                projectile.gameObject,
                SharedMass * growth.GetValue(GrowthStatId.BounceMassRatio),
                growth.GetValue(GrowthStatId.BounceSpeed), 0f, direction);
            BouncingProjectile bounce = projectile.gameObject.AddComponent<BouncingProjectile>();
            bounce.Initialize(status, direction, player);
            return bounce;
        }

        private BulletStatus ConfigureStatus(
            GameObject target, float mass, float speed, float rate, Vector2 direction)
        {
            BulletStatus status = target.TryGetComponent(out BulletStatus existing)
                ? existing
                : target.AddComponent<BulletStatus>();
            status.ApplyGrowthStatus(mass, speed, rate, SharedEfficiency, 1f);
            status.SetCurrentVelocity(direction.normalized * speed);
            return status;
        }

        private float SharedMass => player?.BulletStatus != null ? player.BulletStatus.Mass : 0.1f;
        private float SharedEfficiency => player?.BulletStatus != null
            ? player.BulletStatus.CrackConversionEfficiency
            : 0.05f;
    }

    internal abstract class ConsumedWeaponProjectile : MonoBehaviour
    {
        protected Rigidbody2D Body { get; private set; }
        protected BulletStatus Status { get; private set; }
        private bool consumed;

        protected void InitializeImpactProjectile(BulletStatus status)
        {
            Status = status;
            Body = GetComponent<Rigidbody2D>();
        }

        protected bool TryConsume(Collider2D other, Vector2 worldPosition)
        {
            if (consumed || other == null || Status == null)
            {
                return false;
            }
            CrackProcessingComponent crack = other.GetComponentInParent<CrackProcessingComponent>();
            GlassFragment fragment = crack == null ? other.GetComponentInParent<GlassFragment>() : null;
            if (crack == null && fragment == null)
            {
                return false;
            }

            consumed = true;
            foreach (Collider2D ownCollider in GetComponentsInChildren<Collider2D>())
            {
                ownCollider.enabled = false;
            }
            ImpactEnergyContext context = ImpactEnergyContext.FromBullet(worldPosition, Status);
            if (crack != null)
            {
                crack.HandleImpact(context);
            }
            else
            {
                fragment.ConsumeImpact(context);
            }
            Destroy(gameObject);
            return true;
        }
    }

    [DisallowMultipleComponent]
    internal sealed class SineWaveProjectile : ConsumedWeaponProjectile
    {
        private Vector2 origin;
        private Vector2 forward;
        private Vector2 right;
        private float forwardSpeed;
        private float amplitude;
        private float angularFrequency;
        private float destroyY;
        private float elapsed;
        private bool initialized;

        public void Initialize(
            BulletStatus status, Vector2 spawnOrigin, Vector2 forwardDirection,
            float speed, float waveAmplitude, float frequency, float upperDestroyY)
        {
            InitializeImpactProjectile(status);
            origin = spawnOrigin;
            forward = forwardDirection.sqrMagnitude > 0.000001f
                ? forwardDirection.normalized
                : Vector2.up;
            right = new Vector2(forward.y, -forward.x);
            forwardSpeed = Mathf.Max(0f, speed);
            amplitude = Mathf.Max(0f, waveAmplitude);
            angularFrequency = Mathf.PI * 2f * Mathf.Max(0f, frequency);
            destroyY = upperDestroyY;
            initialized = true;
        }

        private void FixedUpdate()
        {
            if (!initialized || Body == null || Status == null)
            {
                return;
            }
            elapsed += Time.fixedDeltaTime;
            float phase = angularFrequency * elapsed;
            float lateralOffset = amplitude * Mathf.Sin(phase);
            float lateralVelocity = amplitude * angularFrequency * Mathf.Cos(phase);
            Vector2 velocity = forward * forwardSpeed + right * lateralVelocity;
            Vector2 position = origin + forward * forwardSpeed * elapsed + right * lateralOffset;
            Status.SetCurrentVelocity(velocity);
            Body.linearVelocity = velocity;
            Body.MovePosition(position);
            if (position.y >= destroyY)
            {
                Destroy(gameObject);
            }
        }

        private void OnTriggerEnter2D(Collider2D other) => TryConsume(other, transform.position);

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Vector2 point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : (Vector2)transform.position;
            TryConsume(collision.collider, point);
        }
    }
}
