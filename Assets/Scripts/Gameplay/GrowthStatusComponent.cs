using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GlassShooter.Gameplay
{
    public enum GrowthStatId
    {
        PlayerMoveSpeed,
        PlayerFireInterval,
        BulletMass,
        BulletSpeed,
        BulletFireRate,
        BulletShotCount,
        BulletCrackEfficiency,
        BulletContactSize,
        GlassThickness,
        GlassDensity,
        GlassMass,
        GlassGravity,
        GlassElasticity,
        GlassInitialCracks,
        GlassMinimumVulnerability,
        GlassMaximumVulnerability,
        GlassVirtualPoints,
        GlassFixedStrength,
        GlassFragmentAttack,
        GlassFragmentFallSpeed,
        GlassMinimumBreakableArea,
        Count
    }

    public readonly struct GrowthStatDefinition
    {
        public GrowthStatDefinition(
            GrowthStatId id,
            string group,
            string label,
            float baseValue,
            float step,
            int maximumLevel,
            int baseCost,
            bool isInteger = false)
        {
            Id = id;
            Group = group;
            Label = label;
            BaseValue = baseValue;
            Step = step;
            MaximumLevel = maximumLevel;
            BaseCost = baseCost;
            IsInteger = isInteger;
        }

        public GrowthStatId Id { get; }
        public string Group { get; }
        public string Label { get; }
        public float BaseValue { get; }
        public float Step { get; }
        public int MaximumLevel { get; }
        public int BaseCost { get; }
        public bool IsInteger { get; }
    }

    /// <summary>購入レベルを保持し、実際の3種のステータスへ反映します。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ResourceComponent))]
    public sealed class GrowthStatusComponent : MonoBehaviour
    {
        public static readonly GrowthStatDefinition[] Definitions =
        {
            new(GrowthStatId.PlayerMoveSpeed, "PLAYER", "Move speed", 4f, 0.5f, 20, 2),
            new(GrowthStatId.PlayerFireInterval, "PLAYER", "Fire interval", 0.16f, -0.01f, 10, 3),
            new(GrowthStatId.BulletMass, "BULLET", "Mass", 1f, 0.1f, 30, 2),
            new(GrowthStatId.BulletSpeed, "BULLET", "Speed", 12f, 1f, 20, 2),
            new(GrowthStatId.BulletFireRate, "BULLET", "Fire rate", 6.25f, 0.5f, 20, 3),
            new(GrowthStatId.BulletShotCount, "BULLET", "Simultaneous shots", 1f, 1f, 7, 10, true),
            new(GrowthStatId.BulletCrackEfficiency, "BULLET", "Crack efficiency", 0.5f, 0.05f, 10, 4),
            new(GrowthStatId.BulletContactSize, "BULLET", "Contact size multiplier", 0.904382f, -0.01f, 15, 4),
            new(GrowthStatId.GlassThickness, "GLASS", "Thickness", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassDensity, "GLASS", "Density", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassMass, "GLASS", "Mass", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassGravity, "GLASS", "Gravity multiplier", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassElasticity, "GLASS", "Elasticity", 0.1f, 0.05f, 18, 2),
            new(GrowthStatId.GlassInitialCracks, "GLASS", "Initial crack count", 0f, 1f, 20, 3, true),
            new(GrowthStatId.GlassMinimumVulnerability, "GLASS", "Minimum vulnerability", 0f, 0.05f, 20, 3),
            new(GrowthStatId.GlassMaximumVulnerability, "GLASS", "Maximum vulnerability", 1f, 0f, 0, 0),
            new(GrowthStatId.GlassVirtualPoints, "GLASS", "Virtual point count", 32f, 2f, 32, 3, true),
            new(GrowthStatId.GlassFixedStrength, "GLASS", "Fixed position strength", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassFragmentAttack, "GLASS", "Fragment attack multiplier", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassFragmentFallSpeed, "GLASS", "Fragment fall speed", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassMinimumBreakableArea, "GLASS", "Minimum breakable area", 0.04f, -0.002f, 15, 4)
        };

        [SerializeField] private int[] upgradeLevels = new int[(int)GrowthStatId.Count];

        private static GrowthStatusComponent instance;
        private ResourceComponent resource;

        public static GrowthStatusComponent Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                ResourceComponent root = ResourceComponent.Instance;
                instance = root.GetComponent<GrowthStatusComponent>();
                return instance != null ? instance : root.gameObject.AddComponent<GrowthStatusComponent>();
            }
        }

        public event Action Changed;

        public int GetLevel(GrowthStatId id) => upgradeLevels[(int)id];

        public float GetValue(GrowthStatId id)
        {
            GrowthStatDefinition definition = Definitions[(int)id];
            return definition.BaseValue + definition.Step * GetLevel(id);
        }

        public int GetUpgradeCost(GrowthStatId id)
        {
            GrowthStatDefinition definition = Definitions[(int)id];
            return definition.BaseCost * (GetLevel(id) + 1);
        }

        public bool CanUpgrade(GrowthStatId id)
        {
            GrowthStatDefinition definition = Definitions[(int)id];
            return definition.MaximumLevel > 0 && GetLevel(id) < definition.MaximumLevel;
        }

        public bool TryUpgrade(GrowthStatId id)
        {
            if (!CanUpgrade(id) || !resource.TrySpend(GetUpgradeCost(id)))
            {
                return false;
            }

            upgradeLevels[(int)id]++;
            ApplyToScene(SceneManager.GetActiveScene());
            Changed?.Invoke();
            return true;
        }

        public string FormatValue(GrowthStatId id)
        {
            GrowthStatDefinition definition = Definitions[(int)id];
            return definition.IsInteger
                ? Mathf.RoundToInt(GetValue(id)).ToString()
                : GetValue(id).ToString("0.###");
        }

        public void ApplyToScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                foreach (BulletStatus bullet in roots[i].GetComponentsInChildren<BulletStatus>(true))
                {
                    ApplyTo(bullet);
                }
                foreach (GlassStatus glass in roots[i].GetComponentsInChildren<GlassStatus>(true))
                {
                    ApplyTo(glass);
                }
                foreach (PlayerShooterController player in roots[i].GetComponentsInChildren<PlayerShooterController>(true))
                {
                    ApplyTo(player);
                }
            }
        }

        private void ApplyTo(BulletStatus target)
        {
            target.ApplyGrowthStatus(
                GetValue(GrowthStatId.BulletMass),
                GetValue(GrowthStatId.BulletSpeed),
                GetValue(GrowthStatId.BulletFireRate),
                Mathf.RoundToInt(GetValue(GrowthStatId.BulletShotCount)),
                GetValue(GrowthStatId.BulletCrackEfficiency),
                GetValue(GrowthStatId.BulletContactSize));
        }

        private void ApplyTo(GlassStatus target)
        {
            target.ApplyGrowthStatus(
                GetValue(GrowthStatId.GlassThickness),
                GetValue(GrowthStatId.GlassDensity),
                GetValue(GrowthStatId.GlassMass),
                GetValue(GrowthStatId.GlassGravity),
                GetValue(GrowthStatId.GlassElasticity),
                Mathf.RoundToInt(GetValue(GrowthStatId.GlassInitialCracks)),
                GetValue(GrowthStatId.GlassMinimumVulnerability),
                GetValue(GrowthStatId.GlassMaximumVulnerability),
                Mathf.RoundToInt(GetValue(GrowthStatId.GlassVirtualPoints)),
                GetValue(GrowthStatId.GlassFixedStrength),
                GetValue(GrowthStatId.GlassFragmentAttack),
                GetValue(GrowthStatId.GlassFragmentFallSpeed),
                GetValue(GrowthStatId.GlassMinimumBreakableArea));
        }

        private void ApplyTo(PlayerShooterController target)
        {
            target.ApplyGrowthStatus(
                GetValue(GrowthStatId.PlayerMoveSpeed),
                GetValue(GrowthStatId.PlayerFireInterval));
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            resource = GetComponent<ResourceComponent>();
            if (upgradeLevels == null || upgradeLevels.Length != (int)GrowthStatId.Count)
            {
                Array.Resize(ref upgradeLevels, (int)GrowthStatId.Count);
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToScene(scene);
        }
    }
}
