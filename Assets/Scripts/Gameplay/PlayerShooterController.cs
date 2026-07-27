using PolygonRendering.Input;
using UnityEngine;
using System.Collections.Generic;

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
        [SerializeField, Min(0f)] private float collisionVelocityRecovery = 1f;

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
        private Vector2 combinedMovementOverrideVelocity;
        private Vector2 lastCommandedVelocity;
        private float nextFireTime;
        private LineRenderer lr;
        private PolygonCollider2D hitbox;
        private Vector2[][] baseHitboxPaths;
        private SmallLightComponent smallLight;

        public Vector2 MoveLimitMin => Movelimitmin;
        public Vector2 MoveLimitMax => Movelimitmax;
        public BulletStatus BulletStatus => bulletStatus;
        public Projectile ProjectilePrefab => projectilePrefab;
        public float MoveSpeed => moveSpeed;
        public float Mass => playerRigidbody != null ? playerRigidbody.mass : 0f;
        public float FireInterval => bulletStatus != null && bulletStatus.FireRate > 0f
            ? 1f / bulletStatus.FireRate
            : fireInterval;

        /// <summary>成長画面で確定したプレイヤーステータスを反映します。</summary>
        public void ApplyGrowthStatus(
            float newMoveSpeed,
            float newFireInterval,
            float hitboxScale,
            float newMass,
            bool smallLightUnlocked,
            float smallLightRange,
            float smallLightAngle,
            float smallLightLinearMultiplierPerSecond)
        {
            moveSpeed = Mathf.Max(0f, newMoveSpeed);
            fireInterval = Mathf.Max(0.01f, newFireInterval);
            ApplyHitboxScale(hitboxScale);
            if (playerRigidbody == null)
            {
                playerRigidbody = GetComponent<Rigidbody2D>();
            }
            playerRigidbody.mass = Mathf.Max(0.0001f, newMass);
            EnsureSmallLight();
            smallLight.Configure(
                smallLightUnlocked,
                smallLightRange,
                smallLightAngle,
                smallLightLinearMultiplierPerSecond);
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
            EnsureSmallLight();
            CreateBackgroundGrid();

            GameObject child = new GameObject("LineRenderer");
            child.transform.SetParent(transform);
            child.transform.localPosition = Vector3.zero;
            lr = child.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            RenderMovementLimit();
        }

        private void Update()
        {
            if (Input.GetKey(KeyCode.Space) && Time.time >= nextFireTime)
            {
                Fire();
            }

#if UNITY_EDITOR
            Debug_impactFromMouse();
#endif
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

#if UNITY_EDITOR
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
#endif

        private void Move()
        {
            combinedMovementOverrideVelocity = Vector2.zero;

            Vector2 inputDirection = inputState.ArrowDirection;
            if (inputDirection.sqrMagnitude > 1f)
            {
                inputDirection.Normalize();
            }

            Vector2 inputVelocity = inputDirection * moveSpeed;

            // 速度を直接上書きすると、直前の物理ステップで受けた衝突反動が消える。
            // 前回指示した速度との差を物理由来の速度として残し、入力操作とは別に減衰させる。
            Vector2 collisionVelocity =
                playerRigidbody.linearVelocity - lastCommandedVelocity;
            collisionVelocity = Vector2.MoveTowards(
                collisionVelocity,
                Vector2.zero,
                collisionVelocityRecovery * Time.fixedDeltaTime);

            Vector2 nextVelocity = inputVelocity + collisionVelocity;
            ClampMovementToBounds(ref nextVelocity);
            lastCommandedVelocity = nextVelocity;
            playerRigidbody.linearVelocity = nextVelocity;
        }

        /// <summary>
        /// 不可侵領域など、入力より優先する効果の速度を合成して反映します。
        /// 合成値は物理フレームごとの通常移動処理で初期化されます。
        /// </summary>
        public void AddMovementOverrideVelocity(Vector2 velocity)
        {
            combinedMovementOverrideVelocity += velocity;
            playerRigidbody.linearVelocity = combinedMovementOverrideVelocity;
        }

        private void ClampMovementToBounds(ref Vector2 velocity)
        {
            Vector2 position = playerRigidbody.position;
            Vector2 clampedPosition = new Vector2(
                Mathf.Clamp(position.x, Movelimitmin.x, Movelimitmax.x),
                Mathf.Clamp(position.y, Movelimitmin.y, Movelimitmax.y));

            if (position != clampedPosition)
            {
                playerRigidbody.position = clampedPosition;
                position = clampedPosition;
            }

            if ((position.x <= Movelimitmin.x && velocity.x < 0f) ||
                (position.x >= Movelimitmax.x && velocity.x > 0f))
            {
                velocity.x = 0f;
            }

            if ((position.y <= Movelimitmin.y && velocity.y < 0f) ||
                (position.y >= Movelimitmax.y && velocity.y > 0f))
            {
                velocity.y = 0f;
            }
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

        private void EnsureSmallLight()
        {
            if (smallLight != null)
            {
                return;
            }

            smallLight = GetComponentInChildren<SmallLightComponent>(true);
            if (smallLight != null)
            {
                return;
            }

            GameObject field = new GameObject("SmallLight");
            field.transform.SetParent(transform, false);
            smallLight = field.AddComponent<SmallLightComponent>();
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

            nextFireTime = Time.time + FireInterval;
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

    /// <summary>
    /// プレイヤー前方の扇形領域へ触れている、固定されていないガラスを継続縮小します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class SmallLightComponent : MonoBehaviour
    {
        private const int MaximumArcSegments = 64;
        private const float DegreesPerArcSegment = 2f;

        private readonly HashSet<CrackProcessingComponent> processedTargets =
            new HashSet<CrackProcessingComponent>();

        private PolygonCollider2D fieldCollider;
        private Mesh fieldMesh;
        private MeshRenderer fieldRenderer;
        private Material fieldMaterial;
        private float linearMultiplierPerSecond = 1f;
        private float currentRange = -1f;
        private float currentAngle = -1f;

        public void Configure(
            bool unlocked,
            float range,
            float angleDegrees,
            float newLinearMultiplierPerSecond)
        {
            EnsureComponents();

            float safeRange = Mathf.Max(0f, range);
            float safeAngle = Mathf.Clamp(angleDegrees, 0f, 360f);
            linearMultiplierPerSecond = Mathf.Clamp01(newLinearMultiplierPerSecond);

            bool geometryChanged =
                !Mathf.Approximately(currentRange, safeRange) ||
                !Mathf.Approximately(currentAngle, safeAngle);
            currentRange = safeRange;
            currentAngle = safeAngle;
            if (geometryChanged)
            {
                RebuildFieldGeometry();
            }

            bool active = unlocked && safeRange > 0f && safeAngle > 0f;
            fieldCollider.enabled = active;
            fieldRenderer.enabled = active;
        }

        private void Awake()
        {
            EnsureComponents();
            fieldCollider.enabled = false;
            fieldRenderer.enabled = false;
        }

        private void FixedUpdate()
        {
            processedTargets.Clear();
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (!fieldCollider.enabled ||
                linearMultiplierPerSecond >= 1f ||
                other == null)
            {
                return;
            }

            CrackProcessingComponent target =
                other.GetComponentInParent<CrackProcessingComponent>();
            if (target == null)
            {
                return;
            }

            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            if (body == null ||
                body.bodyType != RigidbodyType2D.Dynamic ||
                body.constraints != RigidbodyConstraints2D.None)
            {
                return;
            }

            if (!processedTargets.Add(target))
            {
                return;
            }

            target.ApplyContinuousSizeMultiplier(
                linearMultiplierPerSecond,
                Time.fixedDeltaTime);
        }

        private void EnsureComponents()
        {
            if (fieldCollider == null)
            {
                fieldCollider = GetComponent<PolygonCollider2D>();
                if (fieldCollider == null)
                {
                    fieldCollider = gameObject.AddComponent<PolygonCollider2D>();
                }
                fieldCollider.isTrigger = true;
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = gameObject.AddComponent<MeshFilter>();
            }
            if (fieldMesh == null)
            {
                fieldMesh = new Mesh
                {
                    name = "SmallLightFieldMesh"
                };
                filter.sharedMesh = fieldMesh;
            }

            if (fieldRenderer == null)
            {
                fieldRenderer = GetComponent<MeshRenderer>();
                if (fieldRenderer == null)
                {
                    fieldRenderer = gameObject.AddComponent<MeshRenderer>();
                }
                fieldRenderer.sortingOrder = -10;
            }

            if (fieldMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    fieldMaterial = new Material(shader)
                    {
                        name = "SmallLightFieldMaterial",
                        color = new Color(1f, 0.92f, 0.35f, 0.16f)
                    };
                    fieldRenderer.sharedMaterial = fieldMaterial;
                }
            }
        }

        private void RebuildFieldGeometry()
        {
            int segmentCount = Mathf.Clamp(
                Mathf.CeilToInt(currentAngle / DegreesPerArcSegment),
                2,
                MaximumArcSegments);
            Vector2[] points = new Vector2[segmentCount + 2];
            points[0] = Vector2.zero;

            float halfAngle = currentAngle * 0.5f;
            for (int segment = 0; segment <= segmentCount; segment++)
            {
                float t = segment / (float)segmentCount;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t) * Mathf.Deg2Rad;
                points[segment + 1] = new Vector2(
                    Mathf.Sin(angle) * currentRange,
                    Mathf.Cos(angle) * currentRange);
            }
            fieldCollider.points = points;

            Vector3[] vertices = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                vertices[i] = points[i];
            }

            int[] triangles = new int[segmentCount * 3];
            for (int segment = 0; segment < segmentCount; segment++)
            {
                int triangleIndex = segment * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = segment + 1;
                triangles[triangleIndex + 2] = segment + 2;
            }

            fieldMesh.Clear();
            fieldMesh.vertices = vertices;
            fieldMesh.triangles = triangles;
            fieldMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (fieldMaterial != null)
            {
                Destroy(fieldMaterial);
            }
            if (fieldMesh != null)
            {
                Destroy(fieldMesh);
            }
        }
    }
}
