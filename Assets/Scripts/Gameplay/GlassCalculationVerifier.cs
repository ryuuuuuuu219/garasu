using System.Text;
using GlassShooter.Gameplay;
using UnityEngine;

namespace Gameplay
{
    /// <summary>弾、敵形状、クラック走査、成長予定値をInspector上で検算します。</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(enemyspowner))]
    [AddComponentMenu("Scripts/Gameplay/Glass Calculation Verifier")]
    public sealed class GlassCalculationVerifier : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerShooterController player = null;
        [SerializeField] private BulletStatus bullet = null;
        [SerializeField] private CrackProcessingComponent crackSettingsSource = null;
        [SerializeField] private EnvironmentManager environment = null;

        [Header("Calculated Report")]
        [SerializeField, TextArea(18, 40)] private string report = string.Empty;

        public string Report => report;

        private void Reset()
        {
            FindReferences();
            RefreshReport();
        }

        private void OnEnable()
        {
            FindReferences();
            RefreshReport();
        }

        private void OnValidate()
        {
            FindReferences();
            RefreshReport();
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                RefreshReport();
            }
        }

        [ContextMenu("検算結果を更新")]
        public void RefreshReport()
        {
            if (!TryGetComponent(out enemyspowner spawner))
            {
                report = "enemyspownerが見つかりません。";
                return;
            }

            BulletStatus sourceBullet = bullet != null
                ? bullet
                : player != null
                    ? player.BulletStatus
                    : null;
            GlassStatus glass = spawner.glassStatus;
            if (sourceBullet == null || glass == null)
            {
                report = $"参照不足: BulletStatus={(sourceBullet != null)}, GlassStatus={(glass != null)}";
                return;
            }

            Projectile projectile = player != null ? player.ProjectilePrefab : null;
            float actualSpeed = projectile != null
                ? projectile.Speed
                : sourceBullet.CurrentVelocity.magnitude;
            float minimumImpactEnergy = CalculateConvertedEnergy(
                sourceBullet.Mass,
                actualSpeed,
                sourceBullet.CrackConversionEfficiency);

            float maximumArea = spawner.CalculateMaximumPatternArea();
            float characteristicLength = Mathf.Sqrt(maximumArea);
            float resistance = crackSettingsSource != null
                ? crackSettingsSource.BaseFractureResistance
                : 1f;
            float minimumRadius = crackSettingsSource != null
                ? crackSettingsSource.MinimumScanRadius
                : 0.1f;
            float maximumRadius = crackSettingsSource != null
                ? crackSettingsSource.MaximumScanRadius
                : 20f;
            float rawScanRadius = characteristicLength * Mathf.Sqrt(
                minimumImpactEnergy / Mathf.Max(resistance, 0.0001f));
            float appliedScanRadius = Mathf.Clamp(rawScanRadius, minimumRadius, maximumRadius);
            int maximumFragments = spawner.CalculateMaximumFragmentUpperBound(
                glass.MinimumBreakableArea);

            var builder = new StringBuilder(2048);
            builder.AppendLine("【弾】")
                .AppendLine("種類: 破砕弾（クラック形成＋縮小）")
                .AppendLine($"質量: {sourceBullet.Mass:0.###}")
                .AppendLine($"Projectile実移動速度: {actualSpeed:0.###}")
                .AppendLine($"変換効率: {sourceBullet.CrackConversionEfficiency:0.###}")
                .AppendLine($"最小位置Y: {(player != null ? player.MoveLimitMin.y : 0f):0.###}")
                .AppendLine($"最小着弾エネルギー: {minimumImpactEnergy:0.###}（現在は等速なのでY非依存）");

            builder.AppendLine()
                .AppendLine("【走査半径】")
                .AppendLine($"最大ガラス面積: {maximumArea:0.###}")
                .AppendLine($"面積平方根（代表長さ）: {characteristicLength:0.###}")
                .AppendLine($"基準破壊抵抗: {resistance:0.###}")
                .AppendLine($"未クランプ走査半径: {rawScanRadius:0.###}")
                .AppendLine($"適用走査半径: {appliedScanRadius:0.###}（{minimumRadius:0.###}～{maximumRadius:0.###}）")
                .AppendLine($"代表長さ以上: {minimumImpactEnergy + 0.0001f >= resistance}")
                .AppendLine("条件は『衝撃エネルギー >= 基準破壊抵抗』。外周到達は実交点距離で別判定。")
                .AppendLine()
                .AppendLine("【敵スポーン】")
                .AppendLine($"現在パターンID: {spawner.CurrentPatternId}")
                .AppendLine($"最終生成敵ID: {spawner.LastSpawnedEnemyId}")
                .AppendLine("構成:")
                .AppendLine(spawner.BuildPatternCompositionSummary())
                .AppendLine($"最大ケース破片数（面積による理論上限）: {maximumFragments}")
                .AppendLine()
                .AppendLine("【成長予定・調整対象】")
                .AppendLine($"プレイヤー移動速度: {(player != null ? player.MoveSpeed : 0f):0.###}")
                .AppendLine($"移動範囲: {(player != null ? player.MoveLimitMin : Vector2.zero)} ～ {(player != null ? player.MoveLimitMax : Vector2.zero)}")
                .AppendLine($"実発射間隔: {(player != null ? player.FireInterval : 0f):0.###}")
                .AppendLine($"発射レート: {sourceBullet.FireRate:0.###}")
                .AppendLine($"同時発射数: {sourceBullet.SimultaneousShotCount}")
                .AppendLine($"着弾時の接触サイズ倍率: {sourceBullet.ContactSizeMultiplier:0.####}")
                .AppendLine($"厚さ / 密度 / 質量: {glass.Thickness:0.###} / {glass.Density:0.###} / {glass.Mass:0.###}")
                .AppendLine($"初期亀裂数 / 仮想点数: {glass.InitialCrackCount} / {glass.VirtualPointCount}")
                .AppendLine($"脆弱度範囲: {glass.MinimumInitialVulnerability:0.###}～{glass.MaximumInitialVulnerability:0.###}")
                .AppendLine($"形状: {glass.OutlineShape}")
                .AppendLine($"固定点数 / コア位置 / 固定強度: {glass.FixedPositions.Length} / {glass.CorePosition} / {glass.FixedPositionStrength:0.###}")
                .AppendLine($"破片攻撃倍率 / 落下速度倍率: {glass.FragmentAttackMultiplier:0.###} / {glass.FragmentFallSpeedMultiplier:0.###}")
                .AppendLine($"最小破壊面積: {glass.MinimumBreakableArea:0.###}")
                .AppendLine($"重力倍率 / 弾性: {glass.GravityMultiplier:0.###} / {glass.Elasticity:0.###}")
                .AppendLine($"全体拡大率 / 環境重力倍率 / 有効重力: {(environment != null ? environment.GlobalScaleMultiplier : 1f):0.###} / {(environment != null ? environment.GravityMultiplier : 0.1f):0.###} / {(environment != null ? environment.EffectiveGravity : Physics2D.gravity)}");

            report = builder.ToString();
        }

        private void FindReferences()
        {
            player ??= FindAnyObjectByType<PlayerShooterController>();
            bullet ??= player != null ? player.BulletStatus : FindAnyObjectByType<BulletStatus>();
            crackSettingsSource ??= GetComponentInChildren<CrackProcessingComponent>(true);
            environment ??= FindAnyObjectByType<EnvironmentManager>();
        }

        private static float CalculateConvertedEnergy(float mass, float speed, float efficiency)
        {
            return 0.5f * Mathf.Max(0f, mass) * speed * speed * Mathf.Clamp01(efficiency);
        }
    }
}
