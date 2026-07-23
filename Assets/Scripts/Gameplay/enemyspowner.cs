using GlassShooter.Gameplay;
using System.Collections;
using System.Collections.Generic;
using PolygonRendering;
using UnityEngine;

namespace Gameplay
{
    [AddComponentMenu("Scripts/Gameplay/Enemy Spawner")]
    [RequireComponent(typeof(BossAppearanceManager))]
    internal class enemyspowner : MonoBehaviour
    {
        private static readonly Vector2[] DiamondProjectileOutline =
        {
            new Vector2(0f, 0.5f),
            new Vector2(0.2f, 0f),
            new Vector2(0f, -0.5f),
            new Vector2(-0.2f, 0f)
        };

        public GlassStatus glassStatus;
        [Min(0)] public int difficulty = 1;

        [Header("出現時の残骸排除")]
        [SerializeField, Min(0.01f)] private float repulsionRadius = 12f;
        [SerializeField, Min(0f)] private float repulsionAcceleration = 30f;
        [SerializeField, Min(0.01f)] private float repulsionDuration = 1.5f;

        private BossAppearanceManager manager;
        private Coroutine delayedSpawn;
        private readonly List<EnemyDefeatComponent> trackedEnemies =
            new List<EnemyDefeatComponent>();

        private struct SpawnPattern
        {
            public float Time;
            public string EnemyType;
            public Vector2[] Positions;
            public bool IsActive;
        }

        public int manualSpawnDiffculty = 1;
        public bool manualTrigger = false;

        void TriggerManualSpawn()
        {
            if(!manualTrigger) return;
            manualTrigger = false;
            difficulty = manualSpawnDiffculty;
            SpawnEnemyForCurrentDifficulty();
        }

        // EnemyTypeに対応する外周頂点を、敵の中心を原点としたローカル座標で返します。
        private Vector2[] GetPositionsForPattern(string enemyType)
        {
            switch (enemyType)
            {
                case "Basic":
                    return new[]
                    {
                        new Vector2(-4f, -1.5f),
                        new Vector2(-4f, 1.5f),
                        new Vector2(4f, 1.5f),
                        new Vector2(4f, -1.5f)
                    };
                case "Boss_A":
                case "Boss_A_armor":
                    return CreateMultiLayerPositions(12, 3f, 0f, 12, 1f, 0f);
                case "Boss_A_core":
                case "battery_A":
                    return CreateRegularPolygonPositions(6, 1f, 0f);


                // 新しい形は、EnemyTypeのcaseと外周頂点をここへ追加します。
                default:
                    Debug.LogWarning($"Unknown EnemyType '{enemyType}'. Using Basic outline.", this);
                    return GetPositionsForPattern("Basic");
            }
        }

        private Vector2[] CreateRegularPolygonPositions(
            int vertexCount,
            float radius,
            float phaseDegrees)
        {
            Vector2[] positions = new Vector2[vertexCount];
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                float angleDegrees = phaseDegrees - 360f / vertexCount * vertexIndex;
                positions[vertexIndex] = GetPositionOnCircle(radius, angleDegrees);
            }

            return positions;
        }

        private Vector2[] CreateMultiLayerPositions(
            int outerVertexCount,
            float outerRadius,
            float outerPhaseDegrees,
            int innerVertexCount,
            float innerRadius,
            float innerPhaseDegrees)
        {
            Vector2[] positions = new Vector2[outerVertexCount + innerVertexCount + 4];
            positions[0] = new Vector2(0f, outerRadius);
            positions[1] = new Vector2(0f, innerRadius);

            for (int i = 2; i < 2 + innerVertexCount; i++)
            {
                int vertexIndex = i - 2;
                float angleDegrees = 360f / innerVertexCount * vertexIndex + innerPhaseDegrees;
                positions[i] = GetPositionOnCircle(innerRadius, angleDegrees);
            }

            positions[2 + innerVertexCount] = new Vector2(0f, innerRadius);
            positions[2 + innerVertexCount + 1] = new Vector2(0f, outerRadius);

            for (int i = 4 + innerVertexCount; i < positions.Length; i++)
            {
                int vertexIndex = i - 4 - innerVertexCount;
                float angleDegrees = -(360f / outerVertexCount * vertexIndex + outerPhaseDegrees);
                positions[i] = GetPositionOnCircle(outerRadius, angleDegrees);
            }

            return positions;
        }

        private Vector2 GetPositionOnCircle(float radius, float angleDegrees)
        {
            return Quaternion.Euler(0f, 0f, angleDegrees) * Vector3.up * radius;
        }

