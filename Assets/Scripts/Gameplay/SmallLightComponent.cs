using UnityEngine;

namespace GlassShooter.Gameplay
{
    // UnityがMonoBehaviourと同名のスクリプト資産を認識できるよう、
    // 実装本体はPlayerShooterController.cs内のpartial宣言と共有します。
    public sealed partial class SmallLightComponent
    {
        [Header("Field")]
        [SerializeField] private bool unlocked;
        [SerializeField, Min(0f)] private float range = 2f;
        [SerializeField, Range(0f, 360f)] private float angleDegrees = 2.5f;

        [Header("Erosion")]
        [SerializeField, Range(0f, 1f)]
        private float linearMultiplierPerSecond = 1f;

        [Header("Impact Amplification")]
        [SerializeField, Min(1f)] private float impactMultiplier = 1f;

        public bool Unlocked => unlocked;
        public float Range => range;
        public float AngleDegrees => angleDegrees;
        public float LinearMultiplierPerSecond => linearMultiplierPerSecond;
        public float ImpactMultiplier => impactMultiplier;

        public static SmallLightComponent Active { get; private set; }

        public static float GetActiveImpactMultiplier(Vector2 worldPosition)
        {
            return Active != null ? Active.GetImpactMultiplier(worldPosition) : 1f;
        }

        public float GetImpactMultiplier(Vector2 worldPosition)
        {
            if (!unlocked || impactMultiplier <= 1f || range <= 0f || angleDegrees <= 0f)
            {
                return 1f;
            }

            Vector2 localPosition = transform.InverseTransformPoint(worldPosition);
            float distanceSquared = localPosition.sqrMagnitude;
            if (distanceSquared > range * range + 0.000001f)
            {
                return 1f;
            }
            if (distanceSquared <= 0.000001f || angleDegrees >= 360f)
            {
                return impactMultiplier;
            }

            float angleFromForward = Vector2.Angle(Vector2.up, localPosition);
            return angleFromForward <= angleDegrees * 0.5f + 0.0001f
                ? impactMultiplier
                : 1f;
        }

        private void OnValidate()
        {
            range = Mathf.Max(0f, range);
            angleDegrees = Mathf.Clamp(angleDegrees, 0f, 360f);
            linearMultiplierPerSecond = Mathf.Clamp01(linearMultiplierPerSecond);
            impactMultiplier = Mathf.Max(1f, impactMultiplier);

            if (!Application.isPlaying)
            {
                return;
            }

            EnsureComponents();
            RebuildFieldGeometry();
            ApplyActiveState();
        }
    }
}
