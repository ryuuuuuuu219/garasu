using GlassShooter.Gameplay;
using PolygonRendering;
using UnityEngine;

namespace Gameplay
{
    [AddComponentMenu("Scripts/Gameplay/Enemy Spawner")]
    internal class enemyspowner : MonoBehaviour
    {
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
            enemy.transform.SetParent(transform, false);
            enemy.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);

            GameObject outlineObject = new GameObject("GlassSurfaceLineRenderer");
            outlineObject.transform.SetParent(enemy.transform, false);
            GlassSurfaceLineRenderer outline = outlineObject.AddComponent<GlassSurfaceLineRenderer>();
            outline.SetOutline(outlinePoints);

            GameObject crackObject = new GameObject("CrackLineRenderer");
            crackObject.transform.SetParent(enemy.transform, false);
            crackObject.AddComponent<CrackLineRenderer>();

            // 子の描画コンポーネントを作った後に追加すると、Awakeで参照を自動取得できます。
            enemy.AddComponent<CrackProcessingComponent>();

            PolygonCollider2D collider = enemy.AddComponent<PolygonCollider2D>();
            collider.points = outlinePoints;
        }
    }
}
