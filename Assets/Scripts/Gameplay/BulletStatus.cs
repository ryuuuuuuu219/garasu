using UnityEngine;

namespace GlassShooter.Gameplay
{
    public enum BulletType
    {
        CrackGenerator,
        CrackOpener
    }

    /// <summary>
    /// 企画書で定義された△弾／□弾の着弾計算用ステータスです。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BulletStatus : MonoBehaviour
    {
        [Header("Common")]
        [SerializeField] private BulletType bulletType = BulletType.CrackGenerator;
        [SerializeField, Min(0f)] private float mass = 1f;
        [SerializeField, Min(0f)] private float initialSpeed = 12f;
        [SerializeField] private Vector2 currentVelocity = new Vector2(0f, 12f);
        [SerializeField, Min(0f)] private float fireRate = 6.25f;
        [SerializeField, Min(1)] private int simultaneousShotCount = 1;

        [Header("Triangle Bullet - Crack Generator")]
        [SerializeField, Min(0.0001f)] private float tipCrossSectionArea = 0.05f;
        [SerializeField, Min(0.0001f)] private float stoppingDistance = 0.1f;
        [SerializeField, Range(0f, 1f)] private float crackConversionEfficiency = 0.5f;

        [Header("Square Bullet - Crack Opener")]
        [SerializeField] private Vector2 forceDirection = Vector2.up;
        [SerializeField, Min(0f)] private float effectRadius = 1f;
        [SerializeField, Min(0f)] private float distanceAttenuation = 1f;
        [SerializeField, Min(0f)] private float effectDuration = 0.2f;

        public BulletType Type => bulletType;
        public float Mass => mass;
        public float InitialSpeed => initialSpeed;
        public Vector2 CurrentVelocity => currentVelocity;
        public float FireRate => fireRate;
        public int SimultaneousShotCount => simultaneousShotCount;
        public float TipCrossSectionArea => tipCrossSectionArea;
        public float StoppingDistance => stoppingDistance;
        public float CrackConversionEfficiency => crackConversionEfficiency;
        public Vector2 ForceDirection => forceDirection;
        public float EffectRadius => effectRadius;
        public float DistanceAttenuation => distanceAttenuation;
        public float EffectDuration => effectDuration;

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
            initialSpeed = Mathf.Max(0f, initialSpeed);
            fireRate = Mathf.Max(0f, fireRate);
            simultaneousShotCount = Mathf.Max(1, simultaneousShotCount);
            tipCrossSectionArea = Mathf.Max(0.0001f, tipCrossSectionArea);
            stoppingDistance = Mathf.Max(0.0001f, stoppingDistance);
            effectRadius = Mathf.Max(0f, effectRadius);
            distanceAttenuation = Mathf.Max(0f, distanceAttenuation);
            effectDuration = Mathf.Max(0f, effectDuration);

            if (forceDirection.sqrMagnitude > 0f)
            {
                forceDirection.Normalize();
            }
        }

        public void ModeChange()
        {
            bulletType = 
            bulletType == BulletType.CrackGenerator ? 
            BulletType.CrackOpener : BulletType.CrackGenerator;
        }

    }
}
