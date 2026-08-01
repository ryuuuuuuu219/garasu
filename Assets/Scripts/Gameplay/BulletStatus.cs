using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// 武器の種類やMonoBehaviourに依存しない、着弾1回分の入力値です。
    /// サーチライト倍率と敵倍率は命中対象側で解決します。
    /// </summary>
    public readonly struct ImpactEnergyContext
    {
        public ImpactEnergyContext(
            Vector2 worldPosition,
            Vector2 impactVelocity,
            float impactEnergy,
            float weaponEfficiency,
            float contactSizeMultiplier = 1f)
        {
            WorldPosition = worldPosition;
            ImpactVelocity = impactVelocity;
            ImpactEnergy = Mathf.Max(0f, impactEnergy);
            WeaponEfficiency = Mathf.Max(0f, weaponEfficiency);
            ContactSizeMultiplier = Mathf.Clamp01(contactSizeMultiplier);
        }

        public Vector2 WorldPosition { get; }
        public Vector2 ImpactVelocity { get; }
        public float ImpactEnergy { get; }
        public float WeaponEfficiency { get; }
        public float ContactSizeMultiplier { get; }

        public static ImpactEnergyContext FromBullet(
            Vector2 worldPosition,
            BulletStatus bulletStatus)
        {
            return bulletStatus == null
                ? default
                : new ImpactEnergyContext(
                    worldPosition,
                    bulletStatus.CurrentVelocity,
                    bulletStatus.CalculateKineticEnergy(),
                    bulletStatus.CrackConversionEfficiency,
                    bulletStatus.ContactSizeMultiplier);
        }
    }

    /// <summary>
    /// クラック形成と縮小を同時に行う破砕弾の着弾計算用ステータスです。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BulletStatus : MonoBehaviour
    {
        [Header("Ballistics")]
        [SerializeField, Min(0f)] private float mass = 0.1f;
        [SerializeField] private Vector2 currentVelocity = new Vector2(0f, 4.5f);
        [SerializeField, Min(0f)] private float fireRate = 1f;

        [Header("Fracture")]
        [SerializeField, Min(0f)] private float crackConversionEfficiency = 0.05f;

        [Header("Erosion")]
        [SerializeField, Range(0f, 1f)]
        private float contactSizeMultiplier = 1f;

        public float Mass => mass;
        public Vector2 CurrentVelocity => currentVelocity;
        public float FireRate => fireRate;
        public float CrackConversionEfficiency => crackConversionEfficiency;

        /// <summary>
        /// 着弾ごとに対象へ適用する線形サイズ倍率です。
        /// 初期値は成長ステータスのレベル0と同じ1倍です。
        /// </summary>
        public float ContactSizeMultiplier => contactSizeMultiplier;

        private void Start()
        {
            // 発射済みの弾はPlayerShooterControllerから現在値をコピーされるため、
            // ここで保存値を再適用してコピー内容を上書きしない。
            if (!TryGetComponent<Projectile>(out _))
            {
                GrowthStatusComponent.Instance.ApplyTo(this);
            }
        }

        /// <summary>成長画面で確定した弾ステータスを反映します。</summary>
        public void ApplyGrowthStatus(
            float newMass,
            float speed,
            float newFireRate,
            float newCrackConversionEfficiency,
            float newContactSizeMultiplier)
        {
            mass = Mathf.Max(0f, newMass);
            Vector2 direction = currentVelocity.sqrMagnitude > 0.000001f
                ? currentVelocity.normalized
                : Vector2.up;
            currentVelocity = direction * Mathf.Max(0f, speed);
            fireRate = Mathf.Max(0f, newFireRate);
            crackConversionEfficiency = Mathf.Max(0f, newCrackConversionEfficiency);
            contactSizeMultiplier = Mathf.Clamp01(newContactSizeMultiplier);
        }

        public void CopyFrom(BulletStatus original)
        {
            mass = original.Mass;
            currentVelocity = original.CurrentVelocity;
            fireRate = original.FireRate;
            crackConversionEfficiency = original.CrackConversionEfficiency;
            contactSizeMultiplier = original.ContactSizeMultiplier;
        }

        /// <summary>着弾直前の速度を弾の移動処理から反映します。</summary>
        public void SetCurrentVelocity(Vector2 velocity)
        {
            currentVelocity = velocity;
        }

        /// <summary>現在速度を使って運動エネルギー 1/2 mv^2 を返します。</summary>
        public float CalculateKineticEnergy()
        {
            return 0.5f * mass * currentVelocity.sqrMagnitude;
        }

        private void OnValidate()
        {
            mass = Mathf.Max(0f, mass);
            fireRate = Mathf.Max(0f, fireRate);
            crackConversionEfficiency = Mathf.Max(0f, crackConversionEfficiency);
            contactSizeMultiplier = Mathf.Clamp01(contactSizeMultiplier);
        }
    }
}
