using GlassShooter.Gameplay;
using UnityEngine;

namespace Gameplay
{
    /// <summary>プレイヤーへ向けて妨害用のひし形ガラスを一定間隔で射出します。</summary>
    [DisallowMultipleComponent]
    public sealed class battery_A : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float fireInterval = 5f;
        [SerializeField, Min(0f)] private float spawnDistance = 2f;
        [SerializeField, Min(0f)] private float projectileSpeed = 0.6f;

        private enemyspowner spawner;
        private PlayerShooterController player;
        private float nextFireTime;

        internal void Initialize(enemyspowner owner)
        {
            spawner = owner;
            nextFireTime = Time.time + fireInterval;
            ResolvePlayer();
        }

        private void OnEnable()
        {
            nextFireTime = Time.time + fireInterval;
        }

        private void Update()
        {
            if (Time.time < nextFireTime)
            {
                return;
            }

            nextFireTime = Time.time + fireInterval;
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

            Vector2 direction = (Vector2)player.transform.position -
                (Vector2)transform.position;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                direction = Vector2.down;
            }
            else
            {
                direction.Normalize();
            }

            Vector2 spawnPosition =
                (Vector2)transform.position + direction * spawnDistance;
            float zRotationDegrees =
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;

            spawner.SpawnInterferenceObject(
                enemyspowner.GetDiamondProjectileOutline(),
                spawnPosition,
                zRotationDegrees,
                direction * projectileSpeed);
        }

        private void ResolvePlayer()
        {
            player = FindAnyObjectByType<PlayerShooterController>();
        }
    }
}
