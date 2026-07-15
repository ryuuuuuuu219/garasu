using System;
using System.Collections.Generic;
using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// ガラスの外周とクラックを保持し、外周同士を結ぶクラックから破片を生成します。
    /// すべての形状データはガラスのローカル座標で扱います。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrackProcessingComponent : MonoBehaviour
    {
        [Header("Glass References")]
        [SerializeField] private GameObject glassRoot = null;
        [SerializeField] private GlassSurfaceLineRenderer outlineLineRenderer = null;
        [SerializeField] private CrackLineRenderer crackLineRenderer = null;

        [Header("Geometry (Local Space)")]
        [SerializeField] private Vector2[] outline = Array.Empty<Vector2>();
        [SerializeField] private Vector2[] initCrackPoint = Array.Empty<Vector2>();

        // Unityはジャグ配列をシリアライズしないため、クラックは実行時データとして保持します。
        private Vector2[][] cracks = Array.Empty<Vector2[]>();

        private const float GeometryEpsilon = 0.0001f;

        public GameObject GlassRoot => glassRoot;
        public GlassSurfaceLineRenderer OutlineLineRenderer => outlineLineRenderer;
        public CrackLineRenderer CrackLineRenderer => crackLineRenderer;
        public Vector2[] Outline => (Vector2[])outline.Clone();
        public Vector2[] InitialCrackPoints => (Vector2[])initCrackPoint.Clone();
        public IReadOnlyList<Vector2[]> Cracks => cracks;

        private readonly struct BoundaryLocation
        {
            public BoundaryLocation(int edgeIndex, float edgeT, Vector2 point)
            {
                EdgeIndex = edgeIndex;
                EdgeT = edgeT;
                Point = point;
            }

            public int EdgeIndex { get; }
            public float EdgeT { get; }
            public Vector2 Point { get; }
        }

        private void Awake()
        {
            ResolveMissingReferences();
            EnsureGeometryInitialized();
            RenderCracks();
        }

        private void Reset()
        {
            glassRoot = gameObject;
            ResolveMissingReferences();
            EnsureGeometryInitialized();
        }

        /// <summary>スポーン時に外周とクラックを安全に設定します。</summary>
        public void Initialize(Vector2[] outlinePoints, Vector2[][] crackPaths = null)
        {
            if (outlinePoints == null || outlinePoints.Length < 3)
            {
                throw new ArgumentException("Glass outline requires at least three points.", nameof(outlinePoints));
            }

            ResolveMissingReferences();
            outline = CleanPolygon(outlinePoints);
            cracks = CloneCracks(crackPaths);

            if (outlineLineRenderer != null)
            {
                outlineLineRenderer.SetOutline(outline);
            }

            if (TryGetComponent(out PolygonCollider2D collider))
            {
                collider.points = outline;
            }

            RenderCracks();
        }

        public void SetCracks(Vector2[][] crackPaths)
        {
            cracks = CloneCracks(crackPaths);
            RenderCracks();
        }

        private static Vector2[][] CloneCracks(Vector2[][] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<Vector2[]>();
            }

            var result = new Vector2[source.Length][];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i] == null ? Array.Empty<Vector2>() : (Vector2[])source[i].Clone();
            }
            return result;
        }

        private void RenderCracks()
        {
            if (crackLineRenderer != null)
            {
                crackLineRenderer.SetCracks(cracks);
            }
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
            RenderCracks();
        }

        public void HandleBulletImpact(Vector2 impactWorldPosition, BulletStatus bulletStatus)
        {
            if (bulletStatus == null)
            {
                Debug.LogWarning("Bullet impact was ignored because BulletStatus was null.", this);
                return;
            }

            // 以降のクラック計算は必ずローカル座標を使う。
            Vector2 impactLocalPosition = transform.InverseTransformPoint(impactWorldPosition);

            // TODO: 局所圧力判定、クラック成長予算、□弾の開口判定へ接続する。
            _ = impactLocalPosition;
        }

        /// <summary>
        /// 外周から外周へつながるクラックでガラスを二分し、2つの落下破片へ変換します。
        /// クラックはガラスのローカル座標で指定します。
        /// </summary>
        public bool TrySeparateAlongCrack(Vector2[] crack)
        {
            EnsureGeometryInitialized();
            if (!TrySplitPolygon(outline, crack, out Vector2[] firstRegion, out Vector2[] secondRegion))
            {
                return false;
            }

            CreateFragment(firstRegion, 0);
            CreateFragment(secondRegion, 1);
            Destroy(gameObject);
            return true;
        }

        /// <summary>クラック先端を指定位置まで延ばし、外周に当たれば分離を試みます。</summary>
        public bool TryExtendCrackToBoundary(int crackIndex, Vector2 newCrackPosition)
        {
            EnsureGeometryInitialized();
            if (crackIndex < 0 || crackIndex >= cracks.Length ||
                cracks[crackIndex] == null || cracks[crackIndex].Length == 0)
            {
                return false;
            }

            Vector2 crackTip = cracks[crackIndex][^1];
            float closestDistance = float.PositiveInfinity;
            Vector2 closestHit = default;
            bool foundHit = false;

            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 edgeStart = outline[i];
                Vector2 edgeEnd = outline[(i + 1) % outline.Length];
                if (!TryGetSegmentIntersection(crackTip, newCrackPosition, edgeStart, edgeEnd, out Vector2 hit))
                {
                    continue;
                }

                float distance = (hit - crackTip).sqrMagnitude;
                if (distance <= GeometryEpsilon * GeometryEpsilon || distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                closestHit = hit;
                foundHit = true;
            }

            if (!foundHit)
            {
                return false;
            }

            Vector2[] completedCrack = new Vector2[cracks[crackIndex].Length + 1];
            Array.Copy(cracks[crackIndex], completedCrack, cracks[crackIndex].Length);
            completedCrack[^1] = closestHit;
            cracks[crackIndex] = completedCrack;
            RenderCracks();
            return TrySeparateAlongCrack(completedCrack);
        }

        private static bool TrySplitPolygon(
            Vector2[] polygon,
            Vector2[] crack,
            out Vector2[] firstRegion,
            out Vector2[] secondRegion)
        {
            firstRegion = Array.Empty<Vector2>();
            secondRegion = Array.Empty<Vector2>();
            if (polygon == null || polygon.Length < 3 || crack == null || crack.Length < 2)
            {
                return false;
            }

            if (!TryLocateOnBoundary(polygon, crack[0], out BoundaryLocation start) ||
                !TryLocateOnBoundary(polygon, crack[^1], out BoundaryLocation end))
            {
                return false;
            }

            var normalizedCrack = new List<Vector2>(crack);
            normalizedCrack[0] = start.Point;
            normalizedCrack[^1] = end.Point;

            List<Vector2> boundaryStartToEnd = BuildBoundaryPath(polygon, start, end);
            List<Vector2> boundaryEndToStart = BuildBoundaryPath(polygon, end, start);

            var regionA = new List<Vector2>(boundaryStartToEnd);
            for (int i = normalizedCrack.Count - 2; i > 0; i--)
            {
                regionA.Add(normalizedCrack[i]);
            }

            var regionB = new List<Vector2>(boundaryEndToStart);
            for (int i = 1; i < normalizedCrack.Count - 1; i++)
            {
                regionB.Add(normalizedCrack[i]);
            }

            firstRegion = CleanPolygon(regionA);
            secondRegion = CleanPolygon(regionB);
            return IsValidPolygon(firstRegion) && IsValidPolygon(secondRegion);
        }

        private static bool TryLocateOnBoundary(
            IReadOnlyList<Vector2> polygon,
            Vector2 point,
            out BoundaryLocation location)
        {
            float bestDistanceSquared = float.PositiveInfinity;
            BoundaryLocation best = default;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 start = polygon[i];
                Vector2 end = polygon[(i + 1) % polygon.Count];
                Vector2 edge = end - start;
                float lengthSquared = edge.sqrMagnitude;
                if (lengthSquared <= GeometryEpsilon * GeometryEpsilon)
                {
                    continue;
                }

                float edgeT = Mathf.Clamp01(Vector2.Dot(point - start, edge) / lengthSquared);
                Vector2 projected = start + edgeT * edge;
                float distanceSquared = (point - projected).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = new BoundaryLocation(i, edgeT, projected);
                }
            }

            location = best;
            return bestDistanceSquared <= GeometryEpsilon * GeometryEpsilon;
        }

        private static List<Vector2> BuildBoundaryPath(
            IReadOnlyList<Vector2> polygon,
            BoundaryLocation from,
            BoundaryLocation to)
        {
            var path = new List<Vector2> { from.Point };
            bool directSameEdge = from.EdgeIndex == to.EdgeIndex && from.EdgeT <= to.EdgeT;
            if (!directSameEdge)
            {
                int vertexIndex = (from.EdgeIndex + 1) % polygon.Count;
                int stopVertex = (to.EdgeIndex + 1) % polygon.Count;
                int safety = 0;
                do
                {
                    path.Add(polygon[vertexIndex]);
                    vertexIndex = (vertexIndex + 1) % polygon.Count;
                    safety++;
                }
                while (vertexIndex != stopVertex && safety <= polygon.Count);
            }

            path.Add(to.Point);
            return path;
        }

        private static Vector2[] CleanPolygon(IReadOnlyList<Vector2> points)
        {
            var clean = new List<Vector2>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                if (clean.Count == 0 ||
                    (clean[^1] - points[i]).sqrMagnitude > GeometryEpsilon * GeometryEpsilon)
                {
                    clean.Add(points[i]);
                }
            }

            if (clean.Count > 1 &&
                (clean[0] - clean[^1]).sqrMagnitude <= GeometryEpsilon * GeometryEpsilon)
            {
                clean.RemoveAt(clean.Count - 1);
            }

            return clean.ToArray();
        }

        private static bool IsValidPolygon(IReadOnlyList<Vector2> polygon)
        {
            return polygon != null && polygon.Count >= 3 && Mathf.Abs(SignedArea(polygon)) > GeometryEpsilon;
        }

        private static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float twiceArea = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                twiceArea += Cross(current, next);
            }

            return twiceArea * 0.5f;
        }

        private static float Cross(Vector2 lhs, Vector2 rhs)
        {
            return lhs.x * rhs.y - lhs.y * rhs.x;
        }

        private static bool TryGetSegmentIntersection(
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            Vector2 p4,
            out Vector2 intersection)
        {
            Vector2 r = p2 - p1;
            Vector2 s = p4 - p3;
            float denominator = Cross(r, s);
            if (Mathf.Abs(denominator) <= GeometryEpsilon)
            {
                intersection = default;
                return false;
            }

            Vector2 offset = p3 - p1;
            float t = Cross(offset, s) / denominator;
            float u = Cross(offset, r) / denominator;
            if (t < -GeometryEpsilon || t > 1f + GeometryEpsilon ||
                u < -GeometryEpsilon || u > 1f + GeometryEpsilon)
            {
                intersection = default;
                return false;
            }

            intersection = p1 + Mathf.Clamp01(t) * r;
            return true;
        }

        private void CreateFragment(Vector2[] region, int pieceIndex)
        {
            Vector2 centroid = CalculateCentroid(region);
            Vector2[] centeredRegion = new Vector2[region.Length];
            for (int i = 0; i < region.Length; i++)
            {
                centeredRegion[i] = region[i] - centroid;
            }

            GameObject fragment = new GameObject($"{name}_Fragment_{pieceIndex}");
            fragment.transform.SetParent(transform.parent, false);
            fragment.transform.position = transform.TransformPoint(centroid);
            fragment.transform.rotation = transform.rotation;
            fragment.transform.localScale = transform.localScale;

            GameObject outlineObject = new GameObject("GlassSurfaceLineRenderer");
            outlineObject.transform.SetParent(fragment.transform, false);
            GlassSurfaceLineRenderer renderer = outlineObject.AddComponent<GlassSurfaceLineRenderer>();
            LineRenderer line = outlineObject.GetComponent<LineRenderer>();
            if (outlineLineRenderer != null && outlineLineRenderer.TryGetComponent(out LineRenderer sourceLine))
            {
                line.sharedMaterial = sourceLine.sharedMaterial;
            }
            else
            {
                line.material = new Material(Shader.Find("Sprites/Default"));
            }
            renderer.SetOutline(centeredRegion);

            PolygonCollider2D collider = fragment.AddComponent<PolygonCollider2D>();
            collider.points = centeredRegion;

            Rigidbody2D body = fragment.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 1f;
            body.mass = Mathf.Max(0.05f, Mathf.Abs(SignedArea(centeredRegion)));
            body.angularVelocity = UnityEngine.Random.Range(-90f, 90f);

            fragment.AddComponent<GlassFragment>();
        }

        private static Vector2 CalculateCentroid(IReadOnlyList<Vector2> polygon)
        {
            float areaFactorSum = 0f;
            Vector2 weightedSum = Vector2.zero;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                float factor = Cross(current, next);
                areaFactorSum += factor;
                weightedSum += (current + next) * factor;
            }

            if (Mathf.Abs(areaFactorSum) <= GeometryEpsilon)
            {
                Vector2 average = Vector2.zero;
                for (int i = 0; i < polygon.Count; i++)
                {
                    average += polygon[i];
                }
                return average / polygon.Count;
            }

            return weightedSum / (3f * areaFactorSum);
        }

        private void EnsureGeometryInitialized()
        {
            if ((outline == null || outline.Length < 3) && outlineLineRenderer != null)
            {
                outline = outlineLineRenderer.OutlinePoints;
            }

            outline ??= Array.Empty<Vector2>();
            initCrackPoint ??= Array.Empty<Vector2>();
            cracks ??= Array.Empty<Vector2[]>();
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

    /// <summary>生成済み破片の寿命管理です。落下と回転は Rigidbody2D が担当します。</summary>
    [DisallowMultipleComponent]
    internal sealed class GlassFragment : MonoBehaviour
    {
        [SerializeField] private float destroyBelowY = -8f;

        private void FixedUpdate()
        {
            if (transform.position.y < destroyBelowY)
            {
                Destroy(gameObject);
            }
        }
    }
}
