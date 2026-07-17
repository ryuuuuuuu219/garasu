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
    [RequireComponent(typeof(CrackGrowthComponent))]
    [RequireComponent(typeof(CrackFragmentationComponent))]
    public sealed partial class CrackProcessingComponent : MonoBehaviour
    {
        [Header("Glass References")]
        [SerializeField] private GameObject glassRoot = null;
        [SerializeField] private GlassSurfaceLineRenderer outlineLineRenderer = null;
        [SerializeField] private CrackLineRenderer crackLineRenderer = null;
        [SerializeField] private GlassStatus glassStatus = null;

        [Header("Feature Components")]
        [SerializeField] private CrackGrowthComponent growthComponent = null;
        [SerializeField] private CrackFragmentationComponent fragmentationComponent = null;

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

        [Header("Terminal Fragment Release")]
        [SerializeField, Min(0f)] private float terminalFragmentMaximumArea = 0.5f;
        [SerializeField, Min(0f)] private float anchorFailureEnergy;

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
        public CrackGrowthComponent GrowthComponent => growthComponent;
        public CrackFragmentationComponent FragmentationComponent => fragmentationComponent;

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
            ResolveFeatureComponents();
            ResolveMissingReferences();
            EnsureGeometryInitialized();
            ApplyAnchorState();
            RenderCracks();
        }

        private void Reset()
        {
            ResolveFeatureComponents();
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

        public bool HealCracks()
        {
            ResolveFeatureComponents();
            return growthComponent.HealCracks();
        }

        public void HandleProjectileImpact(Vector2 projectileWorldPosition, BulletStatus bulletStatus)
        {
            ResolveFeatureComponents();
            growthComponent.HandleProjectileImpact(projectileWorldPosition, bulletStatus);
        }

        public void HandleBulletImpact(Vector2 impactWorldPosition, BulletStatus bulletStatus)
        {
            ResolveFeatureComponents();
            growthComponent.HandleBulletImpact(impactWorldPosition, bulletStatus);
        }

        public bool TrySeparateAlongCrack(Vector2[] crack)
        {
            ResolveFeatureComponents();
            return fragmentationComponent.TrySeparateAlongCrack(crack);
        }

        public bool TryExtendCrackToBoundary(int crackIndex, Vector2 newCrackPosition)
        {
            ResolveFeatureComponents();
            return fragmentationComponent.TryExtendCrackToBoundary(crackIndex, newCrackPosition);
        }

        private void ResolveFeatureComponents()
        {
            if (growthComponent == null)
            {
                growthComponent = GetComponent<CrackGrowthComponent>();
            }
            if (growthComponent == null)
            {
                growthComponent = gameObject.AddComponent<CrackGrowthComponent>();
            }

            if (fragmentationComponent == null)
            {
                fragmentationComponent = GetComponent<CrackFragmentationComponent>();
            }
            if (fragmentationComponent == null)
            {
                fragmentationComponent = gameObject.AddComponent<CrackFragmentationComponent>();
            }

            growthComponent.Bind(this);
            fragmentationComponent.Bind(this);
        }

    }
}
