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
        PlayerMass,
        SmallLightUnlock,
        SmallLightRange,
        SmallLightAngle,
        SmallLightShrink,
        Count
    }

    public readonly struct GrowthStatDefinition
    {
        public GrowthStatDefinition(
            GrowthStatId id,
            string group,
            string label,
            string description,
            float baseValue,
            float step,
            int maximumLevel,
            int baseCost,
            bool isInteger = false)
        {
            Id = id;
            Group = group;
            Label = label;
            Description = description;
            BaseValue = baseValue;
            Step = step;
            MaximumLevel = maximumLevel;
            BaseCost = baseCost;
            IsInteger = isInteger;
        }

        public GrowthStatId Id { get; }
        public string Group { get; }
        public string Label { get; }
        public string Description { get; }
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
            new(GrowthStatId.PlayerMoveSpeed, "プレイヤー・ガラス強化", "推力・最高速度強化", "プレイヤーの推力と最高速度を同時に増加させ、破片の回避や位置調整をしやすくします。", 4f, 0.5f, int.MaxValue, 500),
            new(GrowthStatId.PlayerFireInterval, "プレイヤー", "発射間隔", "弾を発射してから次の弾を発射できるまでの秒数です。", 0.16f, -0.01f, 10, 3),
            new(GrowthStatId.BulletMass, "弾幕・破砕弾強化", "質量強化", "破砕弾の質量を増加させます。運動エネルギー E=1/2・m・v² のmが増え、クラック進行能力と初回オーバーキル能力が上昇します。", 0.1f, 0f, int.MaxValue, 2),
            new(GrowthStatId.BulletSpeed, "弾幕・破砕弾強化", "初速強化", "破砕弾の飛行速度を増加させます。着弾までの時間が短くなり、速度の二乗に比例して運動エネルギーが増加します。", 4.5f, 0f, int.MaxValue, 1),
            new(GrowthStatId.BulletFireRate, "弾幕・破砕弾強化", "発射レート強化", "1秒あたりの発射回数を増加させます。実際の発射間隔は「1÷発射レート」秒です。", 1f, 0f, int.MaxValue, 5),
            new(GrowthStatId.BulletCrackEfficiency, "弾幕・破砕弾強化", "クラック変換効率強化", "破砕弾の運動エネルギーからクラック進行へ変換される割合を増加させます。投入エネルギーは運動エネルギーにこの効率と敵ごとのエネルギー乗数を掛けた値です。", 0.05f, 0f, int.MaxValue, 2),
            new(GrowthStatId.BulletContactSize, "弾幕・破砕弾強化", "縮小率強化", "着弾時にガラスへ適用する残存寸法倍率を低下させ、1発あたりの縮小量を増加させます。破壊を速め、弾道を阻む破片の排除に役立つ一方、残る破片の大きさにも影響します。", 1f, 0f, int.MaxValue, 20),
            new(GrowthStatId.GlassThickness, "ガラス", "厚さ", "ガラスの厚さです。破壊に必要なエネルギーへ影響します。", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassDensity, "ガラス", "密度", "ガラスの単位体積あたりの質量です。", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassMass, "ガラス", "質量", "ガラス全体の質量に掛ける倍率です。", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassGravity, "ガラス", "重力倍率", "ガラスや破片に働く重力の倍率です。", 1f, 0.1f, 20, 2),
            new(GrowthStatId.GlassElasticity, "ガラス", "弾性", "衝撃を受けたガラスの変形・反発しやすさです。", 0.1f, 0.05f, 18, 2),
            new(GrowthStatId.GlassInitialCracks, "ガラス", "初期亀裂数", "開始時からガラス表面に存在する亀裂の数です。", 0f, 1f, 20, 3, true),
            new(GrowthStatId.GlassMinimumVulnerability, "ガラス", "最小脆弱度", "ガラス表面に設定される脆弱度の下限です。", 0f, 0.05f, 20, 3),
            new(GrowthStatId.GlassMaximumVulnerability, "ガラス", "最大脆弱度", "ガラス表面に設定される脆弱度の上限です。", 1f, 0f, 0, 0),
            new(GrowthStatId.GlassVirtualPoints, "ガラス", "仮想点数", "亀裂計算に使うガラス表面の仮想点の数です。", 32f, 2f, 32, 3, true),
            new(GrowthStatId.GlassFixedStrength, "ガラス", "固定位置強度", "ガラスを固定している位置の強度倍率です。", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassFragmentAttack, "ガラス", "破片攻撃倍率", "飛散したガラス破片が与える攻撃の倍率です。", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassFragmentFallSpeed, "ガラス", "破片落下速度", "分離したガラス破片が落下する速度の倍率です。", 1f, 0.1f, 20, 3),
            new(GrowthStatId.GlassMinimumBreakableArea, "ガラス", "最小破壊可能面積", "独立した破片として分離できる最小面積です。", 0.04f, -0.002f, 15, 4),
            new(GrowthStatId.PlayerMaximumDurability, "プレイヤー強化", "最大耐久力強化", "プレイヤーが耐えられるダメージの最大量です。", 1f, 0f, 0, 0),
            new(GrowthStatId.PlayerFractureToughness, "プレイヤー強化", "破壊靭性強化", "プレイヤーが亀裂や破壊へ抵抗する強さです。", 1f, 0f, 0, 0),
            new(GrowthStatId.PlayerHitboxScale, "プレイヤー・ガラス強化", "当たり判定縮小", "プレイヤーのPolygonCollider2Dを縮小し、破片へ接触しにくくします。見た目ではなく、保存した基準当たり判定の各頂点座標へ倍率を適用します。", 0.2f, 0f, int.MaxValue, 50),
            new(GrowthStatId.PlayerCollectionRadius, "プレイヤー強化", "自動回収範囲強化", "周囲の資源を自動回収できる半径です。", 0f, 0f, 0, 0),
            new(GrowthStatId.PlayerEnvironmentGravity, "プレイヤー強化", "環境・重力強化", "プレイヤーが環境へ作用させる重力の強さです。", 0.06f, 0.02f, 20, 3),
            new(GrowthStatId.GlassSurfaceFlawMinimumSpacing, "ガラス強化", "表面傷間隔強化", "ガラス表面に生成する傷同士の最小間隔です。", 1.2f, -0.05f, 20, 3),
            new(GrowthStatId.GlassCrackMaximumSize, "ガラス強化", "亀裂の最大の大きさ強化", "1本の亀裂が成長できる最大サイズです。", 1.2f, 0.1f, 20, 3),
            new(GrowthStatId.GlassCrackEnergyCutRate, "ガラス強化", "敵側亀裂エネルギーカット率", "敵側で亀裂へ伝わるエネルギーを減衰させる割合です。", 0f, 0.04f, 20, 4),
            new(GrowthStatId.PlayerMass, "プレイヤー・ガラス強化", "質量強化", "プレイヤーの質量を増加させ、敵の妨害弾やガラス破片との衝突で押しのけられにくくします。", 0.1f, 0f, 1000, 1000),
            new(GrowthStatId.SmallLightUnlock, "特殊効果・破片対策", "スモールライト開放", "プレイヤー前方へ扇形の光を常時照射し、範囲へ接触した固定されていないガラスを継続的に縮小します。", 0f, 1f, 1, 50000, true),
            new(GrowthStatId.SmallLightRange, "特殊効果・破片対策", "スモールライト射程", "スモールライトの扇形が届く距離を増加させます。", 2f, 0f, 1000, 1000),
            new(GrowthStatId.SmallLightAngle, "特殊効果・破片対策", "スモールライト角度", "スモールライトの扇形の全角を増加させます。", 2.5f, 0f, 1000, 1000),
            new(GrowthStatId.SmallLightShrink, "特殊効果・破片対策", "スモールライト縮小率", "照射中のガラスへ1秒ごとに適用する線形寸法倍率を低下させます。面積倍率は線形寸法倍率の二乗です。", 1f, 0f, 1000, 15000)
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
            Definitions[(int)GrowthStatId.PlayerMoveSpeed],
            Definitions[(int)GrowthStatId.PlayerMass],
            Definitions[(int)GrowthStatId.SmallLightUnlock],
            Definitions[(int)GrowthStatId.SmallLightRange],
            Definitions[(int)GrowthStatId.SmallLightAngle],
            Definitions[(int)GrowthStatId.SmallLightShrink]
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

        /// <summary>指定した成長項目の表示情報と初期値・強化条件を返します。</summary>
        public static GrowthStatDefinition GetDefinition(GrowthStatId id) => Definitions[(int)id];

        /// <summary>指定した成長項目がゲームへ与える効果の説明文を返します。</summary>
        public static string GetDescription(GrowthStatId id) => GetDefinition(id).Description;

        /// <summary>成長画面で能力値の後ろに表示する単位を返します。</summary>
        public static string GetValueUnit(GrowthStatId id)
        {
            return id switch
            {
                GrowthStatId.BulletMass => "kg",
                GrowthStatId.BulletSpeed => "m/s",
                GrowthStatId.BulletFireRate => "発/s",
                GrowthStatId.BulletCrackEfficiency => "-",
                GrowthStatId.BulletContactSize => "倍",
                GrowthStatId.PlayerHitboxScale => "倍",
                GrowthStatId.PlayerMoveSpeed => "N, m/s",
                GrowthStatId.PlayerMass => "kg",
                GrowthStatId.SmallLightUnlock => "-",
                GrowthStatId.SmallLightRange => "m",
                GrowthStatId.SmallLightAngle => "deg",
                GrowthStatId.SmallLightShrink => "倍/s",
                _ => "-"
            };
        }

        /// <summary>Lを現在レベルとした、指定項目の現在能力値の計算式を返します。</summary>
        public static string GetValueFormula(GrowthStatId id)
        {
            return id switch
            {
                GrowthStatId.BulletMass => "0.1 + 0.01 × L",
                GrowthStatId.BulletSpeed => "4.5 + 0.2 × √L",
                GrowthStatId.BulletCrackEfficiency => "0.05 + 0.01 × √L",
                GrowthStatId.BulletFireRate => "1 + √(0.2 × L)",
                GrowthStatId.BulletContactSize => "1 - 0.001 × L（適用時に0～1へ制限）",
                GrowthStatId.PlayerHitboxScale => "0.2^max(L ÷ 1000, 1)",
                GrowthStatId.PlayerMoveSpeed => "4 + 0.5 × L",
                GrowthStatId.PlayerMass => "0.1 + 0.001 × L^1.5",
                GrowthStatId.SmallLightUnlock => "L=0: 未開放、L=1: 開放",
                GrowthStatId.SmallLightRange => "2 + 0.5 × L^(2/3)",
                GrowthStatId.SmallLightAngle => "2.5 + 0.1 × L^(2/3) [deg]",
                GrowthStatId.SmallLightShrink => "0.995^(L/3) [線形寸法倍率/秒]",
                _ => FormatLinearValueFormula(GetDefinition(id))
            };
        }

        /// <summary>Lを現在レベルとした、次の1レベルを購入するコスト式を返します。</summary>
        public static string GetUpgradeCostFormula(GrowthStatId id)
        {
            GrowthStatDefinition definition = GetDefinition(id);
            if (id == GrowthStatId.PlayerMass)
            {
                return "1000 × (L + 1)^2";
            }
            if (id == GrowthStatId.SmallLightUnlock)
            {
                return "50000";
            }
            if (id == GrowthStatId.SmallLightRange ||
                id == GrowthStatId.SmallLightAngle)
            {
                return "1000 × (L + 1)";
            }
            if (id == GrowthStatId.SmallLightShrink)
            {
                return "Ceil(15000 × 1.05^L)";
            }
            return definition.MaximumLevel <= 0 || definition.BaseCost <= 0
                ? "購入不可（未実装）"
                : $"{definition.BaseCost} × (L + 1)";
        }

        public float GetValue(GrowthStatId id)
        {
            GrowthStatDefinition definition = GetDefinition(id);
            int level = GetLevel(id);
            return id switch
            {
                GrowthStatId.BulletMass => 0.1f + 0.01f * level,
                GrowthStatId.BulletSpeed => 4.5f + 0.2f * Mathf.Sqrt(level),
                GrowthStatId.BulletCrackEfficiency => 0.05f + 0.01f * Mathf.Sqrt(level),
                GrowthStatId.BulletFireRate => 1f + Mathf.Sqrt(0.2f * level),
                GrowthStatId.BulletContactSize => 1f - 0.001f * level,
                GrowthStatId.PlayerHitboxScale => Mathf.Pow(0.2f, Mathf.Max(level / 1000f, 1f)),
                GrowthStatId.PlayerMoveSpeed => 4f + 0.5f * level,
                GrowthStatId.PlayerMass => 0.1f + 0.001f * Mathf.Pow(level, 1.5f),
                GrowthStatId.SmallLightUnlock => level > 0 ? 1f : 0f,
                GrowthStatId.SmallLightRange => 2f + 0.5f * Mathf.Pow(level, 2f / 3f),
                GrowthStatId.SmallLightAngle => 2.5f + 0.1f * Mathf.Pow(level, 2f / 3f),
                GrowthStatId.SmallLightShrink => Mathf.Pow(0.995f, level / 3f),
                _ => definition.BaseValue + definition.Step * level
            };
        }

        public int GetUpgradeCost(GrowthStatId id)
        {
            GrowthStatDefinition definition = Definitions[(int)id];
            long nextLevel = GetLevel(id) + 1L;
            long cost;
            if (id == GrowthStatId.PlayerMass)
            {
                cost = SaturatingMultiply(
                    definition.BaseCost,
                    SaturatingMultiply(nextLevel, nextLevel));
            }
            else if (id == GrowthStatId.SmallLightUnlock)
            {
                cost = definition.BaseCost;
            }
            else if (id == GrowthStatId.SmallLightRange ||
                id == GrowthStatId.SmallLightAngle)
            {
                cost = SaturatingMultiply(definition.BaseCost, nextLevel);
            }
            else if (id == GrowthStatId.SmallLightShrink)
            {
                double exponentialCost =
                    definition.BaseCost * Math.Pow(1.05d, GetLevel(id));
                cost = exponentialCost >= int.MaxValue
                    ? int.MaxValue
                    : (long)Math.Ceiling(exponentialCost);
            }
            else
            {
                cost = SaturatingMultiply(definition.BaseCost, nextLevel);
            }
            return (int)Math.Min(cost, int.MaxValue);
        }

        public bool CanUpgrade(GrowthStatId id)
        {
            GrowthStatDefinition definition = Definitions[(int)id];
            if ((id == GrowthStatId.SmallLightRange ||
                id == GrowthStatId.SmallLightAngle ||
                id == GrowthStatId.SmallLightShrink) &&
                GetLevel(GrowthStatId.SmallLightUnlock) <= 0)
            {
                return false;
            }
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
            if (id == GrowthStatId.SmallLightUnlock)
            {
                return GetLevel(id) > 0 ? "開放" : "未開放";
            }
            if (id == GrowthStatId.SmallLightShrink)
            {
                float linearMultiplier = GetValue(id);
                float areaMultiplier = linearMultiplier * linearMultiplier;
                return $"{linearMultiplier:0.######} / 面積 {areaMultiplier:0.######}";
            }
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
                GetValue(GrowthStatId.PlayerHitboxScale),
                GetValue(GrowthStatId.PlayerMass),
                GetLevel(GrowthStatId.SmallLightUnlock) > 0,
                GetValue(GrowthStatId.SmallLightRange),
                GetValue(GrowthStatId.SmallLightAngle),
                GetValue(GrowthStatId.SmallLightShrink));
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

        private static string FormatLinearValueFormula(GrowthStatDefinition definition)
        {
            if (Mathf.Approximately(definition.Step, 0f))
            {
                return definition.BaseValue.ToString("0.###");
            }

            string operation = definition.Step > 0f ? "+" : "-";
            return $"{definition.BaseValue:0.###} {operation} {Mathf.Abs(definition.Step):0.###} × L";
        }

        private static long SaturatingMultiply(long left, long right)
        {
            if (left <= 0L || right <= 0L)
            {
                return 0L;
            }
            return left > long.MaxValue / right ? long.MaxValue : left * right;
        }
    }
}
