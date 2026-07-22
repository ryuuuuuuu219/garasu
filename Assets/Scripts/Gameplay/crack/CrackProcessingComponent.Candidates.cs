using System;
using System.Collections.Generic;
using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
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

            return distance * vulnerabilityMultiplier * angleMultiplier;
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

    }
}
