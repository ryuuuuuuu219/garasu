using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>生成済み破片の寿命管理です。落下と回転は Rigidbody2D が担当します。</summary>
    [DisallowMultipleComponent]
    internal sealed class GlassFragment : MonoBehaviour
    {
        [SerializeField] private float destroyBelowY = -8f;

        private void Update()
        {
            if (transform.position.y <= destroyBelowY)
            {
                if (TryGetComponent(out GlassStatus status))
                {
                    status.DestroyGlass();
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }

        public void ConsumeBullet(BulletStatus bulletStatus)
        {
            if (bulletStatus == null)
            {
                return;
            }

            ConsumeImpact(ImpactEnergyContext.FromBullet(transform.position, bulletStatus));
        }

        public void ConsumeImpact(in ImpactEnergyContext context)
        {
            float multiplier = context.ContactSizeMultiplier;
            if (!Mathf.Approximately(multiplier, 1f))
            {
                Destroy(gameObject);
            }
        }
    }
}
