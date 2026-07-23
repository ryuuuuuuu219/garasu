using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// 所有者を中心とする円形の不可侵領域です。
    /// プレイヤーが範囲内へ入ると、速度を外向きへ上書きします。
    /// 弾やガラスの物理挙動には干渉しません。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerOnlyCircularCollider : MonoBehaviour
    {
        private const int BoundarySegmentCount = 96;
        private const float BoundaryLineWidth = 0.04f;

        [SerializeField, Min(0.01f)] private float radius = 6f;
        [SerializeField, Min(0f)] private float radiusOscillationAmplitude = 2f;
        [UnityEngine.Serialization.FormerlySerializedAs("playerImpulse")]
        [SerializeField, Min(0f)] private float repulsionSpeed = 2f;
        [SerializeField, Min(0f)] private float releaseMargin = 0.1f;

        public GameObject player;

        private EnemyDefeatComponent defeatState;
        private PlayerShooterController playerController;
        private Rigidbody2D playerBody;
        private LineRenderer boundaryLine;
        private Material boundaryMaterial;
        private bool isRepelling;

        public float Radius => radius;

        private void Awake()
        {
            defeatState = GetComponent<EnemyDefeatComponent>();
            if (defeatState != null)
            {
                defeatState.Defeated += DisableArea;
            }

            CreateBoundaryLine();
        }

        private void OnEnable()
        {
            bool canActivate = defeatState == null || !defeatState.IsDefeated;
            if (boundaryLine != null)
            {
                boundaryLine.enabled = canActivate;
            }
            enabled = canActivate;
            isRepelling = false;
        }

        private void Update()
        {
            UpdateBoundaryLine(CalculateCurrentRadius());
        }

        private void FixedUpdate()
        {
            if (defeatState != null && defeatState.IsDefeated)
            {
                return;
            }

            ResolvePlayer();
            RepelPlayer(CalculateCurrentRadius());
        }

        public void SetRadius(float newRadius)
        {
            radius = Mathf.Max(0.01f, newRadius);
            UpdateBoundaryLine(CalculateCurrentRadius());
        }

        private void CreateBoundaryLine()
        {
            GameObject lineObject = new GameObject("InviolableAreaBoundary");
            lineObject.transform.SetParent(transform, false);

            boundaryLine = lineObject.AddComponent<LineRenderer>();
            boundaryLine.useWorldSpace = true;
            boundaryLine.loop = true;
            boundaryLine.positionCount = BoundarySegmentCount;
            boundaryLine.startWidth = BoundaryLineWidth;
            boundaryLine.endWidth = BoundaryLineWidth;
            boundaryLine.startColor = Color.white;
            boundaryLine.endColor = Color.white;
            boundaryLine.sortingOrder = 10;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                boundaryMaterial = new Material(shader);
                boundaryLine.sharedMaterial = boundaryMaterial;
            }

            UpdateBoundaryLine(CalculateCurrentRadius());
        }

        private void UpdateBoundaryLine(float currentRadius)
        {
            if (boundaryLine == null)
            {
                return;
            }

            Vector3 center = transform.position;
            for (int index = 0; index < BoundarySegmentCount; index++)
            {
                float angle =
                    2f * Mathf.PI * index / BoundarySegmentCount;
                Vector3 point = center + new Vector3(
                    Mathf.Cos(angle) * currentRadius,
                    Mathf.Sin(angle) * currentRadius,
                    0f);
                boundaryLine.SetPosition(index, point);
            }
        }

        private void ResolvePlayer()
        {
            if (player != null &&
                playerBody != null &&
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
                playerController = null;
                isRepelling = false;
                return;
            }

            playerController = player.GetComponent<PlayerShooterController>();
            playerBody = player.GetComponent<Rigidbody2D>();
        }

        private float CalculateCurrentRadius()
        {
            return Mathf.Max(
                0.01f,
                radius + Mathf.Sin(Time.time) * radiusOscillationAmplitude);
        }

        private void RepelPlayer(float currentRadius)
        {
            if (playerBody == null)
            {
                return;
            }

            Vector2 center = transform.position;
            Vector2 offset = playerBody.position - center;
            float distance = offset.magnitude;

            if (!isRepelling && distance < currentRadius)
            {
                isRepelling = true;
            }
            else if (isRepelling &&
                distance >= currentRadius + releaseMargin)
            {
                isRepelling = false;
            }

            if (!isRepelling)
            {
                return;
            }

            Vector2 direction = distance > Mathf.Epsilon
                ? offset / distance
                : Vector2.down;
            Vector2 repulsionVelocity = direction * repulsionSpeed;

            if (playerController != null)
            {
                playerController.OverrideMovementVelocity(repulsionVelocity);
            }
            else
            {
                playerBody.linearVelocity = repulsionVelocity;
            }
        }

        private void DisableArea()
        {
            isRepelling = false;
            enabled = false;
        }

        private void OnDisable()
        {
            isRepelling = false;
            if (boundaryLine != null)
            {
                boundaryLine.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (defeatState != null)
            {
                defeatState.Defeated -= DisableArea;
            }
            if (boundaryMaterial != null)
            {
                Destroy(boundaryMaterial);
            }
        }
    }
}
