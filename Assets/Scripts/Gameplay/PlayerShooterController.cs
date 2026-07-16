using PolygonRendering.Input;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// プレイヤーの移動と統合破砕弾の発射を担当します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeyboardInputState))]
    public sealed class PlayerShooterController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 7f;

        [Header("Shooting")]
        [SerializeField] private Projectile projectilePrefab = null;
        [SerializeField] private Transform firePoint = null;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.16f;
        [SerializeField] private BulletStatus bulletStatus;

        [SerializeField] private Vector2 Movelimitmin;
        [SerializeField] private Vector2 Movelimitmax;

        private KeyboardInputState inputState;
        private float nextFireTime;
        private LineRenderer lr;
        private PolygonCollider2D hitbox;
        private Vector2[][] baseHitboxPaths;

        public Vector2 MoveLimitMin => Movelimitmin;
        public Vector2 MoveLimitMax => Movelimitmax;
        public BulletStatus BulletStatus => bulletStatus;
        public Projectile ProjectilePrefab => projectilePrefab;
        public float MoveSpeed => moveSpeed;
        public float FireInterval => fireInterval;

        /// <summary>成長画面で確定したプレイヤーステータスを反映します。</summary>
        public void ApplyGrowthStatus(float newMoveSpeed, float newFireInterval, float hitboxScale)
        {
            moveSpeed = Mathf.Max(0f, newMoveSpeed);
            fireInterval = Mathf.Max(0.01f, newFireInterval);
            ApplyHitboxScale(hitboxScale);
        }

        private void Awake()
        {
            inputState = GetComponent<KeyboardInputState>();
            CacheHitbox();

            GameObject child = new GameObject("LineRenderer");
            child.transform.SetParent(transform);
            child.transform.localPosition = Vector3.zero;
            lr = child.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
        }

        private void Update()
        {
            Move();
            RenderMovementLimit();

            if (inputState.SpaceDown && Time.time >= nextFireTime)
            {
                Fire();
            }

            Debug_impactFromMouse();

        }

        void Debug_impactFromMouse()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 mousePosition = Input.mousePosition;
                mousePosition.z = 0f; // Set the z-coordinate to 0 for 2D
                Vector3 worldPosition = Camera.main.ScreenToWorldPoint(mousePosition);
                worldPosition.z = 0f; // Set the z-coordinate to 0 for 2D
                if (projectilePrefab == null || bulletStatus == null)
                {
                    return;
                }
                Projectile projectile = Instantiate(
                    projectilePrefab,
                    worldPosition,
                    Quaternion.identity);
                BulletStatus copy = projectile.TryGetComponent(out BulletStatus existingStatus)
                    ? existingStatus
                    : projectile.gameObject.AddComponent<BulletStatus>();
                copy.CopyFrom(bulletStatus);
            }
        }

        private void Move()
        {
            Vector3 position = transform.position;
            position += (Vector3)inputState.ArrowDirection * moveSpeed * Time.deltaTime;
            position.x = Mathf.Clamp(position.x, Movelimitmin.x, Movelimitmax.x);
            position.y = Mathf.Clamp(position.y, Movelimitmin.y, Movelimitmax.y);
            transform.position = position;
        }

        private void RenderMovementLimit()
        {
            lr.positionCount = 4;
            lr.SetPosition(0, new Vector3(Movelimitmin.x, Movelimitmin.y, 0f));
            lr.SetPosition(1, new Vector3(Movelimitmax.x, Movelimitmin.y, 0f));
            lr.SetPosition(2, new Vector3(Movelimitmax.x, Movelimitmax.y, 0f));
            lr.SetPosition(3, new Vector3(Movelimitmin.x, Movelimitmax.y, 0f));
            lr.loop = true;
            lr.startColor = Color.white;
            lr.endColor = Color.white;
            lr.useWorldSpace = true;
        }

        private void CacheHitbox()
        {
            hitbox = GetComponent<PolygonCollider2D>();
            if (hitbox == null)
            {
                return;
            }

            baseHitboxPaths = new Vector2[hitbox.pathCount][];
            for (int pathIndex = 0; pathIndex < hitbox.pathCount; pathIndex++)
            {
                baseHitboxPaths[pathIndex] = hitbox.GetPath(pathIndex);
            }
        }

        private void ApplyHitboxScale(float scale)
        {
            if (hitbox == null || baseHitboxPaths == null)
            {
                CacheHitbox();
            }
            if (hitbox == null || baseHitboxPaths == null)
            {
                return;
            }

            float safeScale = Mathf.Max(0.0001f, scale);
            for (int pathIndex = 0; pathIndex < baseHitboxPaths.Length; pathIndex++)
            {
                Vector2[] scaledPath = new Vector2[baseHitboxPaths[pathIndex].Length];
                for (int pointIndex = 0; pointIndex < scaledPath.Length; pointIndex++)
                {
                    scaledPath[pointIndex] = baseHitboxPaths[pathIndex][pointIndex] * safeScale;
                }
                hitbox.SetPath(pathIndex, scaledPath);
            }
        }

        private void Fire()
        {
            if (projectilePrefab == null || bulletStatus == null)
            {
                return;
            }

            Vector3 spawnPosition = firePoint != null
                ? firePoint.position
                : transform.position;
            int shotCount = Mathf.Max(1, bulletStatus.SimultaneousShotCount);
            for (int shotIndex = 0; shotIndex < shotCount; shotIndex++)
            {
                SpawnProjectile(spawnPosition);
            }

            nextFireTime = Time.time + fireInterval;
        }

        private void SpawnProjectile(Vector3 spawnPosition)
        {
            Projectile projectile = Instantiate(
                projectilePrefab,
                spawnPosition,
                Quaternion.identity);
            BulletStatus copy = projectile.TryGetComponent(out BulletStatus existingStatus)
                ? existingStatus
                : projectile.gameObject.AddComponent<BulletStatus>();
            copy.CopyFrom(bulletStatus);
        }
    }
}
