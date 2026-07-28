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

        public bool Unlocked => unlocked;
        public float Range => range;
        public float AngleDegrees => angleDegrees;
        public float LinearMultiplierPerSecond => linearMultiplierPerSecond;

        private void OnValidate()
        {
            range = Mathf.Max(0f, range);
            angleDegrees = Mathf.Clamp(angleDegrees, 0f, 360f);
            linearMultiplierPerSecond = Mathf.Clamp01(linearMultiplierPerSecond);

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
