using GlassShooter.Gameplay;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// プレイヤー方向を基準に4発の純粋追尾弾を一定間隔で斉射します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class battery_B : MonoBehaviour
    {
        private static readonly float[] VolleyAngles =
        {
            -120f,
            -45f,
            45f,
            120f
        };

        [SerializeField, Min(0.01f)]
        private float fireInterval = 8f;

        [SerializeField, Min(0f)]
        private float initialFireDelay = 1f;

        [SerializeField, Min(0f)]
        private float spawnDistance = 2f;

        [SerializeField, Min(0.01f)]
        private float projectileSpeed = 6f;

        [SerializeField, Min(0.01f)]
        private float trailHalfWidth = 0.1f;

        [SerializeField, Min(0.001f)]
        private float trailSampleFrequency = 5f;

        [SerializeField, Min(0f)]
        private float maximumTurnRate = 135f;

        private enemyspowner spawner;
        private PlayerShooterController player;
        private float nextFireTime;

        internal void Initialize(enemyspowner owner)
        {
            spawner = owner;
            ResolvePlayer();
            nextFireTime = Time.time + initialFireDelay;
        }

        private void OnEnable()
        {
            nextFireTime = Time.time + initialFireDelay;
        }

        private void Update()
        {
            if (Time.time < nextFireTime)
            {
                return;
            }

            if (spawner == null)
            {
                spawner = GetComponentInParent<enemyspowner>();
            }
            if (player == null)
            {
                ResolvePlayer();
            }
            if (spawner == null || player == null)
            {
                return;
            }

            FireVolley();
            nextFireTime = Time.time + fireInterval;
        }

        private void FireVolley()
        {
            Vector2 toPlayer =
                (Vector2)player.transform.position - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude <= Mathf.Epsilon)
            {
                toPlayer = Vector2.down;
            }
            else
            {
                toPlayer.Normalize();
            }

            Vector2 spawnPosition =
                (Vector2)transform.position + toPlayer * spawnDistance;
            for (int i = 0; i < VolleyAngles.Length; i++)
            {
                Vector2 launchDirection =
                    Rotate(toPlayer, VolleyAngles[i]).normalized;
                SpawnHomingProjectile(spawnPosition, launchDirection);
            }
        }

        private void SpawnHomingProjectile(
            Vector2 spawnPosition,
            Vector2 launchDirection)
        {
            float zRotationDegrees =
                Mathf.Atan2(launchDirection.y, launchDirection.x) *
                Mathf.Rad2Deg - 90f;
            GameObject projectile = spawner.SpawnInterferenceObject(
                enemyspowner.GetDiamondProjectileOutline(),
                spawnPosition,
                zRotationDegrees,
                launchDirection * projectileSpeed);
            if (projectile == null)
            {
                return;
            }

            Rigidbody2D body = projectile.GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;

            PurePursuitHomingProjectile homing =
                projectile.AddComponent<PurePursuitHomingProjectile>();
            homing.Initialize(
                player.transform,
                projectileSpeed,
                maximumTurnRate);

            ProjectileTriangleTrail trail =
                projectile.AddComponent<ProjectileTriangleTrail>();
            trail.ConfigureSampling(trailHalfWidth, trailSampleFrequency);
            trail.EnableFallingGlassTriangles();
            trail.SetVelocityProvider(() =>
                body != null ? body.linearVelocity : Vector2.zero);
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                vector.x * cosine - vector.y * sine,
                vector.x * sine + vector.y * cosine);
        }

        private void ResolvePlayer()
        {
            player = FindAnyObjectByType<PlayerShooterController>();
        }

        private void OnValidate()
        {
            fireInterval = Mathf.Max(0.01f, fireInterval);
            initialFireDelay = Mathf.Max(0f, initialFireDelay);
            spawnDistance = Mathf.Max(0f, spawnDistance);
            projectileSpeed = Mathf.Max(0.01f, projectileSpeed);
            trailHalfWidth = Mathf.Max(0.01f, trailHalfWidth);
            trailSampleFrequency = Mathf.Max(0.001f, trailSampleFrequency);
            maximumTurnRate = Mathf.Max(0f, maximumTurnRate);
        }
    }
}
