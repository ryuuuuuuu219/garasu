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

        public float Speed => speed;
        public float DestroyY => destroyY;

        private Rigidbody2D projectileRigidbody;
        private bool consumed;

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
            Vector2 velocity = Vector2.up * speed;
            if (TryGetComponent(out BulletStatus bulletStatus))
            {
                velocity = bulletStatus.CurrentVelocity;
            }

            Vector2 nextPosition = projectileRigidbody.position + velocity * Time.fixedDeltaTime;
            projectileRigidbody.MovePosition(nextPosition);

            if (nextPosition.y >= destroyY)
            {
                Destroy(gameObject);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!TryConsume(
                    other,
                    out CrackProcessingComponent crackTarget,
                    out GlassFragment fragmentTarget,
                    out BulletStatus bulletStatus))
            {
                return;
            }

            if (crackTarget != null)
            {
                crackTarget.HandleProjectileImpact(transform.position, bulletStatus);
            }
            else
            {
                fragmentTarget.ConsumeBullet(bulletStatus);
            }
            Destroy(gameObject);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!TryConsume(
                    collision.collider,
                    out CrackProcessingComponent crackTarget,
                    out GlassFragment fragmentTarget,
                    out BulletStatus bulletStatus))
            {
                return;
            }

            if (crackTarget != null)
            {
                Vector2 impactWorldPosition = collision.contactCount > 0
                    ? collision.GetContact(0).point
                    : (Vector2)transform.position;
                crackTarget.HandleBulletImpact(impactWorldPosition, bulletStatus);
            }
            else
            {
                fragmentTarget.ConsumeBullet(bulletStatus);
            }
            Destroy(gameObject);
        }

        private bool TryConsume(
            Collider2D other,
            out CrackProcessingComponent crackTarget,
            out GlassFragment fragmentTarget,
            out BulletStatus bulletStatus)
        {
            crackTarget = null;
            fragmentTarget = null;
            bulletStatus = null;
            if (consumed || other == null)
            {
                return false;
            }

            crackTarget = other.GetComponentInParent<CrackProcessingComponent>();
            if (crackTarget == null)
            {
                fragmentTarget = other.GetComponentInParent<GlassFragment>();
            }
            if ((crackTarget == null && fragmentTarget == null) ||
                !TryGetComponent(out bulletStatus))
            {
                return false;
            }

            // Destroyはフレーム終端まで遅延するため、対象処理より先に再入を禁止する。
            consumed = true;
            foreach (Collider2D projectileCollider in GetComponentsInChildren<Collider2D>())
            {
                projectileCollider.enabled = false;
            }
            return true;
        }
    }
}
