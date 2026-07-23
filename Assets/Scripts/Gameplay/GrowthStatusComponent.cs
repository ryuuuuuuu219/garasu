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
        PlayerMaximumDurability,
        PlayerFractureToughness,
        PlayerHitboxScale,
        PlayerCollectionRadius,
        PlayerEnvironmentGravity,
        GlassSurfaceFlawMinimumSpacing,
        GlassCrackMaximumSize,
        GlassCrackEnergyCutRate,
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
            new(GrowthStatId.PlayerMoveSpeed, "プレイヤー・ガラス強化", "移動速度強化", 4f, 0.5f, int.MaxValue, 500),
            new(GrowthStatId.PlayerFireInterval, "プレイヤー", "発射間隔", 0.16f, -0.01f, 10, 3),
            new(GrowthStatId.BulletMass, "弾幕・破砕弾強化", "質量強化", 0.03f, 0f, int.MaxValue, 2),
            new(GrowthStatId.BulletSpeed, "弾幕・破砕弾強化", "初速強化", 3f, 0f, int.MaxValue, 1),
            new(GrowthStatId.BulletFireRate, "弾幕・破砕弾強化", "発射レート強化", 1f, 0f, int.MaxValue, 5),
            new(GrowthStatId.BulletCrackEfficiency, "弾幕・破砕弾強化", "クラック変換効率強化", 0.05f, 0f, int.MaxValue, 2),
            new(GrowthStatId.BulletContactSize, "弾幕・破砕弾強化", "縮小率強化", 1f, 0f, int.MaxValue, 20),
            new(GrowthStatId.GlassThickness, "ガラス", "厚さ", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassDensity, "ガラス", "密度", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassMass, "ガラス", "質量", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassGravity, "ガラス", "重力倍率", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassElasticity, "ガラス", "弾性", 0.1f, 0.05f, 18, 2),
            new(GrowthStatId.GlassInitialCracks, "ガラス", "初期亀裂数", 0f, 1f, 20, 3, true),
            new(GrowthStatId.GlassMinimumVulnerability, "ガラス", "最小脆弱度", 0f, 0.05f, 20, 3),
            new(GrowthStatId.GlassMaximumVulnerability, "ガラス", "最大脆弱度", 1f, 0f, 0, 0),
            new(GrowthStatId.GlassVirtualPoints, "ガラス", "仮想点数", 32f, 2f, 32, 3, true),
            new(GrowthStatId.GlassFixedStrength, "ガラス", "固定位置強度", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassFragmentAttack, "ガラス", "破片攻撃倍率", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassFragmentFallSpeed, "ガラス", "破片落下速度", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassMinimumBreakableArea, "ガラス", "最小破壊可能面積", 0.04f, -0.002f, 15, 4),
            new(GrowthStatId.PlayerMaximumDurability, "プレイヤー強化", "最大耐久力強化", 1f, 0f, 0, 0),
            new(GrowthStatId.PlayerFractureToughness, "プレイヤー強化", "破壊靭性強化", 1f, 0f, 0, 0),
            new(GrowthStatId.PlayerHitboxScale, "プレイヤー・ガラス強化", "当たり判定縮小", 0.2f, 0f, int.MaxValue, 50),
            new(GrowthStatId.PlayerCollectionRadius, "プレイヤー強化", "自動回収範囲強化", 0f, 0f, 0, 0),
            new(GrowthStatId.PlayerEnvironmentGravity, "プレイヤー強化", "環境・重力強化", 0.06f, 0.02f, 20, 3),
            new(GrowthStatId.GlassSurfaceFlawMinimumSpacing, "ガラス強化", "表面傷間隔強化", 1.2f, -0.05f, 20, 3),
            new(GrowthStatId.GlassCrackMaximumSize, "ガラス強化", "亀裂の最大の大きさ強化", 1.2f, 0.1f, 20, 3),
            new(GrowthStatId.GlassCrackEnergyCutRate, "ガラス強化", "敵側亀裂エネルギーカット率", 0f, 0.04f, 20, 4)
        };

        /// <summary>強化画面に表示するカテゴリと項目の順序です。</summary>
        public static readonly GrowthStatDefinition[] DisplayDefinitions =
        {
            Definitions[(int)GrowthStatId.BulletMass],
            Definitions[(int)GrowthStatId.BulletSpeed],
            Definitions[(int)GrowthStatId.BulletCrackEfficiency],
            Definitions[(int)GrowthStatId.BulletFireRate],
            Definitions[(int)GrowthStatId.BulletContactSize],
            Definitions[(int)GrowthStatId.PlayerHitboxScale],
            Definitions[(int)GrowthStatId.PlayerMoveSpeed]
        };

        [SerializeField] private int[] upgradeLevels = new int[(int)GrowthStatId.Count];

        private static GrowthStatusComponent instance;
        private const string PlayerPrefsKeyPrefix = "GlassShooter.Growth.Level.";
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
            int level = GetLevel(id);
            return id switch
            {
                GrowthStatId.BulletMass => 0.03f + 0.01f * level,
                GrowthStatId.BulletSpeed => 3f + 0.2f * Mathf.Sqrt(level),
                GrowthStatId.BulletCrackEfficiency => 0.05f + 0.01f * Mathf.Sqrt(level),
                GrowthStatId.BulletFireRate => 1f + Mathf.Sqrt(0.2f * level),
                GrowthStatId.BulletContactSize => 1f - 0.001f * level,
                GrowthStatId.PlayerHitboxScale => Mathf.Pow(0.2f, Mathf.Max(level / 1000f, 1f)),
                GrowthStatId.PlayerMoveSpeed => 4f + 0.5f * level,
                _ => definition.BaseValue + definition.Step * level
            };
        }

        public int GetUpgradeCost(GrowthStatId id)
        {
            GrowthStatDefinition definition = Definitions[(int)id];
            long cost = (long)definition.BaseCost * (GetLevel(id) + 1L);
            return (int)Math.Min(cost, int.MaxValue);
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
            SaveLevels();
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
                foreach (PlayerShooterController player in roots[i].GetComponentsInChildren<PlayerShooterController>(true))
                {
                    ApplyTo(player);
                }
            }
        }

        public void ApplyTo(BulletStatus target)
        {
            target.ApplyGrowthStatus(
                GetValue(GrowthStatId.BulletMass),
                GetValue(GrowthStatId.BulletSpeed),
                GetValue(GrowthStatId.BulletFireRate),
                GetValue(GrowthStatId.BulletCrackEfficiency),
                GetValue(GrowthStatId.BulletContactSize));
        }

        private void ApplyTo(PlayerShooterController target)
        {
            float fireRate = GetValue(GrowthStatId.BulletFireRate);
            target.ApplyGrowthStatus(
                GetValue(GrowthStatId.PlayerMoveSpeed),
                fireRate > 0f ? 1f / fireRate : GetValue(GrowthStatId.PlayerFireInterval),
                GetValue(GrowthStatId.PlayerHitboxScale));
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
            LoadLevels();
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

        private void LoadLevels()
        {
            for (int index = 0; index < (int)GrowthStatId.Count; index++)
            {
                GrowthStatId id = (GrowthStatId)index;
                upgradeLevels[index] = Mathf.Max(
                    0,
                    PlayerPrefs.GetInt(PlayerPrefsKeyPrefix + id, upgradeLevels[index]));
            }
        }

        private void SaveLevels()
        {
            for (int index = 0; index < (int)GrowthStatId.Count; index++)
            {
                GrowthStatId id = (GrowthStatId)index;
                PlayerPrefs.SetInt(PlayerPrefsKeyPrefix + id, upgradeLevels[index]);
            }
            PlayerPrefs.Save();
        }
    }
}
