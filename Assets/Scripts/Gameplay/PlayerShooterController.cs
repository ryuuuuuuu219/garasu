using PolygonRendering.Input;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// プレイヤーの左右移動と弾の発射を担当します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeyboardInputState))]
    public sealed class PlayerShooterController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 7f;
        [SerializeField] private float horizontalLimit = 8.2f;

        [Header("Shooting")]
        [SerializeField] private Projectile projectilePrefab = null;
        [SerializeField] private Transform firePoint = null;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.16f;

        private KeyboardInputState inputState;
        private float nextFireTime;

        private void Awake()
        {
            inputState = GetComponent<KeyboardInputState>();
        }

        private void Update()
        {
            Move();

            if ((UnityEngine.Input.GetKey(KeyCode.Space) || inputState.LeftShiftActive)
                && Time.time >= nextFireTime)
            {
                Fire();
            }
        }

        private void Move()
        {
            Vector3 position = transform.position;
            position.x += inputState.ArrowDirection.x * moveSpeed * Time.deltaTime;
            position.x = Mathf.Clamp(position.x, -horizontalLimit, horizontalLimit);
            transform.position = position;
        }

        private void Fire()
        {
            if (projectilePrefab == null)
            {
                return;
            }

            Vector3 spawnPosition = firePoint != null ? firePoint.position : transform.position;
            Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
            nextFireTime = Time.time + fireInterval;
        }
    }
}