        private SpawnPattern[] _spawnPatterns;
        private float _timer;
        private int _currentPatternId = -1;
        private int _lastSpawnedEnemyId = -1;
        private bool _initialEnemySpawned;

        public int CurrentPatternId => _currentPatternId;
        public int LastSpawnedEnemyId => _lastSpawnedEnemyId;

        private void Awake()
        {
            manager = GetComponent<BossAppearanceManager>();
            if (manager == null)
            {
                manager = gameObject.AddComponent<BossAppearanceManager>();
            }

            PatternDefine();

            float previousTime = -1f;
            foreach (SpawnPattern pattern in _spawnPatterns)
            {
                if (pattern.Time < previousTime)
                {
                    Debug.LogError("SpawnPatterns are not in ascending order of time.", this);
                }

                previousTime = pattern.Time;
            }
        }

        private void PatternDefine()
        {
            _spawnPatterns = new[]
            {
                new SpawnPattern
                {
                    Time = 1f,
                    EnemyType = "Basic",
                    Positions = new[] { new Vector2(0f, 4f) },
                    IsActive = false
                },
                new SpawnPattern {
                    Time = 100f,
                    EnemyType = "Boss_A_core",
                    Positions = new[] { new Vector2(0f, 4f) },
                    IsActive = false
                    }

            };
        }

        private void Update()
        {
            TriggerManualSpawn();
            _timer += Time.deltaTime;

            if (!_initialEnemySpawned &&
                _spawnPatterns.Length > 0 &&
                _timer >= _spawnPatterns[0].Time)
            {
                _initialEnemySpawned = true;
                SpawnEnemyForCurrentDifficulty();
            }
        }

        private void SpawnEnemyForCurrentDifficulty()
        {
            if (delayedSpawn != null)
            {
                StopCoroutine(delayedSpawn);
                delayedSpawn = null;
            }

            ClearTrackedEnemies();
            _currentPatternId = difficulty;
            string enemyType = "Basic";
            if (difficulty % 5 == 0)
            {
                switch (difficulty / 5 % 2)
                {
                    case 0:
                        enemyType = "Boss_A_core";
                        break;
                    case 1:
                        enemyType = "battery_A";
                        break;
                }
            }
            GameObject enemy = SpawnEnemy(enemyType, new Vector2(0f, 4f), 0);
            TrackEnemy(enemy);
            CreateSpawnRepulsionField(enemy);
            Debug.Log($"Spawning {enemyType} at difficulty {difficulty}", this);
        }

        private void TrackEnemy(GameObject enemy)
        {
            if (enemy == null || !enemy.TryGetComponent(out EnemyDefeatComponent defeat))
            {
                Debug.LogError("Spawned enemy has no EnemyDefeatComponent.", this);
                return;
            }

            defeat.Defeated += OnEnemyDefeated;
            trackedEnemies.Add(defeat);
        }

        private void OnEnemyDefeated()
        {
            if (delayedSpawn != null)
            {
                return;
            }

            if (difficulty < int.MaxValue)
            {
                difficulty++;
            }

            ClearTrackedEnemies();
            delayedSpawn = StartCoroutine(SpawnEnemyAfterDefeatDelay());
        }

        private IEnumerator SpawnEnemyAfterDefeatDelay()
        {
            yield return new WaitForSeconds(1f);
            delayedSpawn = null;
            SpawnEnemyForCurrentDifficulty();
        }

        private void ClearTrackedEnemies()
        {
            for (int i = 0; i < trackedEnemies.Count; i++)
            {
                if (trackedEnemies[i] != null)
                {
                    trackedEnemies[i].Defeated -= OnEnemyDefeated;
                }
            }
            trackedEnemies.Clear();
        }

        private void OnDestroy()
        {
            if (delayedSpawn != null)
            {
                StopCoroutine(delayedSpawn);
                delayedSpawn = null;
            }
            ClearTrackedEnemies();
        }

        private void OnValidate()
        {
            difficulty = Mathf.Max(0, difficulty);
        }

        private GameObject SpawnEnemy(string enemyType, Vector2 localPosition, int positionIndex)
        {
            _lastSpawnedEnemyId++;
            Vector2[] outlinePoints = GetPositionsForPattern(enemyType);

            GameObject enemy = new GameObject($"{enemyType}_{positionIndex}");
            Init(enemy, outlinePoints);
            enemy.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);

