using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// 速度を一定に保ちながら、目標の現在位置へ向けて純粋追尾します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PurePursuitHomingProjectile : MonoBehaviour
    {
        private const float DirectionEpsilon = 0.000001f;

        [SerializeField, Min(0.01f)]
        private float lifetime = 5f;

        [SerializeField, Min(0f)]
        private float turnRateDecay = 40f;

        private Rigidbody2D projectileRigidbody;
        private Transform target;
        private float speed;
        private float maximumTurnRateDegrees;
        private float currentTurnRate;
        private float destructionTime;

        public Vector2 CurrentVelocity =>
            projectileRigidbody != null
                ? projectileRigidbody.linearVelocity
                : Vector2.zero;

        private void Awake()
        {
            projectileRigidbody = GetComponent<Rigidbody2D>();
            destructionTime = Time.time + lifetime;
        }

        public void Initialize(
            Transform newTarget,
            float newSpeed,
            float newMaximumTurnRateDegrees)
        {
            RemoveProjectileColliders();
            target = newTarget;
            speed = Mathf.Max(0.01f, newSpeed);
            maximumTurnRateDegrees = Mathf.Max(
                0f,
                newMaximumTurnRateDegrees);
            currentTurnRate = maximumTurnRateDegrees;
            destructionTime = Time.time + lifetime;
        }

        private void RemoveProjectileColliders()
        {
            Collider2D[] projectileColliders = GetComponents<Collider2D>();
            for (int i = 0; i < projectileColliders.Length; i++)
            {
                Collider2D projectileCollider = projectileColliders[i];
                if (projectileCollider == null)
                {
                    continue;
                }

                // Destroyはフレーム終端まで遅延するため、先に無効化して
                // 同じフレームに生成された軌跡との接触も防止する。
                projectileCollider.enabled = false;
                Destroy(projectileCollider);
            }
        }

        private void FixedUpdate()
        {
            if (Time.time >= destructionTime)
            {
                Destroy(gameObject);
                return;
            }

            if (target == null)
            {
                return;
            }

            Vector2 desiredDirection =
                (Vector2)target.position - projectileRigidbody.position;
            if (desiredDirection.sqrMagnitude <= DirectionEpsilon)
            {
                return;
            }
            desiredDirection.Normalize();

            Vector2 currentDirection = projectileRigidbody.linearVelocity;
            if (currentDirection.sqrMagnitude <= DirectionEpsilon)
            {
                currentDirection = desiredDirection;
            }
            else
            {
                currentDirection.Normalize();
            }

            float signedTurn = Vector2.SignedAngle(
                currentDirection,
                desiredDirection);
            float allowedTurn = currentTurnRate * Time.fixedDeltaTime;
            float appliedTurn = Mathf.Clamp(
                signedTurn,
                -allowedTurn,
                allowedTurn);
            Vector2 nextDirection =
                Rotate(currentDirection, appliedTurn).normalized;

            projectileRigidbody.linearVelocity = nextDirection * speed;
            projectileRigidbody.SetRotation(
                Mathf.Atan2(nextDirection.y, nextDirection.x) *
                Mathf.Rad2Deg - 90f);
            currentTurnRate = Mathf.Max(
                0f,
                currentTurnRate - turnRateDecay * Time.fixedDeltaTime);
        }

        private void OnValidate()
        {
            turnRateDecay = Mathf.Max(0f, turnRateDecay);
            lifetime = Mathf.Max(0.01f, lifetime);
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                vector.x * cosine - vector.y * sine,
                vector.x * sine + vector.y * cosine);
        }
    }
}
