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
    [RequireComponent(typeof(GlassStatus))]
    public sealed class CrackProcessingComponent : MonoBehaviour
    {
        [Header("Glass References")]
        [SerializeField] private GameObject glassRoot = null;
        [SerializeField] private GlassSurfaceLineRenderer outlineLineRenderer = null;
        [SerializeField] private CrackLineRenderer crackLineRenderer = null;
        [SerializeField] private GlassStatus glassStatus = null;

        [Header("Geometry (Local Space)")]
        [SerializeField] private Vector2[] outline = Array.Empty<Vector2>();
        [SerializeField] private Vector2[] initCrackPoint = Array.Empty<Vector2>();

        // Unityはジャグ配列をシリアライズしないため、クラックは実行時データとして保持します。
        private Vector2[][] cracks = Array.Empty<Vector2[]>();

        [Header("Crack Growth")]
        [SerializeField] private int crackRandomSeed = 12345;
        [SerializeField, Min(0f)] private float surfaceFlawMinimumSpacing = 1.2f;
        [SerializeField, Min(0f)] private float crackTipDetectionRadius = 1.2f;
        [SerializeField, Min(0.0001f)] private float baseFractureResistance = 1f;
        [SerializeField, Min(0f)] private float minimumScanRadius = 0.1f;
        [SerializeField, Min(0f)] private float maximumScanRadius = 20f;
        [SerializeField, Range(0.01f, 1f)] private float minimumVulnerabilityCostMultiplier = 0.1f;
        [SerializeField, Min(0f)] private float angleCostWeight = 1f;

        [Header("Boundary Completion")]
        [SerializeField, Min(0f)] private float boundaryCompletionDistance = 0.15f;
        [SerializeField, Min(1)] private int maxBoundaryCompletionCandidates = 3;

        private readonly List<CrackNode> crackNodes = new List<CrackNode>();
        private readonly List<CrackConnection> crackConnections = new List<CrackConnection>();
        private System.Random crackRandom;
        private bool crackGraphInitialized;
        private bool isReleasedFromAnchor;
        private bool isSeparating;

        // 選択中オブジェクトのGizmo表示にだけ使用する直近着弾の診断情報。
        private readonly List<DebugLine> debugPrimaryCandidates = new List<DebugLine>();
        private readonly List<DebugLine> debugRejectedCandidates = new List<DebugLine>();
        private readonly List<DebugLine> debugAcceptedConnections = new List<DebugLine>();
        private readonly List<DebugCircle> debugSecondaryScans = new List<DebugCircle>();
        private readonly List<DebugLine> debugSectorBoundaries = new List<DebugLine>();
        private Vector2 debugLastImpact;
        private float debugLastScanRadius;
        private bool hasDebugImpact;

        private const float GeometryEpsilon = 0.0001f;

        public GameObject GlassRoot => glassRoot;
        public GlassSurfaceLineRenderer OutlineLineRenderer => outlineLineRenderer;
        public CrackLineRenderer CrackLineRenderer => crackLineRenderer;
        public GlassStatus GlassStatus => glassStatus;
        public Vector2[] Outline => (Vector2[])outline.Clone();
        public Vector2[] InitialCrackPoints => (Vector2[])initCrackPoint.Clone();
        public IReadOnlyList<Vector2[]> Cracks => cracks;
        public float BaseFractureResistance => baseFractureResistance;
        public float MinimumScanRadius => minimumScanRadius;
        public float MaximumScanRadius => maximumScanRadius;

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

        [Serializable]
        private sealed class CrackNode
        {
            public int id;
            public Vector2 localPosition;

            [Range(0f, 1f)]
            public float vulnerability;

            public bool isSurfaceFlaw;
        }

        private sealed class CrackConnection
        {
            public int nodeAId;
            public int nodeBId;
            public float fractureCost;
        }

        private sealed class CrackCandidate
        {
            public CrackNode from;
            public CrackNode to;
            public float distance;
            public float signedAngle;
            public float absoluteAngle;
            public float fractureCost;
        }

        private sealed class CrackPathCandidate
        {
            public CrackCandidate primary;
            public CrackCandidate secondary;
            public bool hasSecondary;
            public float totalFractureCost;
        }

        private sealed class BoundaryCompletionCandidate
        {
            public Vector2[] completedPath;
            public float distanceSquared;
        }

        private readonly struct AngleMarker
        {
            public AngleMarker(float angle, CrackCandidate candidate)
            {
                Angle = angle;
                Candidate = candidate;
            }

            public float Angle { get; }
            public CrackCandidate Candidate { get; }
        }

        private readonly struct DebugLine
        {
            public DebugLine(Vector2 from, Vector2 to)
            {
                From = from;
                To = to;
            }

            public Vector2 From { get; }
            public Vector2 To { get; }
        }

        private readonly struct DebugCircle
        {
            public DebugCircle(Vector2 center, float radius)
            {
                Center = center;
                Radius = radius;
            }

            public Vector2 Center { get; }
            public float Radius { get; }
        }

        private void Awake()
        {
            ResolveMissingReferences();
            EnsureGeometryInitialized();
            ApplyAnchorState();
            RenderCracks();
        }

        private void Reset()
        {
            glassRoot = gameObject;
            ResolveMissingReferences();
            EnsureGeometryInitialized();
        }

        /// <summary>スポーン時に外周とクラックを安全に設定します。</summary>
        public void Initialize(
            Vector2[] outlinePoints,
            Vector2[][] crackPaths = null,
            bool releasedFromAnchor = false)
        {
            if (outlinePoints == null || outlinePoints.Length < 3)
            {
                throw new ArgumentException("Glass outline requires at least three points.", nameof(outlinePoints));
            }

            ResolveMissingReferences();
            outline = CleanPolygon(outlinePoints);
            cracks = CloneCracks(crackPaths);
            crackGraphInitialized = false;
            isReleasedFromAnchor |= releasedFromAnchor;
            ApplyAnchorState();

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

        private void ApplyAnchorState()
        {
            if (!TryGetComponent(out Rigidbody2D body))
            {
                return;
            }

            if (isReleasedFromAnchor)
            {
                body.constraints = RigidbodyConstraints2D.None;
                body.gravityScale = glassStatus != null
                    ? glassStatus.GravityMultiplier
                    : 1f;
                return;
            }

            body.constraints = RigidbodyConstraints2D.FreezeAll;
            body.gravityScale = 0f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        public void SetCracks(Vector2[][] crackPaths)
        {
            cracks = CloneCracks(crackPaths);
            crackGraphInitialized = false;
            RenderCracks();
        }

        /// <summary>クラックの末端をランダムに1つ選び、末端側の一区間を修復します。</summary>
        public bool HealCracks()
        {
            EnsureCrackGraphInitialized();
            if (crackConnections.Count == 0)
            {
                return false;
            }

            var degreeByNodeId = new int[crackNodes.Count];
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                degreeByNodeId[connection.nodeAId]++;
                degreeByNodeId[connection.nodeBId]++;
            }

            var terminalNodeIds = new List<int>();
            for (int nodeId = 0; nodeId < degreeByNodeId.Length; nodeId++)
            {
                if (degreeByNodeId[nodeId] == 1)
                {
                    terminalNodeIds.Add(nodeId);
                }
            }

            if (terminalNodeIds.Count == 0)
            {
                return false;
            }

            crackRandom ??= new System.Random(crackRandomSeed);
            int terminalNodeId = terminalNodeIds[crackRandom.Next(terminalNodeIds.Count)];
            for (int connectionIndex = 0; connectionIndex < crackConnections.Count; connectionIndex++)
            {
                CrackConnection connection = crackConnections[connectionIndex];
                if (connection.nodeAId != terminalNodeId && connection.nodeBId != terminalNodeId)
                {
                    continue;
                }

                crackConnections.RemoveAt(connectionIndex);
                break;
            }

            // GetNodeはIDをリストの添字として扱うため、末端ノード削除後にIDを詰める。
            crackNodes.RemoveAt(terminalNodeId);
            for (int nodeId = terminalNodeId; nodeId < crackNodes.Count; nodeId++)
            {
                crackNodes[nodeId].id = nodeId;
            }
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                if (connection.nodeAId > terminalNodeId)
                {
                    connection.nodeAId--;
                }
                if (connection.nodeBId > terminalNodeId)
                {
                    connection.nodeBId--;
                }
            }

            cracks = BuildRenderableCrackPaths();
            RenderCracks();
            return true;
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

        public void HandleProjectileImpact(Vector2 projectileWorldPosition, BulletStatus bulletStatus)
        {
            EnsureGeometryInitialized();
            // Triggerには接触点情報がないため、弾中心をガラス外周へ投影して代表接触点にする。
            Vector2 projectileLocalPosition = transform.InverseTransformPoint(projectileWorldPosition);
            Vector2 impactLocalPosition = GetClosestPointOnOutline(projectileLocalPosition);
            Vector2 impactWorldPosition = transform.TransformPoint(impactLocalPosition);
            HandleBulletImpact(impactWorldPosition, bulletStatus);
        }

        private float pooledImpactEnergy;
        public void HandleBulletImpact(Vector2 impactWorldPosition, BulletStatus bulletStatus)
        {
            if (isSeparating)
            {
                return;
            }

            if (bulletStatus == null)
            {
                Debug.LogWarning("Bullet impact was ignored because BulletStatus was null.", this);
                return;
            }

            // 以降のクラック計算は必ずローカル座標を使う。
            Vector2 impactLocalPosition = transform.InverseTransformPoint(impactWorldPosition);

            if (TryGetComponent(out GlassFragment _) &&
                glassStatus != null &&
                Mathf.Abs(SignedArea(outline)) + GeometryEpsilon < glassStatus.MinimumBreakableArea)
            {
                return;
            }

            Debug.Log("1");

            EnsureCrackGraphInitialized();
            CrackNode surfaceFlaw = FindOrCreateSurfaceFlaw(impactLocalPosition);
            CrackNode startNode = FindCrackTipFromSurfaceRootOrFallback(
                surfaceFlaw,
                impactLocalPosition);
            Vector2 referenceDirection = ResolveReferenceDirection(startNode, bulletStatus.CurrentVelocity);

            float impactEnergy = bulletStatus.CalculateKineticEnergy()
                * bulletStatus.CrackConversionEfficiency;
            impactEnergy += pooledImpactEnergy;
            float scanRadius = CalculateScanRadius(impactEnergy);

            ResetImpactDebugData(impactLocalPosition, scanRadius);
            List<CrackPathCandidate> paths = BuildCrackPathCandidates(
                startNode,
                referenceDirection,
                scanRadius,
                impactEnergy);

            // 実際にクラック形成へ使われなかった分だけを、ガラス全体で次回へ持ち越す。
            // 候補がない場合や全候補が予算超過の場合は、全量がそのまま残る。
            pooledImpactEnergy = ApplyCrackPathsWithinBudget(paths, impactEnergy);
            cracks = BuildRenderableCrackPaths();

            // ボス本体は蓄積破砕を前提とするため、着弾で縮小させない。
            // 分離後の通常破片にはBossGlassComponentを継承しないので、
            // 追撃分だけ最終回収面積が減る。
            bool preventsImpactShrink = TryGetComponent(out BossGlassComponent _);
            if (!preventsImpactShrink &&
                !ApplySizeMultiplier(bulletStatus.ContactSizeMultiplier))
            {
                return;
            }

            RenderCracks();

            // 外周同士を結ぶ連続クラックが完成した場合だけ既存の破片分離へ渡す。
            TrySeparateCompletedPath();

            Debug.Log("2");
        }

        private bool ApplySizeMultiplier(float multiplier)
        {
            multiplier = Mathf.Max(0f, multiplier);
            if (Mathf.Approximately(multiplier, 1f) || outline == null || outline.Length < 3)
            {
                return true;
            }

            Vector2 center = CalculateCentroid(outline);
            ScalePoints(outline, center, multiplier);
            ScalePoints(initCrackPoint, center, multiplier);
            for (int crackIndex = 0; crackIndex < cracks.Length; crackIndex++)
            {
                ScalePoints(cracks[crackIndex], center, multiplier);
            }
            for (int nodeIndex = 0; nodeIndex < crackNodes.Count; nodeIndex++)
            {
                CrackNode node = crackNodes[nodeIndex];
                node.localPosition = center + (node.localPosition - center) * multiplier;
            }

            float scaledArea = Mathf.Abs(SignedArea(outline));
            float minimumArea = glassStatus != null
                ? glassStatus.MinimumBreakableArea
                : 0.04f;
            if (scaledArea <= minimumArea + GeometryEpsilon)
            {
                Destroy(gameObject);
                return false;
            }

            if (outlineLineRenderer != null)
            {
                outlineLineRenderer.SetOutline(outline);
            }
            if (TryGetComponent(out PolygonCollider2D collider))
            {
                collider.points = outline;
            }
            if (TryGetComponent(out Rigidbody2D body))
            {
                body.mass = glassStatus != null
                    ? glassStatus.CalculateMass(scaledArea)
                    : Mathf.Max(0.05f, scaledArea);
            }
            RenderCracks();
            return true;
        }

        private static void ScalePoints(Vector2[] points, Vector2 center, float multiplier)
        {
            if (points == null)
            {
                return;
            }
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = center + (points[i] - center) * multiplier;
            }
        }

        private void EnsureCrackGraphInitialized()
        {
            if (crackGraphInitialized)
            {
                return;
            }

            EnsureGeometryInitialized();
            crackNodes.Clear();
            crackConnections.Clear();
            crackRandom = new System.Random(crackRandomSeed);

            var generatedVulnerabilities = new Dictionary<Vector2, float>();
            if ((initCrackPoint == null || initCrackPoint.Length == 0) &&
                glassStatus != null && outline.Length >= 3)
            {
                GetOutlineSize(out float width, out float height);
                InitialCrackPointData[] generated = glassStatus.GenerateInitialCrackPointData(
                    width,
                    height,
                    crackRandomSeed);
                var validPositions = new List<Vector2>(generated.Length);
                for (int i = 0; i < generated.Length; i++)
                {
                    if (!IsPointInsideOrOnOutline(generated[i].localPosition))
                    {
                        continue;
                    }

                    validPositions.Add(generated[i].localPosition);
                    generatedVulnerabilities[generated[i].localPosition] = generated[i].vulnerability;
                }
                initCrackPoint = validPositions.ToArray();
            }

            float minimumVulnerability = glassStatus != null
                ? glassStatus.MinimumInitialVulnerability
                : 0f;
            float maximumVulnerability = glassStatus != null
                ? glassStatus.MaximumInitialVulnerability
                : 1f;

            for (int i = 0; i < initCrackPoint.Length; i++)
            {
                Vector2 position = initCrackPoint[i];
                if (!IsPointInsideOrOnOutline(position))
                {
                    continue;
                }

                float vulnerability = generatedVulnerabilities.TryGetValue(position, out float generatedValue)
                    ? generatedValue
                    : Mathf.Lerp(
                        minimumVulnerability,
                        maximumVulnerability,
                        NextRandom01());
                GetOrCreateNode(position, vulnerability, false);
            }

            // SetCracks等で渡された既存折れ線をグラフへ復元する。
            for (int pathIndex = 0; pathIndex < cracks.Length; pathIndex++)
            {
                Vector2[] path = cracks[pathIndex];
                if (path == null)
                {
                    continue;
                }

                for (int pointIndex = 0; pointIndex < path.Length; pointIndex++)
                {
                    bool isSurface = IsPointOnOutline(path[pointIndex]);
                    GetOrCreateNode(path[pointIndex], isSurface ? 1f : NextRandom01(), isSurface);
                }

                for (int pointIndex = 0; pointIndex + 1 < path.Length; pointIndex++)
                {
                    CrackNode from = FindNodeAt(path[pointIndex]);
                    CrackNode to = FindNodeAt(path[pointIndex + 1]);
                    if (from != null && to != null)
                    {
                        AddConnection(
                            from,
                            to,
                            Mathf.Max(GeometryEpsilon, Vector2.Distance(from.localPosition, to.localPosition)
                                * baseFractureResistance));
                    }
                }
            }

            crackGraphInitialized = true;
        }

        private void GetOutlineSize(out float width, out float height)
        {
            if (outline == null || outline.Length == 0)
            {
                width = 0f;
                height = 0f;
                return;
            }

            Vector2 min = outline[0];
            Vector2 max = outline[0];
            for (int i = 1; i < outline.Length; i++)
            {
                min = Vector2.Min(min, outline[i]);
                max = Vector2.Max(max, outline[i]);
            }
            width = max.x - min.x;
            height = max.y - min.y;
        }

        private float NextRandom01()
        {
            crackRandom ??= new System.Random(crackRandomSeed);
            return (float)crackRandom.NextDouble();
        }

        private CrackNode GetOrCreateNode(Vector2 position, float vulnerability, bool isSurfaceFlaw)
        {
            CrackNode existing = FindNodeAt(position);
            if (existing != null)
            {
                existing.isSurfaceFlaw |= isSurfaceFlaw;
                if (isSurfaceFlaw)
                {
                    existing.vulnerability = 1f;
                }
                return existing;
            }

            var node = new CrackNode
            {
                id = crackNodes.Count,
                localPosition = position,
                vulnerability = Mathf.Clamp01(vulnerability),
                isSurfaceFlaw = isSurfaceFlaw
            };
            crackNodes.Add(node);
            return node;
        }

        private CrackNode FindNodeAt(Vector2 position)
        {
            float epsilonSquared = GeometryEpsilon * GeometryEpsilon;
            for (int i = 0; i < crackNodes.Count; i++)
            {
                if ((crackNodes[i].localPosition - position).sqrMagnitude <= epsilonSquared)
                {
                    return crackNodes[i];
                }
            }
            return null;
        }

        private CrackNode GetNode(int id)
        {
            return id >= 0 && id < crackNodes.Count ? crackNodes[id] : null;
        }

        private CrackNode FindOrCreateSurfaceFlaw(Vector2 impactLocalPosition)
        {
            CrackNode nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < crackNodes.Count; i++)
            {
                if (!crackNodes[i].isSurfaceFlaw)
                {
                    continue;
                }

                float distance = Vector2.Distance(crackNodes[i].localPosition, impactLocalPosition);
                if (distance < nearestDistance)
                {
                    nearest = crackNodes[i];
                    nearestDistance = distance;
                }
            }

            if (nearest != null && nearestDistance < surfaceFlawMinimumSpacing)
            {
                return nearest;
            }

            return GetOrCreateNode(impactLocalPosition, 1f, true);
        }

        private CrackNode FindCrackTipFromSurfaceRootOrFallback(
            CrackNode surfaceRoot,
            Vector2 impactLocalPosition)
        {
            if (surfaceRoot == null ||
                !surfaceRoot.isSurfaceFlaw ||
                Vector2.Distance(surfaceRoot.localPosition, impactLocalPosition) > crackTipDetectionRadius)
            {
                return surfaceRoot;
            }

            var connectionCountByNodeId = new int[crackNodes.Count];
            var connectedNodeIds = new HashSet<int> { surfaceRoot.id };
            var nodesToVisit = new Queue<int>();
            nodesToVisit.Enqueue(surfaceRoot.id);
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                if (connection.nodeAId >= 0 && connection.nodeAId < connectionCountByNodeId.Length)
                {
                    connectionCountByNodeId[connection.nodeAId]++;
                }
                if (connection.nodeBId >= 0 && connection.nodeBId < connectionCountByNodeId.Length)
                {
                    connectionCountByNodeId[connection.nodeBId]++;
                }
            }

            if (surfaceRoot.id < 0 ||
                surfaceRoot.id >= connectionCountByNodeId.Length ||
                connectionCountByNodeId[surfaceRoot.id] == 0)
            {
                return surfaceRoot;
            }

            // 同じクラックかどうかは内部先端との距離ではなく、外周上の根本との
            // 距離だけで決める。その後、根本につながる成分内の先端を成長させる。
            while (nodesToVisit.Count > 0)
            {
                int currentNodeId = nodesToVisit.Dequeue();
                for (int i = 0; i < crackConnections.Count; i++)
                {
                    CrackConnection connection = crackConnections[i];
                    int nextNodeId = connection.nodeAId == currentNodeId
                        ? connection.nodeBId
                        : connection.nodeBId == currentNodeId
                            ? connection.nodeAId
                            : -1;
                    if (nextNodeId >= 0 && connectedNodeIds.Add(nextNodeId))
                    {
                        nodesToVisit.Enqueue(nextNodeId);
                    }
                }
            }

            CrackNode farthestInternalTip = null;
            float farthestDistanceSquared = float.NegativeInfinity;
            foreach (int nodeId in connectedNodeIds)
            {
                if (nodeId == surfaceRoot.id || connectionCountByNodeId[nodeId] != 1)
                {
                    continue;
                }
                CrackNode node = GetNode(nodeId);
                if (node == null || node.isSurfaceFlaw)
                {
                    continue;
                }
                float distanceSquared = (node.localPosition - surfaceRoot.localPosition).sqrMagnitude;
                if (distanceSquared > farthestDistanceSquared)
                {
                    farthestDistanceSquared = distanceSquared;
                    farthestInternalTip = node;
                }
            }

            return farthestInternalTip ?? surfaceRoot;
        }

        private Vector2 ResolveReferenceDirection(CrackNode startNode, Vector2 bulletVelocity)
        {
            Vector2 fallbackDirection = bulletVelocity.sqrMagnitude > GeometryEpsilon * GeometryEpsilon
                ? bulletVelocity.normalized
                : Vector2.up;

            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                int previousId = connection.nodeAId == startNode.id
                    ? connection.nodeBId
                    : connection.nodeBId == startNode.id
                        ? connection.nodeAId
                        : -1;
                if (previousId < 0)
                {
                    continue;
                }

                CrackNode previous = GetNode(previousId);
                Vector2 direction = startNode.localPosition - previous.localPosition;
                return direction.sqrMagnitude > GeometryEpsilon * GeometryEpsilon
                    ? direction.normalized
                    : fallbackDirection;
            }
            return fallbackDirection;
        }

        private float CalculateScanRadius(float impactEnergy)
        {
            float glassArea = Mathf.Abs(SignedArea(outline));
            float resistance = Mathf.Max(baseFractureResistance, GeometryEpsilon);
            float energyRatio = Mathf.Max(0f, impactEnergy / resistance);

            // 面積の平方根とエネルギー比の平方根から一次走査距離を得る。
            float scanRadius = Mathf.Sqrt(glassArea) * Mathf.Sqrt(energyRatio);
            return Mathf.Clamp(scanRadius, minimumScanRadius, maximumScanRadius);
        }

        private List<CrackPathCandidate> BuildCrackPathCandidates(
            CrackNode startNode,
            Vector2 referenceDirection,
            float scanRadius,
            float impactEnergy)
        {
            var primaryCandidates = new List<CrackCandidate>();
            for (int i = 0; i < crackNodes.Count; i++)
            {
                CrackNode target = crackNodes[i];
                if (TryBuildCandidate(
                    startNode,
                    target,
                    referenceDirection,
                    scanRadius,
                    true,
                    out CrackCandidate candidate))
                {
                    primaryCandidates.Add(candidate);
                    debugPrimaryCandidates.Add(new DebugLine(
                        startNode.localPosition,
                        target.localPosition));
                }
            }

            // 扇形の隣接関係を安定させるため、一次候補は符号付き角度の昇順にする。
            primaryCandidates.Sort((a, b) => a.signedAngle.CompareTo(b.signedAngle));

            var angleMarkers = new List<AngleMarker>(primaryCandidates.Count + 2);
            for (int i = 0; i < primaryCandidates.Count; i++)
            {
                angleMarkers.Add(new AngleMarker(primaryCandidates[i].signedAngle, primaryCandidates[i]));
            }
            AddOuterBoundaryAngleMarkers(startNode, referenceDirection, angleMarkers);
            angleMarkers.Sort((a, b) => a.Angle.CompareTo(b.Angle));

            var paths = new List<CrackPathCandidate>();
            bool hasAffordablePrimary = false;
            for (int i = 0; i < primaryCandidates.Count; i++)
            {
                CrackCandidate primary = primaryCandidates[i];
                hasAffordablePrimary |= primary.fractureCost <= impactEnergy + GeometryEpsilon;
                paths.Add(new CrackPathCandidate
                {
                    primary = primary,
                    hasSecondary = false,
                    totalFractureCost = primary.fractureCost
                });

                float secondaryRadius = Mathf.Max(0f, scanRadius - primary.distance);
                if (secondaryRadius <= GeometryEpsilon)
                {
                    continue;
                }

                debugSecondaryScans.Add(new DebugCircle(primary.to.localPosition, secondaryRadius));
                ResolveCandidateSector(
                    primary,
                    angleMarkers,
                    out float sectorStart,
                    out float sectorEnd,
                    out bool fullSector);
                AddSectorDebugLines(startNode.localPosition, referenceDirection, sectorStart, sectorEnd, scanRadius);

                Vector2 primaryDirection = (primary.to.localPosition - primary.from.localPosition).normalized;
                float remainingEnergyForSecondary = Mathf.Max(
                    0f,
                    impactEnergy - primary.fractureCost);
                bool hasAffordableSecondary = false;
                for (int nodeIndex = 0; nodeIndex < crackNodes.Count; nodeIndex++)
                {
                    CrackNode secondaryNode = crackNodes[nodeIndex];
                    if (secondaryNode.id == primary.to.id || secondaryNode.id == startNode.id)
                    {
                        continue;
                    }

                    Vector2 fromPrimary = secondaryNode.localPosition - primary.to.localPosition;
                    float secondaryDistance = fromPrimary.magnitude;
                    if (secondaryDistance <= GeometryEpsilon || secondaryDistance > secondaryRadius)
                    {
                        continue;
                    }

                    Vector2 fromStart = secondaryNode.localPosition - startNode.localPosition;
                    float secondaryAngle = Vector2.SignedAngle(referenceDirection, fromStart);
                    if (!fullSector && !IsAngleInsideSector(secondaryAngle, sectorStart, sectorEnd))
                    {
                        continue;
                    }

                    if (!TryBuildCandidate(
                        primary.to,
                        secondaryNode,
                        primaryDirection,
                        secondaryRadius,
                        false,
                        out CrackCandidate secondary))
                    {
                        continue;
                    }

                    float directionAlignment = Mathf.Clamp01(
                        (Vector2.Dot(primaryDirection, fromPrimary.normalized) + 1f) * 0.5f);
                    float distanceFactor = Mathf.Clamp01(1f - secondaryDistance / secondaryRadius);
                    float energyFactor = Mathf.Clamp01(
                        remainingEnergyForSecondary / Mathf.Max(impactEnergy, GeometryEpsilon));
                    float probability = Mathf.Clamp01(
                        secondaryNode.vulnerability
                        * directionAlignment
                        * distanceFactor
                        * energyFactor);

                    if (NextRandom01() > probability)
                    {
                        continue;
                    }

                    paths.Add(new CrackPathCandidate
                    {
                        primary = primary,
                        secondary = secondary,
                        hasSecondary = true,
                        totalFractureCost = primary.fractureCost + secondary.fractureCost
                    });
                    hasAffordableSecondary |= secondary.fractureCost
                        <= remainingEnergyForSecondary + GeometryEpsilon;
                }

                // 通常の二次候補に予算内の接続先がない場合だけ、進行方向の外周を候補にする。
                if (!hasAffordableSecondary &&
                    TryBuildBoundaryFallbackCandidate(
                        primary.to,
                        primaryDirection,
                        secondaryRadius,
                        remainingEnergyForSecondary,
                        out CrackCandidate boundarySecondary))
                {
                    paths.Add(new CrackPathCandidate
                    {
                        primary = primary,
                        secondary = boundarySecondary,
                        hasSecondary = true,
                        totalFractureCost = primary.fractureCost + boundarySecondary.fractureCost
                    });
                }
            }

            // 通常の一次候補に予算内の接続先がない場合だけ、基準方向の外周を候補にする。
            if (!hasAffordablePrimary &&
                TryBuildBoundaryFallbackCandidate(
                    startNode,
                    referenceDirection,
                    scanRadius,
                    impactEnergy,
                    out CrackCandidate boundaryPrimary))
            {
                paths.Add(new CrackPathCandidate
                {
                    primary = boundaryPrimary,
                    hasSecondary = false,
                    totalFractureCost = boundaryPrimary.fractureCost
                });
            }
            return paths;
        }

        private bool TryBuildBoundaryFallbackCandidate(
            CrackNode origin,
            Vector2 direction,
            float maximumDistance,
            float availableEnergy,
            out CrackCandidate candidate)
        {
            candidate = null;
            if (origin == null ||
                maximumDistance <= GeometryEpsilon ||
                availableEnergy <= GeometryEpsilon ||
                direction.sqrMagnitude <= GeometryEpsilon * GeometryEpsilon ||
                outline == null ||
                outline.Length < 3 ||
                !TryGetFirstBoundaryIntersection(
                    origin.localPosition,
                    direction.normalized,
                    maximumDistance,
                    out Vector2 boundaryPoint))
            {
                return false;
            }

            // 既存ノードなら再利用する。新規交点は候補中だけ負IDで保持し、採用時に永続化する。
            CrackNode boundaryNode = FindNodeAt(boundaryPoint) ?? new CrackNode
            {
                id = -1,
                localPosition = boundaryPoint,
                vulnerability = 1f,
                isSurfaceFlaw = false
            };

            if (!TryBuildCandidate(
                origin,
                boundaryNode,
                direction,
                maximumDistance,
                false,
                out candidate) ||
                candidate.fractureCost > availableEnergy + GeometryEpsilon)
            {
                candidate = null;
                return false;
            }

            debugPrimaryCandidates.Add(new DebugLine(origin.localPosition, boundaryPoint));
            return true;
        }

        private bool TryGetFirstBoundaryIntersection(
            Vector2 origin,
            Vector2 direction,
            float maximumDistance,
            out Vector2 boundaryPoint)
        {
            boundaryPoint = default;
            Vector2 rayEnd = origin + direction * maximumDistance;
            float closestDistanceSquared = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < outline.Length; i++)
            {
                if (!TryGetSegmentIntersection(
                    origin,
                    rayEnd,
                    outline[i],
                    outline[(i + 1) % outline.Length],
                    out Vector2 hit))
                {
                    continue;
                }

                float distanceSquared = (hit - origin).sqrMagnitude;
                if (distanceSquared <= GeometryEpsilon * GeometryEpsilon ||
                    distanceSquared >= closestDistanceSquared)
                {
                    continue;
                }

                closestDistanceSquared = distanceSquared;
                boundaryPoint = hit;
                found = true;
            }
            return found;
        }

        private bool TryBuildCandidate(
            CrackNode from,
            CrackNode to,
            Vector2 referenceDirection,
            float maximumDistance,
            bool recordRejected,
            out CrackCandidate candidate)
        {
            candidate = null;
            if (from == null || to == null || from.id == to.id || AreDirectlyConnected(from.id, to.id))
            {
                return false;
            }

            Vector2 delta = to.localPosition - from.localPosition;
            float distance = delta.magnitude;
            if (distance <= GeometryEpsilon || distance > maximumDistance ||
                !IsPointInsideOrOnOutline(to.localPosition) ||
                IntersectsOuterBoundaryBeforeTarget(from.localPosition, to.localPosition) ||
                IntersectsExistingCrackImproperly(from, to))
            {
                if (recordRejected && distance > GeometryEpsilon)
                {
                    debugRejectedCandidates.Add(new DebugLine(from.localPosition, to.localPosition));
                }
                return false;
            }

            Vector2 direction = delta / distance;
            Vector2 safeReference = referenceDirection.sqrMagnitude > GeometryEpsilon * GeometryEpsilon
                ? referenceDirection.normalized
                : direction;
            float signedAngle = Vector2.SignedAngle(safeReference, direction);

            candidate = new CrackCandidate
            {
                from = from,
                to = to,
                distance = distance,
                signedAngle = signedAngle,
                absoluteAngle = Mathf.Abs(signedAngle),
                fractureCost = CalculateFractureCost(from, to, safeReference, direction, distance)
            };
            return true;
        }

        private float CalculateFractureCost(
            CrackNode from,
            CrackNode to,
            Vector2 referenceDirection,
            Vector2 candidateDirection,
            float distance)
        {
            float averageVulnerability = (from.vulnerability + to.vulnerability) * 0.5f;

            // 脆弱性が高いほど倍率を下げ、同じ距離でも進展しやすくする。
            float vulnerabilityMultiplier = minimumVulnerabilityCostMultiplier
                + (1f - minimumVulnerabilityCostMultiplier) * (1f - averageVulnerability);

            // 基準方向との一致度を0～1へ正規化し、逆方向ほど角度コストを増やす。
            float alignment = (Vector2.Dot(referenceDirection, candidateDirection) + 1f) * 0.5f;
            float angleMultiplier = 1f + angleCostWeight * (1f - alignment);

            return baseFractureResistance * distance * vulnerabilityMultiplier * angleMultiplier;
        }

        private void AddOuterBoundaryAngleMarkers(
            CrackNode startNode,
            Vector2 referenceDirection,
            List<AngleMarker> markers)
        {
            Vector2 position = startNode.localPosition;
            int vertexIndex = FindOutlineVertex(position);
            if (vertexIndex >= 0)
            {
                AddDirectionMarker(outline[(vertexIndex - 1 + outline.Length) % outline.Length] - position);
                AddDirectionMarker(outline[(vertexIndex + 1) % outline.Length] - position);
                return;
            }

            if (!TryLocateOnBoundary(outline, position, out BoundaryLocation location))
            {
                return;
            }

            AddDirectionMarker(outline[location.EdgeIndex] - position);
            AddDirectionMarker(outline[(location.EdgeIndex + 1) % outline.Length] - position);

            void AddDirectionMarker(Vector2 direction)
            {
                if (direction.sqrMagnitude <= GeometryEpsilon * GeometryEpsilon)
                {
                    return;
                }
                markers.Add(new AngleMarker(
                    Vector2.SignedAngle(referenceDirection, direction.normalized),
                    null));
            }
        }

        private int FindOutlineVertex(Vector2 position)
        {
            float epsilonSquared = GeometryEpsilon * GeometryEpsilon;
            for (int i = 0; i < outline.Length; i++)
            {
                if ((outline[i] - position).sqrMagnitude <= epsilonSquared)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void ResolveCandidateSector(
            CrackCandidate primary,
            IReadOnlyList<AngleMarker> sortedMarkers,
            out float sectorStart,
            out float sectorEnd,
            out bool fullSector)
        {
            int markerIndex = -1;
            for (int i = 0; i < sortedMarkers.Count; i++)
            {
                if (ReferenceEquals(sortedMarkers[i].Candidate, primary))
                {
                    markerIndex = i;
                    break;
                }
            }

            fullSector = sortedMarkers.Count <= 1 || markerIndex < 0;
            if (fullSector)
            {
                sectorStart = -180f;
                sectorEnd = 180f;
                return;
            }

            float previousAngle = sortedMarkers[(markerIndex - 1 + sortedMarkers.Count) % sortedMarkers.Count].Angle;
            float nextAngle = sortedMarkers[(markerIndex + 1) % sortedMarkers.Count].Angle;
            sectorStart = GetCircularMidAngle(previousAngle, primary.signedAngle);
            sectorEnd = GetCircularMidAngle(primary.signedAngle, nextAngle);
        }

        private static float GetCircularMidAngle(float angleA, float angleB)
        {
            return NormalizeSignedAngle(angleA + Mathf.DeltaAngle(angleA, angleB) * 0.5f);
        }

        private static bool IsAngleInsideSector(float angle, float sectorStart, float sectorEnd)
        {
            float normalizedAngle = NormalizeUnsignedAngle(angle);
            float normalizedStart = NormalizeUnsignedAngle(sectorStart);
            float normalizedEnd = NormalizeUnsignedAngle(sectorEnd);
            if (normalizedStart <= normalizedEnd)
            {
                return normalizedAngle >= normalizedStart - GeometryEpsilon
                    && normalizedAngle <= normalizedEnd + GeometryEpsilon;
            }
            return normalizedAngle >= normalizedStart - GeometryEpsilon
                || normalizedAngle <= normalizedEnd + GeometryEpsilon;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return Mathf.Repeat(angle + 180f, 360f) - 180f;
        }

        private static float NormalizeUnsignedAngle(float angle)
        {
            return Mathf.Repeat(angle, 360f);
        }

        private bool AreDirectlyConnected(int nodeAId, int nodeBId)
        {
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                if ((connection.nodeAId == nodeAId && connection.nodeBId == nodeBId) ||
                    (connection.nodeAId == nodeBId && connection.nodeBId == nodeAId))
                {
                    return true;
                }
            }
            return false;
        }

        private void AddConnection(CrackNode from, CrackNode to, float fractureCost)
        {
            if (from == null || to == null || from.id == to.id || AreDirectlyConnected(from.id, to.id))
            {
                return;
            }

            crackConnections.Add(new CrackConnection
            {
                nodeAId = Mathf.Min(from.id, to.id),
                nodeBId = Mathf.Max(from.id, to.id),
                fractureCost = Mathf.Max(0f, fractureCost)
            });
        }

        private bool IntersectsOuterBoundaryBeforeTarget(Vector2 start, Vector2 target)
        {
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 edgeStart = outline[i];
                Vector2 edgeEnd = outline[(i + 1) % outline.Length];
                if (!TryGetSegmentIntersection(start, target, edgeStart, edgeEnd, out Vector2 hit))
                {
                    continue;
                }

                bool touchesCandidateEndpoint = Approximately(hit, start) || Approximately(hit, target);
                if (!touchesCandidateEndpoint)
                {
                    return true;
                }
            }

            // 凹形状では端点交差だけでも線分の途中が外部へ出る場合があるため内部点も確認する。
            return !IsPointInsideOrOnOutline(Vector2.Lerp(start, target, 0.25f))
                || !IsPointInsideOrOnOutline(Vector2.Lerp(start, target, 0.5f))
                || !IsPointInsideOrOnOutline(Vector2.Lerp(start, target, 0.75f));
        }

        private bool IsPointInsideOrOnOutline(Vector2 point)
        {
            if (outline == null || outline.Length < 3)
            {
                return false;
            }

            for (int i = 0; i < outline.Length; i++)
            {
                if (IsPointOnSegment(
                    point,
                    outline[i],
                    outline[(i + 1) % outline.Length]))
                {
                    return true;
                }
            }

            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                Vector2 current = outline[i];
                Vector2 previous = outline[j];
                bool crossesRay = (current.y > point.y) != (previous.y > point.y);
                if (!crossesRay)
                {
                    continue;
                }

                float intersectionX = (previous.x - current.x)
                    * (point.y - current.y)
                    / (previous.y - current.y)
                    + current.x;
                if (point.x < intersectionX)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private bool IsPointOnOutline(Vector2 point)
        {
            if (outline == null)
            {
                return false;
            }
            for (int i = 0; i < outline.Length; i++)
            {
                if (IsPointOnSegment(point, outline[i], outline[(i + 1) % outline.Length]))
                {
                    return true;
                }
            }
            return false;
        }

        private Vector2 GetClosestPointOnOutline(Vector2 point)
        {
            if (outline == null || outline.Length < 2)
            {
                return point;
            }

            Vector2 closest = outline[0];
            float closestDistanceSquared = float.PositiveInfinity;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 candidate = ClosestPointOnSegment(
                    point,
                    outline[i],
                    outline[(i + 1) % outline.Length]);
                float distanceSquared = (point - candidate).sqrMagnitude;
                if (distanceSquared < closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closest = candidate;
                }
            }
            return closest;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 edge = end - start;
            float lengthSquared = edge.sqrMagnitude;
            if (lengthSquared <= GeometryEpsilon * GeometryEpsilon)
            {
                return start;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, edge) / lengthSquared);
            return start + edge * t;
        }

        private static bool IsPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 closest = ClosestPointOnSegment(point, start, end);
            return (point - closest).sqrMagnitude <= GeometryEpsilon * GeometryEpsilon;
        }

        private bool IntersectsExistingCrackImproperly(CrackNode from, CrackNode to)
        {
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                CrackNode existingA = GetNode(connection.nodeAId);
                CrackNode existingB = GetNode(connection.nodeBId);
                if (existingA == null || existingB == null)
                {
                    continue;
                }

                if (SegmentsHaveImproperCollinearOverlap(
                    from.localPosition,
                    to.localPosition,
                    existingA.localPosition,
                    existingB.localPosition))
                {
                    return true;
                }

                if (!TryGetSegmentIntersection(
                    from.localPosition,
                    to.localPosition,
                    existingA.localPosition,
                    existingB.localPosition,
                    out Vector2 hit))
                {
                    continue;
                }

                bool candidateEndpoint = Approximately(hit, from.localPosition)
                    || Approximately(hit, to.localPosition);
                bool existingEndpoint = Approximately(hit, existingA.localPosition)
                    || Approximately(hit, existingB.localPosition);
                if (!(candidateEndpoint && existingEndpoint))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SegmentsHaveImproperCollinearOverlap(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            Vector2 ab = b - a;
            if (Mathf.Abs(Cross(ab, c - a)) > GeometryEpsilon ||
                Mathf.Abs(Cross(ab, d - a)) > GeometryEpsilon)
            {
                return false;
            }

            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= GeometryEpsilon * GeometryEpsilon)
            {
                return false;
            }

            float cT = Vector2.Dot(c - a, ab) / lengthSquared;
            float dT = Vector2.Dot(d - a, ab) / lengthSquared;
            float overlapStart = Mathf.Max(0f, Mathf.Min(cT, dT));
            float overlapEnd = Mathf.Min(1f, Mathf.Max(cT, dT));
            return overlapEnd - overlapStart > GeometryEpsilon;
        }

        private static bool Approximately(Vector2 lhs, Vector2 rhs)
        {
            return (lhs - rhs).sqrMagnitude <= GeometryEpsilon * GeometryEpsilon;
        }

        private float ApplyCrackPathsWithinBudget(
            List<CrackPathCandidate> paths,
            float impactEnergy)
        {
            paths.Sort((a, b) => a.totalFractureCost.CompareTo(b.totalFractureCost));
            float remainingEnergy = Mathf.Max(0f, impactEnergy);

            for (int i = 0; i < paths.Count; i++)
            {
                CrackPathCandidate path = paths[i];
                if (!CanApplyPathWithoutNewIntersection(path))
                {
                    continue;
                }

                float additionalCost = CalculateMissingConnectionCost(path);
                if (additionalCost > remainingEnergy + GeometryEpsilon)
                {
                    continue;
                }

                CreateMissingConnections(path);
                remainingEnergy = Mathf.Max(0f, remainingEnergy - additionalCost);
            }

            return remainingEnergy;
        }

        private bool CanApplyPathWithoutNewIntersection(CrackPathCandidate path)
        {
            if (!AreDirectlyConnected(path.primary.from.id, path.primary.to.id) &&
                IntersectsExistingCrackImproperly(path.primary.from, path.primary.to))
            {
                return false;
            }

            return !path.hasSecondary
                || AreDirectlyConnected(path.secondary.from.id, path.secondary.to.id)
                || !IntersectsExistingCrackImproperly(path.secondary.from, path.secondary.to);
        }

        private float CalculateMissingConnectionCost(CrackPathCandidate path)
        {
            float cost = AreDirectlyConnected(path.primary.from.id, path.primary.to.id)
                ? 0f
                : path.primary.fractureCost;
            if (path.hasSecondary &&
                !AreDirectlyConnected(path.secondary.from.id, path.secondary.to.id))
            {
                cost += path.secondary.fractureCost;
            }
            return cost;
        }

        private void CreateMissingConnections(CrackPathCandidate path)
        {
            CreateIfMissing(path.primary);
            if (path.hasSecondary)
            {
                CreateIfMissing(path.secondary);
            }

            void CreateIfMissing(CrackCandidate candidate)
            {
                if (candidate.from.id < 0)
                {
                    candidate.from = GetOrCreateNode(
                        candidate.from.localPosition,
                        candidate.from.vulnerability,
                        false);
                }
                if (candidate.to.id < 0)
                {
                    candidate.to = GetOrCreateNode(
                        candidate.to.localPosition,
                        candidate.to.vulnerability,
                        false);
                }
                if (AreDirectlyConnected(candidate.from.id, candidate.to.id))
                {
                    return;
                }
                AddConnection(candidate.from, candidate.to, candidate.fractureCost);
                float crackScore = CalculateWorldCrackLength(
                    candidate.from.localPosition,
                    candidate.to.localPosition);
                ResourceComponent.Instance.Add(crackScore);
                Vector2 localMidpoint =
                    (candidate.from.localPosition + candidate.to.localPosition) * 0.5f;
                ResourceUIManager.Instance?.ShowCrackScore(
                    transform.TransformPoint(localMidpoint),
                    crackScore);
                debugAcceptedConnections.Add(new DebugLine(
                    candidate.from.localPosition,
                    candidate.to.localPosition));
            }
        }

        private void ResetImpactDebugData(Vector2 impactLocalPosition, float scanRadius)
        {
            debugPrimaryCandidates.Clear();
            debugRejectedCandidates.Clear();
            debugAcceptedConnections.Clear();
            debugSecondaryScans.Clear();
            debugSectorBoundaries.Clear();
            debugLastImpact = impactLocalPosition;
            debugLastScanRadius = scanRadius;
            hasDebugImpact = true;
        }

        private void AddSectorDebugLines(
            Vector2 center,
            Vector2 referenceDirection,
            float sectorStart,
            float sectorEnd,
            float radius)
        {
            Vector2 startDirection = Rotate(referenceDirection, sectorStart);
            Vector2 endDirection = Rotate(referenceDirection, sectorEnd);
            debugSectorBoundaries.Add(new DebugLine(center, center + startDirection * radius));
            debugSectorBoundaries.Add(new DebugLine(center, center + endDirection * radius));
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 normalized = vector.sqrMagnitude > GeometryEpsilon * GeometryEpsilon
                ? vector.normalized
                : Vector2.up;
            return new Vector2(
                normalized.x * cos - normalized.y * sin,
                normalized.x * sin + normalized.y * cos);
        }

        private Vector2[][] BuildRenderableCrackPaths()
        {
            if (crackConnections.Count == 0)
            {
                return Array.Empty<Vector2[]>();
            }

            var adjacency = new Dictionary<int, List<int>>();
            for (int edgeIndex = 0; edgeIndex < crackConnections.Count; edgeIndex++)
            {
                CrackConnection connection = crackConnections[edgeIndex];
                AddAdjacent(connection.nodeAId, edgeIndex);
                AddAdjacent(connection.nodeBId, edgeIndex);
            }

            var paths = new List<Vector2[]>();
            var visitedEdges = new bool[crackConnections.Count];

            // 次数1の先端や分岐点から開始し、次数2の区間を一続きの折れ線へまとめる。
            foreach (KeyValuePair<int, List<int>> pair in adjacency)
            {
                if (pair.Value.Count == 2)
                {
                    continue;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int edgeIndex = pair.Value[i];
                    if (!visitedEdges[edgeIndex])
                    {
                        paths.Add(TracePath(pair.Key, edgeIndex, adjacency, visitedEdges));
                    }
                }
            }

            // 全ノードが次数2の閉ループも、未訪問辺から回収する。
            for (int edgeIndex = 0; edgeIndex < crackConnections.Count; edgeIndex++)
            {
                if (!visitedEdges[edgeIndex])
                {
                    paths.Add(TracePath(
                        crackConnections[edgeIndex].nodeAId,
                        edgeIndex,
                        adjacency,
                        visitedEdges));
                }
            }

            return paths.ToArray();

            void AddAdjacent(int nodeId, int edgeIndex)
            {
                if (!adjacency.TryGetValue(nodeId, out List<int> edges))
                {
                    edges = new List<int>();
                    adjacency.Add(nodeId, edges);
                }
                edges.Add(edgeIndex);
            }
        }

        private Vector2[] TracePath(
            int startNodeId,
            int firstEdgeIndex,
            IReadOnlyDictionary<int, List<int>> adjacency,
            bool[] visitedEdges)
        {
            var points = new List<Vector2>();
            int currentNodeId = startNodeId;
            int currentEdgeIndex = firstEdgeIndex;
            CrackNode startNode = GetNode(startNodeId);
            if (startNode == null)
            {
                return Array.Empty<Vector2>();
            }
            points.Add(startNode.localPosition);

            int safety = 0;
            while (currentEdgeIndex >= 0 && safety++ <= crackConnections.Count)
            {
                visitedEdges[currentEdgeIndex] = true;
                CrackConnection connection = crackConnections[currentEdgeIndex];
                int nextNodeId = connection.nodeAId == currentNodeId
                    ? connection.nodeBId
                    : connection.nodeAId;
                CrackNode nextNode = GetNode(nextNodeId);
                if (nextNode == null)
                {
                    break;
                }
                points.Add(nextNode.localPosition);

                if (!adjacency.TryGetValue(nextNodeId, out List<int> adjacentEdges) ||
                    adjacentEdges.Count != 2)
                {
                    break;
                }

                int nextEdgeIndex = -1;
                for (int i = 0; i < adjacentEdges.Count; i++)
                {
                    if (adjacentEdges[i] != currentEdgeIndex && !visitedEdges[adjacentEdges[i]])
                    {
                        nextEdgeIndex = adjacentEdges[i];
                        break;
                    }
                }

                if (nextEdgeIndex < 0)
                {
                    break;
                }
                currentNodeId = nextNodeId;
                currentEdgeIndex = nextEdgeIndex;
            }
            return points.ToArray();
        }

        private bool TrySeparateCompletedPath()
        {
            for (int i = 0; i < cracks.Length; i++)
            {
                Vector2[] path = cracks[i];
                if (path == null || path.Length < 2 ||
                    !IsPointOnOutline(path[0]) ||
                    !IsPointOnOutline(path[^1]))
                {
                    continue;
                }

                if (TrySeparateAlongCrack(path))
                {
                    return true;
                }
            }

            // 分岐点で描画用折れ線が分かれていても、グラフ上で外周同士がつながれば分離へ渡す。
            var boundaryNodeIds = new HashSet<int>();
            for (int i = 0; i < crackConnections.Count; i++)
            {
                AddIfOnBoundary(crackConnections[i].nodeAId);
                AddIfOnBoundary(crackConnections[i].nodeBId);
            }
            var boundaryNodes = new List<int>(boundaryNodeIds);

            for (int startIndex = 0; startIndex < boundaryNodes.Count; startIndex++)
            {
                for (int endIndex = startIndex + 1; endIndex < boundaryNodes.Count; endIndex++)
                {
                    if (TryFindNodePath(
                        boundaryNodes[startIndex],
                        boundaryNodes[endIndex],
                        out Vector2[] continuousPath) &&
                        TrySeparateAlongCrack(continuousPath))
                    {
                        return true;
                    }
                }
            }

            // 通常判定では外周へ届かなかった短い隙間だけを補完する。
            return TryCompleteNearBoundaryPath();

            void AddIfOnBoundary(int nodeId)
            {
                CrackNode node = GetNode(nodeId);
                if (node != null && IsPointOnOutline(node.localPosition))
                {
                    boundaryNodeIds.Add(nodeId);
                }
            }
        }

        private bool TryCompleteNearBoundaryPath()
        {
            if (boundaryCompletionDistance <= GeometryEpsilon ||
                maxBoundaryCompletionCandidates <= 0 ||
                outline == null ||
                outline.Length < 3)
            {
                return false;
            }

            var degreeByNodeId = new int[crackNodes.Count];
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                if (connection.nodeAId >= 0 && connection.nodeAId < degreeByNodeId.Length)
                {
                    degreeByNodeId[connection.nodeAId]++;
                }
                if (connection.nodeBId >= 0 && connection.nodeBId < degreeByNodeId.Length)
                {
                    degreeByNodeId[connection.nodeBId]++;
                }
            }

            float maximumDistanceSquared = boundaryCompletionDistance * boundaryCompletionDistance;
            var candidates = new List<BoundaryCompletionCandidate>();
            for (int nodeId = 0; nodeId < crackNodes.Count; nodeId++)
            {
                CrackNode tip = crackNodes[nodeId];
                if (degreeByNodeId[nodeId] != 1 || IsPointOnOutline(tip.localPosition) ||
                    !TryFindShortestBoundaryPathToNode(nodeId, out Vector2[] pathToTip))
                {
                    continue;
                }

                for (int edgeIndex = 0; edgeIndex < outline.Length; edgeIndex++)
                {
                    Vector2 boundaryPoint = ClosestPointOnSegment(
                        tip.localPosition,
                        outline[edgeIndex],
                        outline[(edgeIndex + 1) % outline.Length]);
                    float distanceSquared = (boundaryPoint - tip.localPosition).sqrMagnitude;
                    if (distanceSquared <= GeometryEpsilon * GeometryEpsilon ||
                        distanceSquared > maximumDistanceSquared ||
                        Approximately(boundaryPoint, pathToTip[0]) ||
                        IntersectsOuterBoundaryBeforeTarget(tip.localPosition, boundaryPoint))
                    {
                        continue;
                    }

                    var boundaryNode = new CrackNode
                    {
                        id = -1,
                        localPosition = boundaryPoint,
                        vulnerability = 1f,
                        isSurfaceFlaw = true
                    };
                    if (IntersectsExistingCrackImproperly(tip, boundaryNode) ||
                        ContainsEquivalentCompletion(candidates, tip.localPosition, boundaryPoint))
                    {
                        continue;
                    }

                    var completedPath = new Vector2[pathToTip.Length + 1];
                    Array.Copy(pathToTip, completedPath, pathToTip.Length);
                    completedPath[^1] = boundaryPoint;
                    candidates.Add(new BoundaryCompletionCandidate
                    {
                        completedPath = completedPath,
                        distanceSquared = distanceSquared
                    });
                }
            }

            candidates.Sort((a, b) => a.distanceSquared.CompareTo(b.distanceSquared));
            int attemptCount = Mathf.Min(maxBoundaryCompletionCandidates, candidates.Count);
            for (int i = 0; i < attemptCount; i++)
            {
                if (TrySeparateAlongCrack(candidates[i].completedPath))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryFindShortestBoundaryPathToNode(int targetNodeId, out Vector2[] shortestPath)
        {
            shortestPath = Array.Empty<Vector2>();
            float shortestLength = float.PositiveInfinity;
            for (int nodeId = 0; nodeId < crackNodes.Count; nodeId++)
            {
                CrackNode node = crackNodes[nodeId];
                if (nodeId == targetNodeId || !IsPointOnOutline(node.localPosition) ||
                    !TryFindNodePath(nodeId, targetNodeId, out Vector2[] candidatePath))
                {
                    continue;
                }

                float length = 0f;
                for (int pointIndex = 0; pointIndex + 1 < candidatePath.Length; pointIndex++)
                {
                    length += Vector2.Distance(candidatePath[pointIndex], candidatePath[pointIndex + 1]);
                }
                if (length < shortestLength)
                {
                    shortestLength = length;
                    shortestPath = candidatePath;
                }
            }
            return shortestPath.Length >= 2;
        }

        private static bool ContainsEquivalentCompletion(
            IReadOnlyList<BoundaryCompletionCandidate> candidates,
            Vector2 tip,
            Vector2 boundaryPoint)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2[] path = candidates[i].completedPath;
                if (path != null && path.Length >= 2 &&
                    Approximately(path[^2], tip) &&
                    Approximately(path[^1], boundaryPoint))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryFindNodePath(int startNodeId, int targetNodeId, out Vector2[] path)
        {
            path = Array.Empty<Vector2>();
            var queue = new Queue<int>();
            var visited = new HashSet<int> { startNodeId };
            var previous = new Dictionary<int, int>();
            queue.Enqueue(startNodeId);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == targetNodeId)
                {
                    break;
                }

                for (int i = 0; i < crackConnections.Count; i++)
                {
                    CrackConnection connection = crackConnections[i];
                    int next = connection.nodeAId == current
                        ? connection.nodeBId
                        : connection.nodeBId == current
                            ? connection.nodeAId
                            : -1;
                    if (next < 0 || !visited.Add(next))
                    {
                        continue;
                    }
                    previous[next] = current;
                    queue.Enqueue(next);
                }
            }

            if (!visited.Contains(targetNodeId))
            {
                return false;
            }

            var nodeIds = new List<int> { targetNodeId };
            int nodeId = targetNodeId;
            while (nodeId != startNodeId)
            {
                if (!previous.TryGetValue(nodeId, out nodeId))
                {
                    return false;
                }
                nodeIds.Add(nodeId);
            }
            nodeIds.Reverse();

            path = new Vector2[nodeIds.Count];
            for (int i = 0; i < nodeIds.Count; i++)
            {
                path[i] = GetNode(nodeIds[i]).localPosition;
            }
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (outline != null && outline.Length >= 2)
            {
                Gizmos.color = Color.cyan;
                for (int i = 0; i < outline.Length; i++)
                {
                    DrawLocalLine(outline[i], outline[(i + 1) % outline.Length]);
                }
            }

            Gizmos.color = Color.yellow;
            if (initCrackPoint != null)
            {
                for (int i = 0; i < initCrackPoint.Length; i++)
                {
                    Gizmos.DrawWireSphere(transform.TransformPoint(initCrackPoint[i]), 0.06f);
                }
            }

            for (int i = 0; i < crackNodes.Count; i++)
            {
                CrackNode node = crackNodes[i];
                if (node.isSurfaceFlaw)
                {
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawSphere(transform.TransformPoint(node.localPosition), 0.075f);
                }
            }

            if (hasDebugImpact)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(transform.TransformPoint(debugLastImpact), debugLastScanRadius);
            }

            DrawDebugLines(debugPrimaryCandidates, new Color(0.2f, 0.6f, 1f));
            DrawDebugLines(debugRejectedCandidates, Color.red);
            DrawDebugLines(debugAcceptedConnections, Color.green);
            DrawDebugLines(debugSectorBoundaries, Color.magenta);

            Gizmos.color = new Color(1f, 0.8f, 0.1f);
            for (int i = 0; i < debugSecondaryScans.Count; i++)
            {
                Gizmos.DrawWireSphere(
                    transform.TransformPoint(debugSecondaryScans[i].Center),
                    debugSecondaryScans[i].Radius);
            }
        }

        private void DrawDebugLines(IReadOnlyList<DebugLine> lines, Color color)
        {
            Gizmos.color = color;
            for (int i = 0; i < lines.Count; i++)
            {
                DrawLocalLine(lines[i].From, lines[i].To);
            }
        }

        private void DrawLocalLine(Vector2 from, Vector2 to)
        {
            Gizmos.DrawLine(transform.TransformPoint(from), transform.TransformPoint(to));
        }

        private void OnValidate()
        {
            surfaceFlawMinimumSpacing = Mathf.Max(0f, surfaceFlawMinimumSpacing);
            crackTipDetectionRadius = Mathf.Max(0f, crackTipDetectionRadius);
            baseFractureResistance = Mathf.Max(0.0001f, baseFractureResistance);
            minimumScanRadius = Mathf.Max(0f, minimumScanRadius);
            maximumScanRadius = Mathf.Max(minimumScanRadius, maximumScanRadius);
            minimumVulnerabilityCostMultiplier = Mathf.Clamp(
                minimumVulnerabilityCostMultiplier,
                0.01f,
                1f);
            angleCostWeight = Mathf.Max(0f, angleCostWeight);
            boundaryCompletionDistance = Mathf.Max(0f, boundaryCompletionDistance);
            maxBoundaryCompletionCandidates = Mathf.Max(1, maxBoundaryCompletionCandidates);
        }

        /// <summary>
        /// 外周から外周へつながるクラックでガラスを二分し、2つの落下破片へ変換します。
        /// クラックはガラスのローカル座標で指定します。
        /// </summary>
        public bool TrySeparateAlongCrack(Vector2[] crack)
        {
            if (isSeparating)
            {
                return false;
            }

            EnsureGeometryInitialized();
            if (!TrySplitPolygon(outline, crack, out Vector2[] firstRegion, out Vector2[] secondRegion) ||
                !IsSafeSplitByArea(firstRegion, secondRegion))
            {
                return false;
            }

            // Destroyまでの同一フレーム中に再び分離されないよう、生成前に再入を止める。
            isSeparating = true;
            if (TryGetComponent(out Collider2D sourceCollider))
            {
                sourceCollider.enabled = false;
            }

            float firstArea = Mathf.Abs(SignedArea(firstRegion));
            float secondArea = Mathf.Abs(SignedArea(secondRegion));
            bool firstIsLargest = firstArea >= secondArea;
            bool firstIsReleased = isReleasedFromAnchor || !firstIsLargest;
            bool secondIsReleased = isReleasedFromAnchor || firstIsLargest;

            CreateFragment(firstRegion, 0, firstIsReleased);
            CreateFragment(secondRegion, 1, secondIsReleased);
            Destroy(gameObject);
            return true;
        }

        private bool IsSafeSplitByArea(
            IReadOnlyList<Vector2> firstRegion,
            IReadOnlyList<Vector2> secondRegion)
        {
            float sourceArea = Mathf.Abs(SignedArea(outline));
            float firstArea = Mathf.Abs(SignedArea(firstRegion));
            float secondArea = Mathf.Abs(SignedArea(secondRegion));
            float minimumArea = glassStatus != null
                ? glassStatus.MinimumBreakableArea
                : 0.04f;

            // 両方とも生成不能になる分割だけを拒否する。片側だけが細片なら分割を許可し、
            // CreateFragment側で細片を生成せず除去することで細い領域の成長阻害を防ぐ。
            if (firstArea <= minimumArea + GeometryEpsilon &&
                secondArea <= minimumArea + GeometryEpsilon)
            {
                return false;
            }

            // 自己交差や外周上の重複で面積が増減する異常分割も拒否する。
            float areaTolerance = Mathf.Max(GeometryEpsilon * 10f, sourceArea * 0.001f);
            return Mathf.Abs(firstArea + secondArea - sourceArea) <= areaTolerance;
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
            bool separated = TrySeparateAlongCrack(completedCrack);
            if (!separated)
            {
                crackGraphInitialized = false;
            }
            return separated;
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

        private float CalculateWorldCrackLength(Vector2 from, Vector2 to)
        {
            return transform.TransformVector(to - from).magnitude;
        }

        private float CalculateWorldFragmentArea(float localArea)
        {
            Vector3 worldRight = transform.TransformVector(Vector3.right);
            Vector3 worldUp = transform.TransformVector(Vector3.up);
            return localArea * Vector3.Cross(worldRight, worldUp).magnitude;
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

        private static Vector2[][] ClipCracksToPolygon(
            IReadOnlyList<Vector2[]> sourceCracks,
            IReadOnlyList<Vector2> polygon)
        {
            var clippedSegments = new List<Vector2[]>();
            if (sourceCracks == null || polygon == null || polygon.Count < 3)
            {
                return clippedSegments.ToArray();
            }

            for (int crackIndex = 0; crackIndex < sourceCracks.Count; crackIndex++)
            {
                Vector2[] path = sourceCracks[crackIndex];
                if (path == null || path.Length < 2)
                {
                    continue;
                }

                for (int pointIndex = 0; pointIndex + 1 < path.Length; pointIndex++)
                {
                    Vector2 start = path[pointIndex];
                    Vector2 end = path[pointIndex + 1];
                    Vector2 direction = end - start;
                    float lengthSquared = direction.sqrMagnitude;
                    if (lengthSquared <= GeometryEpsilon * GeometryEpsilon)
                    {
                        continue;
                    }

                    var parameters = new List<float> { 0f, 1f };
                    for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
                    {
                        Vector2 edgeStart = polygon[edgeIndex];
                        Vector2 edgeEnd = polygon[(edgeIndex + 1) % polygon.Count];
                        if (!TryGetSegmentIntersection(start, end, edgeStart, edgeEnd, out Vector2 hit))
                        {
                            continue;
                        }

                        float parameter = Mathf.Clamp01(
                            Vector2.Dot(hit - start, direction) / lengthSquared);
                        AddUniqueParameter(parameters, parameter);
                    }
                    parameters.Sort();

                    // 外周交点で線分を分割し、破片内部の区間だけを残す。
                    // 新しい外周と重なる分離済みのクラックを継承すると、
                    // 次回着弾時に細片の再分離を繰り返せてしまう。
                    for (int parameterIndex = 0; parameterIndex + 1 < parameters.Count; parameterIndex++)
                    {
                        float fromT = parameters[parameterIndex];
                        float toT = parameters[parameterIndex + 1];
                        if (toT - fromT <= GeometryEpsilon)
                        {
                            continue;
                        }

                        Vector2 middle = Vector2.Lerp(start, end, (fromT + toT) * 0.5f);
                        if (!IsPointStrictlyInsidePolygon(middle, polygon))
                        {
                            continue;
                        }

                        clippedSegments.Add(new[]
                        {
                            Vector2.Lerp(start, end, fromT),
                            Vector2.Lerp(start, end, toT)
                        });
                    }
                }
            }
            return clippedSegments.ToArray();
        }

        private static void AddUniqueParameter(List<float> parameters, float value)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                if (Mathf.Abs(parameters[i] - value) <= GeometryEpsilon)
                {
                    return;
                }
            }
            parameters.Add(value);
        }

        private static bool IsPointStrictlyInsidePolygon(
            Vector2 point,
            IReadOnlyList<Vector2> polygon)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                if (IsPointOnSegment(point, polygon[i], polygon[(i + 1) % polygon.Count]))
                {
                    return false;
                }
            }

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 current = polygon[i];
                Vector2 previous = polygon[j];
                bool crossesRay = (current.y > point.y) != (previous.y > point.y);
                if (!crossesRay)
                {
                    continue;
                }

                float intersectionX = (previous.x - current.x)
                    * (point.y - current.y)
                    / (previous.y - current.y)
                    + current.x;
                if (point.x < intersectionX)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private void CreateFragment(Vector2[] region, int pieceIndex, bool releasedFromAnchor)
        {
            Vector2 centroid = CalculateCentroid(region);
            Vector2[] centeredRegion = new Vector2[region.Length];
            for (int i = 0; i < region.Length; i++)
            {
                centeredRegion[i] = region[i] - centroid;
            }
            float fragmentArea = Mathf.Abs(SignedArea(centeredRegion));
            float minimumBreakableArea = glassStatus != null
                ? glassStatus.MinimumBreakableArea
                : 0.04f;
            // 最小閾値以下の領域は破片Objectを作らず、その場で消滅扱いにする。
            if (fragmentArea <= minimumBreakableArea + GeometryEpsilon)
            {
                return;
            }
            const bool canBreakAgain = true;

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

            Vector2[][] fragmentCracks = ClipCracksToPolygon(cracks, region);
            for (int crackIndex = 0; crackIndex < fragmentCracks.Length; crackIndex++)
            {
                for (int pointIndex = 0; pointIndex < fragmentCracks[crackIndex].Length; pointIndex++)
                {
                    fragmentCracks[crackIndex][pointIndex] -= centroid;
                }
            }

            if (fragmentCracks.Length > 0 || canBreakAgain)
            {
                GameObject crackObject = new GameObject("CrackLineRenderer");
                crackObject.transform.SetParent(fragment.transform, false);
                CrackLineRenderer fragmentCrackRenderer = crackObject.AddComponent<CrackLineRenderer>();
                LineRenderer fragmentCrackLine = crackObject.GetComponent<LineRenderer>();
                if (crackLineRenderer != null &&
                    crackLineRenderer.TryGetComponent(out LineRenderer sourceCrackLine))
                {
                    fragmentCrackLine.sharedMaterial = sourceCrackLine.sharedMaterial;
                }
                else
                {
                    fragmentCrackLine.material = new Material(Shader.Find("Sprites/Default"));
                }
                fragmentCrackRenderer.SetCracks(fragmentCracks);
            }

            PolygonCollider2D collider = fragment.AddComponent<PolygonCollider2D>();
            collider.points = centeredRegion;

            GlassStatus fragmentStatus = fragment.AddComponent<GlassStatus>();
            fragmentStatus.CopyFrom(glassStatus);
            fragmentStatus.SetResourceRewardArea(CalculateWorldFragmentArea(fragmentArea));

            Rigidbody2D body = fragment.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = releasedFromAnchor
                ? fragmentStatus.GravityMultiplier
                : 0f;
            body.constraints = releasedFromAnchor
                ? RigidbodyConstraints2D.None
                : RigidbodyConstraints2D.FreezeAll;
            body.mass = fragmentStatus.CalculateMass(fragmentArea);

            Vector2 inheritedLinearVelocity = Vector2.zero;
            float inheritedAngularVelocity = 0f;
            if (TryGetComponent(out Rigidbody2D sourceBody))
            {
                inheritedLinearVelocity = sourceBody.linearVelocity;
                inheritedAngularVelocity = sourceBody.angularVelocity;
            }
            else if (releasedFromAnchor)
            {
                inheritedAngularVelocity = UnityEngine.Random.Range(-90f, 90f);
            }

            fragment.AddComponent<GlassFragment>();

            if (canBreakAgain)
            {
                CrackProcessingComponent fragmentProcessing = fragment.AddComponent<CrackProcessingComponent>();
                CopyGrowthSettingsTo(fragmentProcessing, pieceIndex);
                fragmentProcessing.Initialize(centeredRegion, fragmentCracks, releasedFromAnchor);
            }

            // CrackProcessingComponent.Awake の初期固定後に、解放済み破片のみ運動を引き継ぐ。
            if (releasedFromAnchor)
            {
                body.linearVelocity = inheritedLinearVelocity;
                body.angularVelocity = inheritedAngularVelocity;
            }

            ResourceUIManager.Instance?.ShowFragmentScore(
                fragment,
                fragmentStatus.resourceRewardArea);
        }

        private void CopyGrowthSettingsTo(CrackProcessingComponent target, int pieceIndex)
        {
            target.crackRandomSeed = unchecked(crackRandomSeed * 397 + pieceIndex + 1);
            target.surfaceFlawMinimumSpacing = surfaceFlawMinimumSpacing;
            target.crackTipDetectionRadius = crackTipDetectionRadius;
            target.baseFractureResistance = baseFractureResistance;
            target.minimumScanRadius = minimumScanRadius;
            target.maximumScanRadius = maximumScanRadius;
            target.minimumVulnerabilityCostMultiplier = minimumVulnerabilityCostMultiplier;
            target.angleCostWeight = angleCostWeight;
            target.boundaryCompletionDistance = boundaryCompletionDistance;
            target.maxBoundaryCompletionCandidates = maxBoundaryCompletionCandidates;
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

            if (glassStatus == null)
            {
                glassStatus = GetComponent<GlassStatus>();
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
