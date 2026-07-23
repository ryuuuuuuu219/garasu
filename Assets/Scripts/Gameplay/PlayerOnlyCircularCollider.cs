using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// 所有者を中心とする円形の不可侵領域です。
    /// プレイヤーだけを円外へ押し戻し、弾やガラスの物理挙動には干渉しません。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerOnlyCircularCollider : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float radius = 6f;
        [SerializeField, Min(0f)] private float radiusOscillationAmplitude = 2f;
        [SerializeField, Min(0f)] private float playerImpulse = 2f;
        [SerializeField, HideInInspector] private CircleCollider2D areaCollider;

        public GameObject areaObject;
        public GameObject player;

        private EnemyDefeatComponent defeatState;
        private Rigidbody2D playerBody;
        private Collider2D playerCollider;

        public float Radius => radius;
        public CircleCollider2D AreaCollider => areaCollider;

        private void Awake()
        {
            defeatState = GetComponent<EnemyDefeatComponent>();
            if (defeatState != null)
            {
                defeatState.Defeated += DisableArea;
            }

            EnsureAreaCollider();
        }

        private void OnEnable()
        {
            EnsureAreaCollider();
            bool canActivate = defeatState == null || !defeatState.IsDefeated;
            areaCollider.gameObject.SetActive(canActivate);
            SyncAreaTransform();
        }

        private void FixedUpdate()
        {
            if (areaCollider == null ||
                defeatState != null && defeatState.IsDefeated)
            {
                return;
            }

            SyncAreaTransform();
            UpdateOscillatingRadius();
            ResolvePlayer();
            RepelPlayer();
        }

        public void SetRadius(float newRadius)
        {
            radius = Mathf.Max(0.01f, newRadius);
            EnsureAreaCollider();
            areaCollider.radius = radius;
        }

        private void EnsureAreaCollider()
        {
            if (areaCollider != null)
            {
                areaCollider.radius = radius;
                areaCollider.isTrigger = true;
                return;
            }

            areaObject = new GameObject(
                $"{name}_PlayerOnlyInviolableArea");
            areaObject.transform.position = transform.position;
            areaCollider = areaObject.AddComponent<CircleCollider2D>();
            areaCollider.radius = radius;
            areaCollider.isTrigger = true;
        }

        private void SyncAreaTransform()
        {
            if (areaCollider == null)
            {
                return;
            }

            Transform areaTransform = areaCollider.transform;
            areaTransform.position = transform.position;
            areaTransform.rotation = Quaternion.identity;
            areaTransform.localScale = Vector3.one;
        }

        private void ResolvePlayer()
        {
            if (player != null &&
                playerBody != null &&
                playerCollider != null &&
                playerBody.gameObject == player)
            {
                return;
            }

            if (player == null)
            {
                PlayerShooterController controller =
                    FindAnyObjectByType<PlayerShooterController>();
                player = controller != null ? controller.gameObject : null;
            }

            if (player == null)
            {
                playerBody = null;
                playerCollider = null;
                return;
            }

            playerBody = player.GetComponent<Rigidbody2D>();
            playerCollider = player.GetComponent<Collider2D>();
        }

        private void UpdateOscillatingRadius()
        {
            float currentRadius =
                radius + Mathf.Sin(Time.time) * radiusOscillationAmplitude;
            areaCollider.radius = Mathf.Max(0.01f, currentRadius);
        }

        private void RepelPlayer()
        {
            if (playerBody == null || playerCollider == null)
            {
                return;
            }

            Vector2 center = transform.position;
            Vector2 offset = playerBody.position - center;
            float playerClearance = playerCollider.bounds.extents.magnitude;
            float minimumDistance = areaCollider.radius + playerClearance;
            if (offset.sqrMagnitude >= minimumDistance * minimumDistance)
            {
                return;
            }

            Vector2 direction = offset.sqrMagnitude > Mathf.Epsilon
                ? offset.normalized
                : Vector2.down;
            playerBody.AddForce(
                direction * playerImpulse,
                ForceMode2D.Impulse);
        }

        private void DisableArea()
        {
            if (areaCollider != null)
            {
                areaCollider.gameObject.SetActive(false);
            }
            enabled = false;
        }

        private void OnDisable()
        {
            if (areaCollider != null)
            {
                areaCollider.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (defeatState != null)
            {
                defeatState.Defeated -= DisableArea;
            }
            if (areaCollider != null)
            {
                Destroy(areaCollider.gameObject);
            }
        }
    }
}
