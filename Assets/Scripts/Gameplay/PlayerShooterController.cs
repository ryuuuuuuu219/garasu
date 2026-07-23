using PolygonRendering.Input;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// プレイヤーの移動と統合破砕弾の発射を担当します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeyboardInputState))]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerShooterController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 7f;

        [Header("Shooting")]
        [SerializeField] private Projectile projectilePrefab = null;
        [SerializeField] private Transform firePoint = null;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.16f;
        [SerializeField] private BulletStatus bulletStatus;

        [SerializeField] private Vector2 Movelimitmin = new Vector2 (-15f, -8.5f);
        [SerializeField] private Vector2 Movelimitmax = new Vector2 (15f, 8.5f);

        [SerializeField] Camera mainCam;

        [Header("Background Grid")]
        [SerializeField, Min(0.1f)] private float gridSpacing = 2f;
        [SerializeField, Min(0.001f)] private float gridLineWidth = 0.025f;
        [SerializeField, Range(0f, 1f)] private float gridAlpha = 1f;

        private KeyboardInputState inputState;
        private Rigidbody2D playerRigidbody;
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
            mainCam = Camera.main;

            inputState = GetComponent<KeyboardInputState>();
            playerRigidbody = GetComponent<Rigidbody2D>();
            playerRigidbody.bodyType = RigidbodyType2D.Dynamic;
            playerRigidbody.gravityScale = 0f;
            playerRigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            playerRigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            playerRigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            CacheHitbox();
            CreateBackgroundGrid();

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
            RenderMovementLimit();

            if (inputState.SpaceDown && Time.time >= nextFireTime)
            {
                Fire();
            }

            Debug_impactFromMouse();
            chaseCamera();

        }

        private void FixedUpdate()
        {
            Move();
        }

        public Vector3 cameraCenter=Vector3.zero;
        void chaseCamera()
        {
            if (mainCam == null)
            {
                return;
            }

            Vector3 vector = transform.position-cameraCenter;
            Vector3 cameraPos = cameraCenter + vector * 0.6f;
            cameraPos.z = mainCam.transform.position.z;

            mainCam.transform.position = cameraPos;
        }

        private void CreateBackgroundGrid()
        {
            GameObject gridRoot = new GameObject("BackgroundGrid");
            gridRoot.transform.SetParent(transform);
            gridRoot.transform.localPosition = Vector3.zero;

            Shader spriteShader = Shader.Find("Sprites/Default");
            Material gridMaterial = spriteShader != null
                ? new Material(spriteShader)
                : null;
            Color gridColor = new Color(1f, 1f, 1f, gridAlpha);

            Vector2 gridMin = Movelimitmin;
            Vector2 gridMax = Movelimitmax;
            if (mainCam != null && mainCam.orthographic)
            {
                float verticalMargin = mainCam.orthographicSize;
                float horizontalMargin = verticalMargin * mainCam.aspect;
                gridMin -= new Vector2(horizontalMargin, verticalMargin);
                gridMax += new Vector2(horizontalMargin, verticalMargin);
            }

            float firstX = Mathf.Floor(gridMin.x / gridSpacing) * gridSpacing;
            float lastX = Mathf.Ceil(gridMax.x / gridSpacing) * gridSpacing;
            for (float x = firstX; x <= lastX + Mathf.Epsilon; x += gridSpacing)
            {
                CreateGridLine(
                    gridRoot.transform,
                    gridMaterial,
                    gridColor,
                    new Vector3(x, gridMin.y, 0f),
                    new Vector3(x, gridMax.y, 0f));
            }

            float firstY = Mathf.Floor(gridMin.y / gridSpacing) * gridSpacing;
            float lastY = Mathf.Ceil(gridMax.y / gridSpacing) * gridSpacing;
            for (float y = firstY; y <= lastY + Mathf.Epsilon; y += gridSpacing)
            {
                CreateGridLine(
                    gridRoot.transform,
                    gridMaterial,
                    gridColor,
                    new Vector3(gridMin.x, y, 0f),
                    new Vector3(gridMax.x, y, 0f));
            }
        }

        private void CreateGridLine(
            Transform parent,
            Material material,
            Color color,
            Vector3 start,
            Vector3 end)
        {
            GameObject lineObject = new GameObject("GridLine");
            lineObject.transform.SetParent(parent);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = gridLineWidth;
            line.endWidth = gridLineWidth;
            line.startColor = color;
            line.endColor = color;
            line.useWorldSpace = true;
            line.sortingOrder = -100;
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
            Vector2 position = playerRigidbody.position;
            position += inputState.ArrowDirection * moveSpeed * Time.fixedDeltaTime;
            position.x = Mathf.Clamp(position.x, Movelimitmin.x, Movelimitmax.x);
            position.y = Mathf.Clamp(position.y, Movelimitmin.y, Movelimitmax.y);
            playerRigidbody.MovePosition(position);
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
            SpawnProjectile(spawnPosition);

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
