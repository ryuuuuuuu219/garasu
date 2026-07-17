using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>生成済み破片の寿命管理です。落下と回転は Rigidbody2D が担当します。</summary>
    [DisallowMultipleComponent]
    internal sealed class GlassFragment : MonoBehaviour
    {
        [SerializeField] private float destroyBelowY = -8f;

        public void ConsumeBullet(BulletStatus bulletStatus)
        {
            if (bulletStatus == null)
            {
                return;
            }

            float multiplier = bulletStatus.ContactSizeMultiplier;
            if (!Mathf.Approximately(multiplier, 1f))
            {
                Destroy(gameObject);
            }
        }
    }
}
