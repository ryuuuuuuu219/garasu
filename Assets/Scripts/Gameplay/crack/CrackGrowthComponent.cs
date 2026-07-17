using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>着弾受付とクラック成長処理を担当する機能コンポーネントです。</summary>
    [DisallowMultipleComponent]
    public sealed class CrackGrowthComponent : MonoBehaviour
    {
        [SerializeField] private CrackProcessingComponent processing = null;

        internal void Bind(CrackProcessingComponent owner)
        {
            processing = owner;
        }

        internal bool HealCracks()
        {
            return Processing.HealCracksCore();
        }

        internal void HandleProjectileImpact(Vector2 projectileWorldPosition, BulletStatus bulletStatus)
        {
            Processing.HandleProjectileImpactCore(projectileWorldPosition, bulletStatus);
        }

        internal void HandleBulletImpact(Vector2 impactWorldPosition, BulletStatus bulletStatus)
        {
            Processing.HandleBulletImpactCore(impactWorldPosition, bulletStatus);
        }

        private CrackProcessingComponent Processing
        {
            get
            {
                if (processing == null)
                {
                    processing = GetComponent<CrackProcessingComponent>();
                }

                return processing;
            }
        }
    }
}
