using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// クラック形成と縮小を同時に行う破砕弾の着弾計算用ステータスです。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BulletStatus : MonoBehaviour
    {
        [Header("Ballistics")]
        [SerializeField, Min(0f)] private float mass = 1f;
        [SerializeField] private Vector2 currentVelocity = new Vector2(0f, 12f);
        [SerializeField, Min(0f)] private float fireRate = 6.25f;
        [SerializeField, Min(1)] private int simultaneousShotCount = 1;

        [Header("Fracture")]
        [SerializeField, Range(0f, 1f)] private float crackConversionEfficiency = 0.5f;

        [Header("Erosion")]
        [SerializeField, Range(0f, 1f)]
        private float contactSizeMultiplier = 0.904382f;

        public float Mass => mass;
        public Vector2 CurrentVelocity => currentVelocity;
        public float FireRate => fireRate;
        public int SimultaneousShotCount => simultaneousShotCount;
        public float CrackConversionEfficiency => crackConversionEfficiency;

        /// <summary>
        /// 着弾ごとに対象へ適用する線形サイズ倍率です。
        /// 初期値は旧□弾の (1 - 10^-2)^10、約0.9044倍です。
        /// </summary>
        public float ContactSizeMultiplier => contactSizeMultiplier;

        /// <summary>成長画面で確定した弾ステータスを反映します。</summary>
        public void ApplyGrowthStatus(
            float newMass,
            float speed,
            float newFireRate,
            int newSimultaneousShotCount,
            float newCrackConversionEfficiency,
            float newContactSizeMultiplier)
        {
            mass = Mathf.Max(0f, newMass);
            Vector2 direction = currentVelocity.sqrMagnitude > 0.000001f
                ? currentVelocity.normalized
                : Vector2.up;
            currentVelocity = direction * Mathf.Max(0f, speed);
            fireRate = Mathf.Max(0f, newFireRate);
            simultaneousShotCount = Mathf.Max(1, newSimultaneousShotCount);
            crackConversionEfficiency = Mathf.Clamp01(newCrackConversionEfficiency);
            contactSizeMultiplier = Mathf.Clamp01(newContactSizeMultiplier);
        }

        public void CopyFrom(BulletStatus original)
        {
            mass = original.Mass;
            currentVelocity = original.CurrentVelocity;
            fireRate = original.FireRate;
            simultaneousShotCount = original.SimultaneousShotCount;
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
            simultaneousShotCount = Mathf.Max(1, simultaneousShotCount);
            contactSizeMultiplier = Mathf.Clamp01(contactSizeMultiplier);
        }
    }
}
