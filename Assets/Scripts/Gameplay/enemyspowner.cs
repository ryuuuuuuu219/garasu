using GlassShooter.Gameplay;
using PolygonRendering;
using UnityEngine;

namespace Gameplay
{
    [AddComponentMenu("Scripts/Gameplay/Enemy Spawner")]
    internal class enemyspowner : MonoBehaviour
    {
        public GlassStatus glassStatus;

        private struct SpownPattern
        {
            public float Time;
            public string EnemyType;
            public Vector2[] Positions;
            public bool IsActive;
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

                // 新しい形は、EnemyTypeのcaseと外周頂点をここへ追加します。
                default:
                    Debug.LogWarning($"Unknown EnemyType '{enemyType}'. Using Basic outline.", this);
                    return GetPositionsForPattern("Basic");
            }
        }

        private SpownPattern[] _spownPatterns;
        private float _timer;

        private void Awake()
        {
            PatternDefine();

            float previousTime = -1f;
            foreach (SpownPattern pattern in _spownPatterns)
            {
                if (pattern.Time < previousTime)
                {
                    Debug.LogError("SpownPatterns are not in ascending order of time.", this);
                }

                previousTime = pattern.Time;
            }
        }

        private void PatternDefine()
        {
            _spownPatterns = new[]
            {
                new SpownPattern
                {
                    Time = 1f,
                    EnemyType = "Basic",
                    Positions = new[] { new Vector2(0f, 2f) },
                    IsActive = false
                }
            };
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            for (int patternIndex = 0; patternIndex < _spownPatterns.Length; patternIndex++)
            {
                SpownPattern pattern = _spownPatterns[patternIndex];
                if (pattern.IsActive || _timer < pattern.Time)
                {
                    continue;
                }

                for (int positionIndex = 0; positionIndex < pattern.Positions.Length; positionIndex++)
                {
                    SpawnEnemy(pattern.EnemyType, pattern.Positions[positionIndex], positionIndex);
                }

                pattern.IsActive = true;
                _spownPatterns[patternIndex] = pattern;
                Debug.Log($"Spawning {pattern.EnemyType} at time {_timer}", this);
            }
        }

        private void SpawnEnemy(string enemyType, Vector2 localPosition, int positionIndex)
        {
            Vector2[] outlinePoints = GetPositionsForPattern(enemyType);

            GameObject enemy = new GameObject($"{enemyType}_{positionIndex}");
            Init(enemy, outlinePoints);
            enemy.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
        }

        public void Init(GameObject obj, Vector2[] outlinePoints)
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

            // 子の描画とColliderを作った後に追加すると、Awakeで参照を自動取得できます。
            CrackProcessingComponent processing = obj.AddComponent<CrackProcessingComponent>();
            processing.Initialize(outlinePoints);
        }

        void lrSetting(LineRenderer lr)
        {
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.startColor = Color.gray;
            lr.endColor = Color.gray;
        }
    }
}
