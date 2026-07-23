using System;
using System.Collections.Generic;
using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
        /// <summary>
        /// 外周から外周へつながるクラックでガラスを二分します。
        /// 敵は弱点を含む側をこのGameObjectへ残し、activeTargetsの参照を維持します。
        /// 敵以外は固定中のみ現在の重心を含む側をこのGameObjectへ残します。
        /// クラックはガラスのローカル座標で指定します。
        /// </summary>
        internal bool TrySeparateAlongCrackCore(Vector2[] crack)
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
            bool firstRetainsSource;
            if (enemyDefeat != null && enemyDefeat.HasWeakPoint)
            {
                Vector2 weakPoint = enemyDefeat.WeakPointLocalPosition;
                bool weakPointInFirst = IsPointInsideOrOnPolygon(weakPoint, firstRegion);
                bool weakPointInSecond = IsPointInsideOrOnPolygon(weakPoint, secondRegion);
                firstRetainsSource = weakPointInFirst != weakPointInSecond
                    ? weakPointInFirst
                    : firstArea >= secondArea;
            }
            else
            {
                Vector2 sourceCentroid = CalculateCentroid(outline);
                bool centroidInFirst = IsPointStrictlyInsidePolygon(sourceCentroid, firstRegion);
                bool centroidInSecond = IsPointStrictlyInsidePolygon(sourceCentroid, secondRegion);
                firstRetainsSource = centroidInFirst != centroidInSecond
                    ? centroidInFirst
                    : firstArea >= secondArea;
            }

            bool firstIsReleased = isReleasedFromAnchor || !firstRetainsSource;
            bool secondIsReleased = isReleasedFromAnchor || firstRetainsSource;
            bool retainsOriginalObject = enemyDefeat != null || !isReleasedFromAnchor;

            if (retainsOriginalObject)
            {
                Vector2[] retainedRegion = firstRetainsSource ? firstRegion : secondRegion;
                Vector2[] releasedRegion = firstRetainsSource ? secondRegion : firstRegion;
                int releasedPieceIndex = firstRetainsSource ? 1 : 0;

                var retainedFragments = new List<GameObject>(2) { gameObject };
                GameObject releasedFragment = CreateFragment(
                    releasedRegion,
                    releasedPieceIndex,
                    true);
                if (releasedFragment != null)
                {
                    retainedFragments.Add(releasedFragment);
                }

                BossGlassComponent retainedBoss = GetComponentInParent<BossGlassComponent>();
                bool retainedIsReleased = enemyDefeat != null
                    ? enemyDefeat.IsDefeated
                    : false;
                RetainFragment(retainedRegion, retainedIsReleased);
                retainedBoss?.ReplaceModule(gameObject, retainedFragments);
                return true;
            }

            var fragments = new List<GameObject>(2);
            GameObject firstFragment = CreateFragment(firstRegion, 0, firstIsReleased);
            GameObject secondFragment = CreateFragment(secondRegion, 1, secondIsReleased);
            if (firstFragment != null)
            {
                fragments.Add(firstFragment);
            }
            if (secondFragment != null)
            {
                fragments.Add(secondFragment);
            }

            BossGlassComponent boss = GetComponentInParent<BossGlassComponent>();
            boss?.ReplaceModule(gameObject, fragments);
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
        internal bool TryExtendCrackToBoundaryCore(int crackIndex, Vector2 newCrackPosition)
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

        private static bool IsPointInsideOrOnPolygon(
            Vector2 point,
            IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                if (IsPointOnSegment(point, polygon[i], polygon[(i + 1) % polygon.Count]))
                {
                    return true;
                }
            }

            return IsPointStrictlyInsidePolygon(point, polygon);
        }

        private void RetainFragment(Vector2[] region, bool releasedFromAnchor)
        {
            float fragmentArea = Mathf.Abs(SignedArea(region));
            Vector2[][] retainedCracks = ClipCracksToPolygon(cracks, region);
            var retainedInitialPoints = new List<Vector2>();
            if (initCrackPoint != null)
            {
                for (int i = 0; i < initCrackPoint.Length; i++)
                {
                    if (IsPointInsideOrOnPolygon(initCrackPoint[i], region))
                    {
                        retainedInitialPoints.Add(initCrackPoint[i]);
                    }
                }
            }

            if (enemyDefeat != null && enemyDefeat.HasWeakPoint)
            {
                Vector2 weakPoint = enemyDefeat.WeakPointLocalPosition;
                bool containsWeakPoint = false;
                for (int i = 0; i < retainedInitialPoints.Count; i++)
                {
                    if ((retainedInitialPoints[i] - weakPoint).sqrMagnitude <=
                        GeometryEpsilon * GeometryEpsilon)
                    {
                        containsWeakPoint = true;
                        break;
                    }
                }
                if (!containsWeakPoint)
                {
                    retainedInitialPoints.Add(weakPoint);
                }
            }

            outline = CleanPolygon(region);
            cracks = retainedCracks;
            initCrackPoint = retainedInitialPoints.ToArray();
            crackNodes.Clear();
            crackConnections.Clear();
            crackGraphInitialized = false;
            crackRandom = null;
            pooledImpactEnergy = 0f;
            anchorFailureEnergy = 0f;
            isReleasedFromAnchor = releasedFromAnchor;
            overkillEvaluationConsumed = true;

            if (outlineLineRenderer != null)
            {
                outlineLineRenderer.SetOutline(outline);
            }
            RenderCracks();

            if (TryGetComponent(out PolygonCollider2D collider))
            {
                collider.points = outline;
                collider.enabled = true;
            }

            if (glassStatus != null)
            {
                glassStatus.SetResourceRewardArea(CalculateWorldFragmentArea(fragmentArea));
            }

            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
            }
            body.mass = glassStatus != null
                ? glassStatus.CalculateMass(fragmentArea)
                : Mathf.Max(0.05f, fragmentArea);
            ApplyAnchorState();

            if (!TryGetComponent(out GlassFragment _))
            {
                gameObject.AddComponent<GlassFragment>();
            }

            if (glassStatus == null || !glassStatus.IsResourceRewardSuppressed)
            {
                ResourceUIManager.Instance?.ShowFragmentScore(
                    gameObject,
                    glassStatus != null ? glassStatus.ResourceReward : 0f);
            }

            // 同じGameObjectが次の着弾でさらに分割できるよう再入防止を解除する。
            isSeparating = false;
        }

        private GameObject CreateFragment(Vector2[] region, int pieceIndex, bool releasedFromAnchor)
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
                return null;
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

            if (!fragmentStatus.IsResourceRewardSuppressed)
            {
                ResourceUIManager.Instance?.ShowFragmentScore(
                    fragment,
                    fragmentStatus.ResourceReward);
            }

            return fragment;
        }

        private void CopyGrowthSettingsTo(CrackProcessingComponent target, int pieceIndex)
        {
            target.crackRandomSeed = unchecked(crackRandomSeed * 397 + pieceIndex + 1);
            target.surfaceFlawMinimumSpacing = surfaceFlawMinimumSpacing;
            target.crackTipDetectionRadius = crackTipDetectionRadius;
            // 破片は元オブジェクトの初回判定後に生まれるため、再度オーバーキル判定しない。
            target.overkillEvaluationConsumed = true;
            target.minimumScanRadius = minimumScanRadius;
            target.maximumScanRadius = maximumScanRadius;
            target.minimumVulnerabilityCostMultiplier = minimumVulnerabilityCostMultiplier;
            target.angleCostWeight = angleCostWeight;
            target.terminalFragmentMaximumArea = terminalFragmentMaximumArea;
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

            if (enemyDefeat == null)
            {
                enemyDefeat = GetComponent<EnemyDefeatComponent>();
            }

            if (outlineLineRenderer == null)
            {
                outlineLineRenderer = glassRoot.GetComponentInChildren<GlassSurfaceLineRenderer>(true);
            }

            if (crackLineRenderer == null)
            {
                crackLineRenderer = glassRoot.GetComponentInChildren<CrackLineRenderer>(true);
            }
        }    }
}