            switch (enemyType)
            {
                case "Boss_A_core":
                    GameObject armor = SpawnEnemy("Boss_A_armor", localPosition, positionIndex);
                    armor.transform.SetParent(enemy.transform, true);
                    BossGlassComponent boss = enemy.AddComponent<BossGlassComponent>();
                    boss.AddModule(armor, 3f);
                    manager.apperdelay(enemy);
                    break;
                case "battery_A":
                    battery_A battery = enemy.AddComponent<battery_A>();
                    battery.Initialize(this);
                    manager.apperdelay(enemy);
                    break;
            }

            return enemy;
        }

        private void CreateSpawnRepulsionField(GameObject spawnedEnemy)
        {
            if (spawnedEnemy == null)
            {
                return;
            }

            GameObject fieldObject = new GameObject(
                $"{spawnedEnemy.name}_SpawnRepulsionField");
            fieldObject.transform.SetParent(transform, false);
            fieldObject.transform.position = spawnedEnemy.transform.position;

            EnemySpawnRepulsionField field =
                fieldObject.AddComponent<EnemySpawnRepulsionField>();
            field.Initialize(
                spawnedEnemy.transform,
                repulsionRadius,
                repulsionAcceleration,
                repulsionDuration);
        }

        public void Init(GameObject obj, Vector2[] outlinePoints)
        {
            InitializeGlassObject(obj, outlinePoints, true, false);
        }

        /// <summary>
        /// 指定形状の妨害用ガラスをワールド座標へ生成します。
        /// 初速を省略した場合は静止状態で生成されます。
        /// 破砕・落下しても資源報酬は発生しません。
        /// </summary>
        public GameObject SpawnInterferenceObject(
            Vector2[] outlinePoints,
            Vector2 worldPosition,
            float zRotationDegrees,
            Vector2 initialVelocity = default)
        {
            if (outlinePoints == null || outlinePoints.Length < 3)
            {
                Debug.LogError(
                    "Interference object requires at least three outline points.",
                    this);
                return null;
            }

            GameObject obj = new GameObject("EnemyInterferenceObject");
            Rigidbody2D body = obj.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            InitializeGlassObject(obj, outlinePoints, false, true);
            obj.transform.SetPositionAndRotation(
                new Vector3(worldPosition.x, worldPosition.y, 0f),
                Quaternion.Euler(0f, 0f, zRotationDegrees));

            GlassStatus status = obj.GetComponent<GlassStatus>();
            status.SetResourceRewardSuppressed(true);
            body.mass = status.CalculateMass(Mathf.Abs(CalculateSignedArea(outlinePoints)));
            body.linearVelocity = initialVelocity;
            return obj;
        }

        /// <summary>対角線長1、0.4のひし形弾のローカル頂点を返します。</summary>
        public static Vector2[] GetDiamondProjectileOutline()
        {
            return (Vector2[])DiamondProjectileOutline.Clone();
        }

        private void InitializeGlassObject(
            GameObject obj,
            Vector2[] outlinePoints,
            bool addEnemyDefeat,
            bool releasedFromAnchor)
        {
            obj.transform.SetParent(transform, false);

            GameObject outlineObject = new GameObject("GlassSurfaceLineRenderer");
            outlineObject.transform.SetParent(obj.transform, false);
            GlassSurfaceLineRenderer outline = outlineObject.AddComponent<GlassSurfaceLineRenderer>();
            var lr1 = outline.GetComponent<LineRenderer>();
            lr1.material = new Material(Shader.Find("Sprites/Default"));
            outline.SetOutline(outlinePoints);

            GameObject crackObject = new GameObject("CrackLineRenderer");
            crackObject.transform.SetParent(obj.transform, false);
            crackObject.AddComponent<CrackLineRenderer>();
            var lr2 = crackObject.GetComponent<LineRenderer>();
            lr2.material = new Material(Shader.Find("Sprites/Default"));

            lrSetting(lr1);
            lrSetting(lr2);

            PolygonCollider2D collider = obj.AddComponent<PolygonCollider2D>();
            collider.points = outlinePoints;

            GlassStatus spawnedStatus = obj.TryGetComponent(out GlassStatus existingStatus)
                ? existingStatus
                : obj.AddComponent<GlassStatus>();
            spawnedStatus.CopyFrom(glassStatus);
            spawnedStatus.ApplyEnemyDefenseDifficulty(difficulty);

            if (addEnemyDefeat &&
                !obj.TryGetComponent(out EnemyDefeatComponent _))
            {
                obj.AddComponent<EnemyDefeatComponent>();
            }

            // 子の描画とColliderを作った後に追加すると、Awakeで参照を自動取得できます。
            CrackProcessingComponent processing = obj.AddComponent<CrackProcessingComponent>();
            processing.Initialize(outlinePoints, null, releasedFromAnchor);
        }

        public string BuildPatternCompositionSummary()
        {
            EnsurePatternsDefined();
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < _spawnPatterns.Length; i++)
            {
                SpawnPattern pattern = _spawnPatterns[i];
                if (i > 0)
                {
                    builder.AppendLine();
                }
                builder.Append('[').Append(i).Append("] t=")
                    .Append(pattern.Time.ToString("0.###"))
                    .Append(" type=").Append(pattern.EnemyType)
                    .Append(" count=").Append(pattern.Positions?.Length ?? 0)
                    .Append(" active=").Append(pattern.IsActive);
            }
            return builder.ToString();
        }

        public float CalculateMaximumPatternArea()
        {
            EnsurePatternsDefined();
            float maximumArea = 0f;
            for (int i = 0; i < _spawnPatterns.Length; i++)
            {
                maximumArea = Mathf.Max(
                    maximumArea,
                    Mathf.Abs(CalculateSignedArea(GetPositionsForPattern(_spawnPatterns[i].EnemyType))));
            }
            return maximumArea;
        }

        public int CalculateMaximumFragmentUpperBound(float minimumBreakableArea)
        {
            if (minimumBreakableArea <= 0f)
            {
                return int.MaxValue;
            }

            // 各破片は閾値より大きい必要があるため、N * 閾値 < 元面積を満たす最大N。
            return Mathf.Max(
                0,
                Mathf.CeilToInt(CalculateMaximumPatternArea() / minimumBreakableArea) - 1);
        }

        private void EnsurePatternsDefined()
        {
            if (_spawnPatterns == null)
            {
                PatternDefine();
            }
        }

        private static float CalculateSignedArea(Vector2[] points)
        {
            if (points == null || points.Length < 3)
            {
                return 0f;
            }

            float twiceArea = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 current = points[i];
                Vector2 next = points[(i + 1) % points.Length];
                twiceArea += current.x * next.y - next.x * current.y;
            }
            return twiceArea * 0.5f;
        }

        void lrSetting(LineRenderer lr)
        {
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.startColor = Color.gray;
            lr.endColor = Color.gray;
        }
    }

    /// <summary>
    /// 敵出現地点から旧ガラス残骸を短時間押し出します。
    /// 新しく出現した敵とその子オブジェクトには作用しません。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class EnemySpawnRepulsionField : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float radius = 12f;
        [SerializeField, Min(0f)] private float outwardAcceleration = 30f;
        [SerializeField, Min(0.01f)] private float duration = 1.5f;

        private Transform excludedRoot;
        private float elapsedTime;

        public void Initialize(
            Transform spawnedEnemyRoot,
            float fieldRadius,
            float acceleration,
            float fieldDuration)
        {
            excludedRoot = spawnedEnemyRoot;
            radius = Mathf.Max(0.01f, fieldRadius);
            outwardAcceleration = Mathf.Max(0f, acceleration);
            duration = Mathf.Max(0.01f, fieldDuration);
        }

        private void FixedUpdate()
        {
            elapsedTime += Time.fixedDeltaTime;
            if (elapsedTime >= duration)
            {
                Destroy(gameObject);
                return;
            }

            GlassFragment[] debris = FindObjectsByType<GlassFragment>();
            Vector2 center = transform.position;
            float radiusSquared = radius * radius;

            for (int debrisIndex = 0; debrisIndex < debris.Length; debrisIndex++)
            {
                GlassFragment fragment = debris[debrisIndex];
                if (fragment == null ||
                    (excludedRoot != null &&
                        fragment.transform.IsChildOf(excludedRoot)) ||
                    !fragment.TryGetComponent(out Rigidbody2D body) ||
                    body.bodyType != RigidbodyType2D.Dynamic)
                {
                    continue;
                }

                Vector2 offset = body.worldCenterOfMass - center;
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSquared);
                Vector2 direction = distance > Mathf.Epsilon
                    ? offset / distance
                    : Vector2.up;
                float distanceFactor = Mathf.Max(0.2f, 1f - distance / radius);

                // 質量を掛けて、大小の破片へ同じ外向き加速度を与える。
                body.AddForce(
                    direction * outwardAcceleration * distanceFactor * body.mass,
                    ForceMode2D.Force);
            }
        }
    }
}
