using System;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public enum GlassOutlineShape
    {
        Custom,
        Rectangle,
        Circle,
        Slender,
        Suspended
    }

    [Serializable]
    public struct InitialCrackPointData
    {
        public Vector2 localPosition;

        [Range(0f, 1f)]
        public float vulnerability;
    }

    /// <summary>
    /// 現在の物理、内部クラック点、破片処理で実使用するガラスステータスです。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlassStatus : MonoBehaviour
    {
        [Header("Basic Physical Properties")]
        [SerializeField, Min(0.0001f)] private float thickness = 1f;
        [SerializeField, Min(0.0001f)] private float density = 1f;
        [SerializeField, Min(0f)] private float mass = 1f;
        [SerializeField, Min(0f)] private float gravityMultiplier = 1f;
        [SerializeField, Min(0f)] private float elasticity = 0.1f;

        [Header("Crack Nodes")]
        [SerializeField, Min(0)] private int initialCrackCount;
        [SerializeField, Range(0f, 1f)] private float minimumInitialVulnerability;
        [SerializeField, Range(0f, 1f)] private float maximumInitialVulnerability = 1f;
        [SerializeField, Min(0)] private int virtualPointCount = 32;
        [SerializeField, Range(0f, 1f)] private float enemyCrackEnergyMultiplier = 1f;
        [SerializeField, Min(0f)] private float maximumScanRadius = 20f;

        [Header("Structure")]
        [SerializeField] private GlassOutlineShape outlineShape = GlassOutlineShape.Rectangle;
        [SerializeField] private Vector2[] fixedPositions = Array.Empty<Vector2>();
        [SerializeField] private Vector2 corePosition = Vector2.zero;

        [Header("Difficulty Extensions")]
        [SerializeField, Min(0f)] private float fixedPositionStrength = 1f;
        [SerializeField, Min(0f)] private float fragmentAttackMultiplier = 1f;
        [SerializeField, Min(0f)] private float fragmentFallSpeedMultiplier = 1f;
        [SerializeField, Min(0f)] public float resourceRewardArea;

        private bool resourceRewardGranted;
        [SerializeField, Min(0f)] private float minimumBreakableArea = 0.04f;

        public float Thickness => thickness;
        public float Density => density;
        public float Mass => mass;
        public float GravityMultiplier => gravityMultiplier;
        public float Elasticity => elasticity;
        public int InitialCrackCount => initialCrackCount;
        public float MinimumInitialVulnerability => minimumInitialVulnerability;
        public float MaximumInitialVulnerability => maximumInitialVulnerability;
        public int VirtualPointCount => virtualPointCount;
        public float EnemyCrackEnergyMultiplier => enemyCrackEnergyMultiplier;
        public float MaximumScanRadius => maximumScanRadius;
        public GlassOutlineShape OutlineShape => outlineShape;
        public Vector2[] FixedPositions => (Vector2[])fixedPositions.Clone();
        public Vector2 CorePosition => corePosition;
        public float FixedPositionStrength => fixedPositionStrength;
        public float FragmentAttackMultiplier => fragmentAttackMultiplier;
        public float FragmentFallSpeedMultiplier => fragmentFallSpeedMultiplier;

        public void SetResourceRewardArea(float area)
        {
            resourceRewardArea = Mathf.Max(0f, area);
            resourceRewardGranted = false;
        }
        public float MinimumBreakableArea => minimumBreakableArea;

        /// <summary>成長画面で確定したガラスステータスを反映します。</summary>
        public void ApplyGrowthStatus(
            float newThickness,
            float newDensity,
            float newMass,
            float newGravityMultiplier,
            float newElasticity,
            int newInitialCrackCount,
            float newMinimumInitialVulnerability,
            float newMaximumInitialVulnerability,
            int newVirtualPointCount,
            float newFixedPositionStrength,
            float newFragmentAttackMultiplier,
            float newFragmentFallSpeedMultiplier,
            float newMinimumBreakableArea,
            float newEnemyCrackEnergyMultiplier = 1f,
            float newMaximumScanRadius = 20f)
        {
            thickness = Mathf.Max(0.0001f, newThickness);
            density = Mathf.Max(0.0001f, newDensity);
            mass = Mathf.Max(0f, newMass);
            gravityMultiplier = Mathf.Max(0f, newGravityMultiplier);
            elasticity = Mathf.Max(0f, newElasticity);
            initialCrackCount = Mathf.Max(0, newInitialCrackCount);
            minimumInitialVulnerability = Mathf.Clamp01(newMinimumInitialVulnerability);
            maximumInitialVulnerability = Mathf.Clamp01(newMaximumInitialVulnerability);
            if (minimumInitialVulnerability > maximumInitialVulnerability)
            {
                minimumInitialVulnerability = maximumInitialVulnerability;
            }
            virtualPointCount = Mathf.Max(0, newVirtualPointCount);
            fixedPositionStrength = Mathf.Max(0f, newFixedPositionStrength);
            fragmentAttackMultiplier = Mathf.Max(0f, newFragmentAttackMultiplier);
            fragmentFallSpeedMultiplier = Mathf.Max(0f, newFragmentFallSpeedMultiplier);
            minimumBreakableArea = Mathf.Max(0f, newMinimumBreakableArea);
            enemyCrackEnergyMultiplier = Mathf.Clamp01(newEnemyCrackEnergyMultiplier);
            maximumScanRadius = Mathf.Max(0f, newMaximumScanRadius);
        }

        /// <summary>敵防御を決める3ステータスを一括で反映します。</summary>
        public void ApplyEnemyDefenseStatus(
            float newEnemyCrackEnergyMultiplier,
            int newVirtualPointCount,
            float newMaximumScanRadius)
        {
            enemyCrackEnergyMultiplier = Mathf.Clamp01(newEnemyCrackEnergyMultiplier);
            virtualPointCount = Mathf.Max(0, newVirtualPointCount);
            maximumScanRadius = Mathf.Max(0f, newMaximumScanRadius);
        }

        /// <summary>難易度をwaveとclassへ分解し、敵防御値へ変換します。</summary>
        public void ApplyEnemyDefenseDifficulty(int difficulty)
        {
            int safeDifficulty = Mathf.Max(0, difficulty);
            int wave = safeDifficulty / 5;
            int enemyClass = safeDifficulty % 5;
            int pointCount = 12 + enemyClass * 8 + UnityEngine.Random.Range(-4, 5);
            int scanRadiusCandidate =
                5 * enemyClass + UnityEngine.Random.Range(-8, 9);

            ApplyEnemyDefenseStatus(
                Mathf.Pow(10f, wave * -0.3f),
                pointCount,
                Mathf.Clamp(scanRadiusCandidate, 3f, 20f));
        }

        /// <summary>面積から、厚さと密度を反映した破片質量を求めます。</summary>
        public float CalculateMass(float area)
        {
            return Mathf.Max(0.05f, Mathf.Abs(area) * thickness * density);
        }

        /// <summary>シード付き乱数で、再現可能な内部亀裂点と脆弱性を生成します。</summary>
        public InitialCrackPointData[] GenerateInitialCrackPointData(
            float width,
            float height,
            int seed)
        {
            int count = virtualPointCount > 0 ? virtualPointCount : initialCrackCount;
            var result = new InitialCrackPointData[count];
            var random = new System.Random(seed);
            float minVulnerability = Mathf.Min(
                minimumInitialVulnerability,
                maximumInitialVulnerability);
            float maxVulnerability = Mathf.Max(
                minimumInitialVulnerability,
                maximumInitialVulnerability);

            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(
                    -width * 0.5f,
                    width * 0.5f,
                    (float)random.NextDouble());
                float y = Mathf.Lerp(
                    -height * 0.5f,
                    height * 0.5f,
                    (float)random.NextDouble());
                float vulnerability = Mathf.Lerp(
                    minVulnerability,
                    maxVulnerability,
                    (float)random.NextDouble());

                result[i] = new InitialCrackPointData
                {
                    localPosition = new Vector2(x, y),
                    vulnerability = vulnerability
                };
            }

            return result;
        }

        /// <summary>実際の外周内に、指定数の仮想点を再現可能な形で生成します。</summary>
        public InitialCrackPointData[] GenerateInitialCrackPointData(
            Vector2[] outline,
            int seed)
        {
            int count = virtualPointCount > 0 ? virtualPointCount : initialCrackCount;
            if (count <= 0 || outline == null || outline.Length < 3)
            {
                return Array.Empty<InitialCrackPointData>();
            }

            Vector2 minimum = outline[0];
            Vector2 maximum = outline[0];
            for (int i = 1; i < outline.Length; i++)
            {
                minimum = Vector2.Min(minimum, outline[i]);
                maximum = Vector2.Max(maximum, outline[i]);
            }

            var result = new InitialCrackPointData[count];
            var random = new System.Random(seed);
            float minVulnerability = Mathf.Min(minimumInitialVulnerability, maximumInitialVulnerability);
            float maxVulnerability = Mathf.Max(minimumInitialVulnerability, maximumInitialVulnerability);
            int generatedCount = 0;
            int attempts = 0;
            int maximumAttempts = Mathf.Max(1024, count * 256);
            while (generatedCount < count && attempts++ < maximumAttempts)
            {
                Vector2 candidate = new Vector2(
                    Mathf.Lerp(minimum.x, maximum.x, (float)random.NextDouble()),
                    Mathf.Lerp(minimum.y, maximum.y, (float)random.NextDouble()));
                if (!IsPointInsidePolygon(candidate, outline))
                {
                    continue;
                }

                result[generatedCount++] = new InitialCrackPointData
                {
                    localPosition = candidate,
                    vulnerability = Mathf.Lerp(
                        minVulnerability,
                        maxVulnerability,
                        (float)random.NextDouble())
                };
            }

            // 極端に細いポリゴンでも個数契約を破らない。生成済み内部点を再利用し、
            // 1点も得られない退化形状だけは外周頂点を安全な最終手段とする。
            Vector2 fallback = generatedCount > 0 ? result[0].localPosition : outline[0];
            while (generatedCount < count)
            {
                result[generatedCount++] = new InitialCrackPointData
                {
                    localPosition = fallback,
                    vulnerability = Mathf.Lerp(
                        minVulnerability,
                        maxVulnerability,
                        (float)random.NextDouble())
                };
            }
            return result;
        }

        private static bool IsPointInsidePolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) /
                    (b.y - a.y) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Spawnerなどに置いたテンプレート設定を生成済みガラスへコピーします。</summary>
        public void CopyFrom(GlassStatus source)
        {
            if (source == null)
            {
                return;
            }

            thickness = source.thickness;
            density = source.density;
            mass = source.mass;
            gravityMultiplier = source.gravityMultiplier;
            elasticity = source.elasticity;
            initialCrackCount = source.initialCrackCount;
            minimumInitialVulnerability = source.minimumInitialVulnerability;
            maximumInitialVulnerability = source.maximumInitialVulnerability;
            virtualPointCount = source.virtualPointCount;
            enemyCrackEnergyMultiplier = source.enemyCrackEnergyMultiplier;
            maximumScanRadius = source.maximumScanRadius;
            outlineShape = source.outlineShape;
            fixedPositions = source.fixedPositions == null
                ? Array.Empty<Vector2>()
                : (Vector2[])source.fixedPositions.Clone();
            corePosition = source.corePosition;
            fixedPositionStrength = source.fixedPositionStrength;
            fragmentAttackMultiplier = source.fragmentAttackMultiplier;
            fragmentFallSpeedMultiplier = source.fragmentFallSpeedMultiplier;
            minimumBreakableArea = source.minimumBreakableArea;
        }

        private void OnValidate()
        {
            thickness = Mathf.Max(0.0001f, thickness);
            density = Mathf.Max(0.0001f, density);
            mass = Mathf.Max(0f, mass);
            gravityMultiplier = Mathf.Max(0f, gravityMultiplier);
            elasticity = Mathf.Max(0f, elasticity);
            initialCrackCount = Mathf.Max(0, initialCrackCount);
            virtualPointCount = Mathf.Max(0, virtualPointCount);
            enemyCrackEnergyMultiplier = Mathf.Clamp01(enemyCrackEnergyMultiplier);
            maximumScanRadius = Mathf.Max(0f, maximumScanRadius);
            minimumBreakableArea = Mathf.Max(0f, minimumBreakableArea);
            minimumInitialVulnerability = Mathf.Clamp01(minimumInitialVulnerability);
            maximumInitialVulnerability = Mathf.Clamp01(maximumInitialVulnerability);
            if (minimumInitialVulnerability > maximumInitialVulnerability)
            {
                (minimumInitialVulnerability, maximumInitialVulnerability) =
                    (maximumInitialVulnerability, minimumInitialVulnerability);
            }
            fixedPositions ??= Array.Empty<Vector2>();
        }

        //消滅処理
        public void DestroyGlass()
        {
            if (!resourceRewardGranted && resourceRewardArea > 0f)
            {
                resourceRewardGranted = true;
                ResourceComponent.Instance.Add(resourceRewardArea);
            }
            Destroy(gameObject);
        }

        public void Update()
        {
            if(transform.position.y < -10f) // 画面下に落ちたら消滅
            {
                DestroyGlass();
            }
        }

    }
}
