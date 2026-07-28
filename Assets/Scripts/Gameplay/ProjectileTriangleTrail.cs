using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// 弾の左右の軌跡点を記録し、左右交互の三角形コライダーをワールド上へ残します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileTriangleTrail : MonoBehaviour
    {
        private const float DirectionEpsilon = 0.000001f;

        [Header("Sampling")]
        [SerializeField, Min(0.01f)]
        private float halfWidth = 0.5f;

        [SerializeField, Min(0.001f)]
        private float sampleInterval = 0.1f;

        [Header("Collider")]
        [SerializeField]
        private Transform colliderRoot;

        [SerializeField, Min(0f)]
        private float minimumDoubleArea = 0.000001f;

        [SerializeField]
        private bool isTrigger;

        [Header("Debug")]
        [SerializeField]
        private bool drawDebugGizmos = true;

        [SerializeField, Min(0.001f)]
        private float debugPointRadius = 0.04f;

        [SerializeField]
        private Color leftPointColor = Color.cyan;

        [SerializeField]
        private Color rightPointColor = Color.magenta;

        [SerializeField]
        private Color triangleEdgeColor = Color.yellow;

        private readonly List<Vector2> L = new();
        private readonly List<Vector2> R = new();

        private BulletStatus bulletStatus;
        private Rigidbody2D projectileRigidbody;
        private Func<Vector2> velocityProvider;
        private Vector2 previousRecordedCenter;
        private Vector2 lastValidDirection = Vector2.up;
        private float sampleTimer;
        private bool ownsColliderRoot;

        public IReadOnlyList<Vector2> LeftPoints => L;
        public IReadOnlyList<Vector2> RightPoints => R;

        private void Awake()
        {
            TryGetComponent(out bulletStatus);
            TryGetComponent(out projectileRigidbody);
            InitializeTrail();
        }

        private void Update()
        {
            UpdateSampling();
        }

        /// <summary>
        /// 誘導処理などが保持する現在速度を、方向取得元として登録します。
        /// 実際に移動した区間がある場合は、その変位方向を優先します。
        /// </summary>
        public void SetVelocityProvider(Func<Vector2> provider)
        {
            velocityProvider = provider;
        }

        private void InitializeTrail()
        {
            L.Clear();
            R.Clear();
            sampleTimer = 0f;

            Vector2 center = transform.position;
            Vector2 direction = GetCurrentDirection();
            if (direction.sqrMagnitude > DirectionEpsilon)
            {
                lastValidDirection = direction.normalized;
            }

            CalculateLeftAndRight(
                center,
                lastValidDirection,
                out Vector2 initialLeft,
                out Vector2 initialRight);
            L.Add(initialLeft);
            R.Add(initialRight);
            previousRecordedCenter = center;
        }

        private void UpdateSampling()
        {
            sampleTimer += Time.deltaTime;

            while (sampleTimer >= sampleInterval)
            {
                sampleTimer -= sampleInterval;
                RecordNextPoints();
            }
        }

        private void RecordNextPoints()
        {
            if (L.Count != R.Count)
            {
                Debug.LogError(
                    "Projectile trail point lists became inconsistent. Sampling was stopped.",
                    this);
                enabled = false;
                return;
            }

            Vector2 currentCenter = transform.position;
            Vector2 movement = currentCenter - previousRecordedCenter;
            Vector2 direction;
            if (movement.sqrMagnitude > DirectionEpsilon)
            {
                direction = movement.normalized;
                lastValidDirection = direction;
            }
            else
            {
                direction = GetCurrentDirection();
                if (direction.sqrMagnitude > DirectionEpsilon)
                {
                    direction.Normalize();
                    lastValidDirection = direction;
                }
                else
                {
                    direction = lastValidDirection;
                }
            }

            int t = L.Count;
            CalculateLeftAndRight(
                currentCenter,
                direction,
                out Vector2 newLeft,
                out Vector2 newRight);
            L.Add(newLeft);
            R.Add(newRight);
            previousRecordedCenter = currentCenter;

            CreateTriangleForIndex(t);
        }

        private void CalculateLeftAndRight(
            Vector2 center,
            Vector2 direction,
            out Vector2 left,
            out Vector2 right)
        {
            Vector2 safeDirection = direction.sqrMagnitude > DirectionEpsilon
                ? direction.normalized
                : Vector2.up;
            Vector2 normal = new Vector2(-safeDirection.y, safeDirection.x);
            left = center + normal * halfWidth;
            right = center - normal * halfWidth;
        }

        private void CreateTriangleForIndex(int t)
        {
            if (t <= 0 || L.Count != R.Count || t >= L.Count)
            {
                return;
            }

            if (t == 1)
            {
                CreateTriangleCollider(L[0], L[1], R[1]);
                return;
            }

            if (t - 2 < 0 || t - 1 < 0)
            {
                return;
            }

            if (t % 2 == 1)
            {
                CreateTriangleCollider(L[t - 2], L[t], R[t - 1]);
            }
            else
            {
                CreateTriangleCollider(R[t - 2], R[t], L[t - 1]);
            }
        }

        private void CreateTriangleCollider(
            Vector2 worldA,
            Vector2 worldB,
            Vector2 worldC)
        {
            float cross = Cross(worldA, worldB, worldC);
            if (Mathf.Abs(cross) < minimumDoubleArea)
            {
                return;
            }

            if (cross < 0f)
            {
                (worldB, worldC) = (worldC, worldB);
            }

            Transform root = GetOrCreateColliderRoot();
            GameObject triangleObject = new GameObject(
                $"TrailTriangle_{root.childCount:0000}");
            Transform triangleTransform = triangleObject.transform;
            triangleTransform.SetParent(root, false);
            triangleTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            triangleTransform.localScale = Vector3.one;

            Vector2 localA = triangleTransform.InverseTransformPoint(worldA);
            Vector2 localB = triangleTransform.InverseTransformPoint(worldB);
            Vector2 localC = triangleTransform.InverseTransformPoint(worldC);

            PolygonCollider2D triangleCollider =
                triangleObject.AddComponent<PolygonCollider2D>();
            triangleCollider.isTrigger = isTrigger;
            triangleCollider.points = new[] { localA, localB, localC };
        }

        private Transform GetOrCreateColliderRoot()
        {
            if (colliderRoot != null && !colliderRoot.IsChildOf(transform))
            {
                return colliderRoot;
            }

            if (colliderRoot != null)
            {
                Debug.LogWarning(
                    "The assigned trail collider root belongs to the projectile. " +
                    "A world-space root was created instead.",
                    this);
            }

            GameObject rootObject = new GameObject(
                $"{gameObject.name}_TriangleTrail");
            colliderRoot = rootObject.transform;
            colliderRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            colliderRoot.localScale = Vector3.one;
            ownsColliderRoot = true;
            return colliderRoot;
        }

        private Vector2 GetCurrentDirection()
        {
            if (velocityProvider != null)
            {
                Vector2 providedVelocity = velocityProvider.Invoke();
                if (providedVelocity.sqrMagnitude > DirectionEpsilon)
                {
                    return providedVelocity.normalized;
                }
            }

            if ((bulletStatus != null || TryGetComponent(out bulletStatus)) &&
                bulletStatus.CurrentVelocity.sqrMagnitude > DirectionEpsilon)
            {
                return bulletStatus.CurrentVelocity.normalized;
            }

            if (projectileRigidbody != null &&
                projectileRigidbody.linearVelocity.sqrMagnitude > DirectionEpsilon)
            {
                return projectileRigidbody.linearVelocity.normalized;
            }

            Vector2 transformDirection = transform.up;
            return transformDirection.sqrMagnitude > DirectionEpsilon
                ? transformDirection.normalized
                : lastValidDirection;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            return ab.x * ac.y - ab.y * ac.x;
        }

        private void OnDestroy()
        {
            if (ownsColliderRoot &&
                colliderRoot != null &&
                colliderRoot.childCount == 0)
            {
                Destroy(colliderRoot.gameObject);
            }
        }

        private void OnValidate()
        {
            halfWidth = Mathf.Max(0.01f, halfWidth);
            sampleInterval = Mathf.Max(0.001f, sampleInterval);
            minimumDoubleArea = Mathf.Max(0f, minimumDoubleArea);
            debugPointRadius = Mathf.Max(0.001f, debugPointRadius);
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos || L.Count != R.Count)
            {
                return;
            }

            for (int i = 0; i < L.Count; i++)
            {
                Gizmos.color = leftPointColor;
                Gizmos.DrawSphere(L[i], debugPointRadius);
                Gizmos.color = rightPointColor;
                Gizmos.DrawSphere(R[i], debugPointRadius);
            }

            Gizmos.color = triangleEdgeColor;
            for (int t = 1; t < L.Count; t++)
            {
                if (t == 1)
                {
                    DrawTriangleEdges(L[0], L[1], R[1]);
                }
                else if (t % 2 == 1)
                {
                    DrawTriangleEdges(L[t - 2], L[t], R[t - 1]);
                }
                else
                {
                    DrawTriangleEdges(R[t - 2], R[t], L[t - 1]);
                }
            }
        }

        private static void DrawTriangleEdges(
            Vector2 a,
            Vector2 b,
            Vector2 c)
        {
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, a);
        }
    }
}
