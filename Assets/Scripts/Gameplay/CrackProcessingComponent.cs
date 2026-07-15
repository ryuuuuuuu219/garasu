using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// ガラスへの弾着を受け取り、今後のクラック生成処理へ橋渡しします。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrackProcessingComponent : MonoBehaviour
    {
        [Header("Glass References")]
        [SerializeField]
        [Tooltip("ガラス本体および、今後生成するクラックや破片の親です。")]
        private GameObject glassRoot = null;

        [SerializeField]
        [Tooltip("ガラス外周を描画するコンポーネントです。")]
        private GlassSurfaceLineRenderer outlineLineRenderer = null;

        [SerializeField]
        [Tooltip("現在のクラックを描画するコンポーネントです。")]
        private CrackLineRenderer crackLineRenderer = null;

        public GameObject GlassRoot => glassRoot;
        public GlassSurfaceLineRenderer OutlineLineRenderer => outlineLineRenderer;
        public CrackLineRenderer CrackLineRenderer => crackLineRenderer;

        private void Awake()
        {
            ResolveMissingReferences();
        }

        private void Reset()
        {
            glassRoot = gameObject;
            ResolveMissingReferences();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            BulletStatus bulletStatus = collision.collider.GetComponentInParent<BulletStatus>();
            if (bulletStatus == null)
            {
                return;
            }

            Vector2 impactWorldPosition = collision.contactCount > 0
                ? collision.GetContact(0).point
                : (Vector2)collision.transform.position;

            CompleteBulletImpact(impactWorldPosition, bulletStatus);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            BulletStatus bulletStatus = other.GetComponentInParent<BulletStatus>();
            if (bulletStatus == null)
            {
                return;
            }

            CompleteBulletImpact(other.transform.position, bulletStatus);
        }

        private void CompleteBulletImpact(Vector2 impactWorldPosition, BulletStatus bulletStatus)
        {
            HandleBulletImpact(impactWorldPosition, bulletStatus);
            Destroy(bulletStatus.gameObject);
        }

        /// <summary>
        /// 弾着座標と弾ステータスを受け取る入口です。
        /// クラック発生判定と成長処理は次段階でこの関数へ追加します。
        /// </summary>
        public void HandleBulletImpact(Vector2 impactWorldPosition, BulletStatus bulletStatus)
        {
            if (bulletStatus == null)
            {
                Debug.LogWarning("Bullet impact was ignored because BulletStatus was null.", this);
                return;
            }

            // TODO: 局所圧力判定、クラック成長予算、□弾の開口判定をここから呼び出す。
        }

        private void ResolveMissingReferences()
        {
            if (glassRoot == null)
            {
                glassRoot = gameObject;
            }

            if (outlineLineRenderer == null)
            {
                outlineLineRenderer = glassRoot.GetComponentInChildren<GlassSurfaceLineRenderer>(true);
            }

            if (crackLineRenderer == null)
            {
                crackLineRenderer = glassRoot.GetComponentInChildren<CrackLineRenderer>(true);
            }
        }
    }
}
