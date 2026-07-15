using System;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public enum GlassWeaknessDistribution
    {
        Uniform,
        Random,
        CenterWeighted,
        EdgeWeighted
    }

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

    /// <summary>企画書で定義されたガラスの物性・クラック・構造パラメータです。</summary>
    [DisallowMultipleComponent]
    public sealed class GlassStatus : MonoBehaviour
    {
        [Header("Basic Physical Properties")]
        [SerializeField, Min(0.0001f)] private float thickness = 1f;
        [SerializeField, Min(0.0001f)] private float density = 1f;
        [SerializeField, Min(0f)] private float mass = 1f;
        [SerializeField, Min(0f)] private float gravityMultiplier = 1f;
        [SerializeField, Min(0f)] private float elasticity = 0.1f;
        [SerializeField, Range(0f, 1f)] private float impactAttenuationRate = 0.1f;

        [Header("Crack Properties")]
        [SerializeField, Min(0f)] private float crackInitiationResistance = 10f;
        [SerializeField, Min(0.0001f)] private float crackPropagationResistance = 1f;
        [SerializeField, Min(0f)] private float branchThreshold = 10f;
        [SerializeField, Min(0f)] private float branchCost = 1f;
        [SerializeField, Min(0f)] private float fragmentSeparationResistance = 1f;
        [SerializeField, Min(0)] private int initialCrackCount = 0;
        [SerializeField, Min(0f)] private float internalDefectDensity = 1f;
        [SerializeField, Min(0f)] private float internalDefectVariation = 0.25f;
        [SerializeField, Range(0f, 1f)] private float minimumInitialVulnerability = 0f;
        [SerializeField, Range(0f, 1f)] private float maximumInitialVulnerability = 1f;

        [Header("Structural Properties")]
        [SerializeField, Min(1)] private int resolution = 8;
        [SerializeField, Min(0)] private int virtualPointCount = 32;
        [SerializeField, Min(0.0001f)] private float virtualPointSpacing = 0.5f;
        [SerializeField] private GlassWeaknessDistribution weaknessDistribution = GlassWeaknessDistribution.Random;
        [SerializeField] private GlassOutlineShape outlineShape = GlassOutlineShape.Rectangle;
        [SerializeField] private Vector2[] fixedPositions = Array.Empty<Vector2>();
        [SerializeField] private Vector2 corePosition = Vector2.zero;

        [Header("Difficulty Extensions")]
        [SerializeField, Min(0f)] private float fixedPositionStrength = 1f;
        [SerializeField, Min(0f)] private float fragmentAttackMultiplier = 1f;
        [SerializeField, Min(0f)] private float fragmentFallSpeedMultiplier = 1f;
        [SerializeField, Min(0f)] private float minimumBreakableArea = 0.04f;
        [SerializeField, Min(0f)] private float repairSpeed = 0f;

        public float Thickness => thickness;
        public float Density => density;
        public float Mass => mass;
        public float GravityMultiplier => gravityMultiplier;
        public float Elasticity => elasticity;
        public float ImpactAttenuationRate => impactAttenuationRate;
        public float CrackInitiationResistance => crackInitiationResistance;
        public float CrackPropagationResistance => crackPropagationResistance;
        public float BranchThreshold => branchThreshold;
        public float BranchCost => branchCost;
        public float FragmentSeparationResistance => fragmentSeparationResistance;
        public int InitialCrackCount => initialCrackCount;
        public float InternalDefectDensity => internalDefectDensity;
        public float InternalDefectVariation => internalDefectVariation;
        public float MinimumInitialVulnerability => minimumInitialVulnerability;
        public float MaximumInitialVulnerability => maximumInitialVulnerability;
        public int Resolution => resolution;
        public int VirtualPointCount => virtualPointCount;
        public float VirtualPointSpacing => virtualPointSpacing;
        public GlassWeaknessDistribution WeaknessDistribution => weaknessDistribution;
        public GlassOutlineShape OutlineShape => outlineShape;
        public Vector2[] FixedPositions => (Vector2[])fixedPositions.Clone();
        public Vector2 CorePosition => corePosition;
        public float FixedPositionStrength => fixedPositionStrength;
        public float FragmentAttackMultiplier => fragmentAttackMultiplier;
        public float FragmentFallSpeedMultiplier => fragmentFallSpeedMultiplier;
        public float MinimumBreakableArea => minimumBreakableArea;
        public float RepairSpeed => repairSpeed;

        /// <summary>面積から、厚さと密度を反映した破片質量を求めます。</summary>
        public float CalculateMass(float area)
        {
            return Mathf.Max(0.05f, Mathf.Abs(area) * thickness * density);
        }

        /// <summary>指定範囲内へ内部欠陥点を生成します。</summary>
        public Vector2[] Crackpositions(float width, float height)
        {
            int count = virtualPointCount > 0 ? virtualPointCount : initialCrackCount;
            Vector2[] positions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float x = UnityEngine.Random.Range(-width * 0.5f, width * 0.5f);
                float y = UnityEngine.Random.Range(-height * 0.5f, height * 0.5f);
                positions[i] = new Vector2(x, y);
            }
            return positions;
        }

        /// <summary>シード付き乱数で、再現可能な内部亀裂点と脆弱性を生成します。</summary>
        public InitialCrackPointData[] GenerateInitialCrackPointData(float width, float height, int seed)
        {
            int count = virtualPointCount > 0 ? virtualPointCount : initialCrackCount;
            var result = new InitialCrackPointData[count];
            var random = new System.Random(seed);
            float minVulnerability = Mathf.Min(minimumInitialVulnerability, maximumInitialVulnerability);
            float maxVulnerability = Mathf.Max(minimumInitialVulnerability, maximumInitialVulnerability);

            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(-width * 0.5f, width * 0.5f, (float)random.NextDouble());
                float y = Mathf.Lerp(-height * 0.5f, height * 0.5f, (float)random.NextDouble());
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
            impactAttenuationRate = source.impactAttenuationRate;
            crackInitiationResistance = source.crackInitiationResistance;
            crackPropagationResistance = source.crackPropagationResistance;
            branchThreshold = source.branchThreshold;
            branchCost = source.branchCost;
            fragmentSeparationResistance = source.fragmentSeparationResistance;
            initialCrackCount = source.initialCrackCount;
            internalDefectDensity = source.internalDefectDensity;
            internalDefectVariation = source.internalDefectVariation;
            minimumInitialVulnerability = source.minimumInitialVulnerability;
            maximumInitialVulnerability = source.maximumInitialVulnerability;
            resolution = source.resolution;
            virtualPointCount = source.virtualPointCount;
            virtualPointSpacing = source.virtualPointSpacing;
            weaknessDistribution = source.weaknessDistribution;
            outlineShape = source.outlineShape;
            fixedPositions = source.fixedPositions == null
                ? Array.Empty<Vector2>()
                : (Vector2[])source.fixedPositions.Clone();
            corePosition = source.corePosition;
            fixedPositionStrength = source.fixedPositionStrength;
            fragmentAttackMultiplier = source.fragmentAttackMultiplier;
            fragmentFallSpeedMultiplier = source.fragmentFallSpeedMultiplier;
            minimumBreakableArea = source.minimumBreakableArea;
            repairSpeed = source.repairSpeed;
        }

        private void OnValidate()
        {
            thickness = Mathf.Max(0.0001f, thickness);
            density = Mathf.Max(0.0001f, density);
            mass = Mathf.Max(0f, mass);
            gravityMultiplier = Mathf.Max(0f, gravityMultiplier);
            elasticity = Mathf.Max(0f, elasticity);
            crackPropagationResistance = Mathf.Max(0.0001f, crackPropagationResistance);
            initialCrackCount = Mathf.Max(0, initialCrackCount);
            resolution = Mathf.Max(1, resolution);
            virtualPointCount = Mathf.Max(0, virtualPointCount);
            virtualPointSpacing = Mathf.Max(0.0001f, virtualPointSpacing);
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
    }
}
