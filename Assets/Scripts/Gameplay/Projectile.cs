using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// 画面上方へ直進し、表示範囲外で破棄されるプロトタイプ用の弾です。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float speed = 12f;
        [SerializeField] private float destroyY = 6.5f;

        private Rigidbody2D projectileRigidbody;

        private void Awake()
        {
            if (!TryGetComponent(out projectileRigidbody))
            {
                projectileRigidbody = gameObject.AddComponent<Rigidbody2D>();
            }

            projectileRigidbody.bodyType = RigidbodyType2D.Kinematic;
            projectileRigidbody.gravityScale = 0f;
            projectileRigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            projectileRigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        private void FixedUpdate()
        {
            Vector2 nextPosition = projectileRigidbody.position + Vector2.up * (speed * Time.fixedDeltaTime);
            projectileRigidbody.MovePosition(nextPosition);

            if (nextPosition.y >= destroyY)
            {
                Destroy(gameObject);
            }
        }
    }
}
