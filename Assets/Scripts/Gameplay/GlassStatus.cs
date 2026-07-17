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
            float newMinimumBreakableArea)
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
