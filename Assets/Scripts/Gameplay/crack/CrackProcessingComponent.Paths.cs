using System;
using System.Collections.Generic;
using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
        private float ApplyCrackPathsWithinBudget(
            List<CrackPathCandidate> paths,
            float impactEnergy,
            out bool crackProgressed)
        {
            paths.Sort((a, b) => a.totalFractureCost.CompareTo(b.totalFractureCost));
            float remainingEnergy = Mathf.Max(0f, impactEnergy);
            crackProgressed = false;

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

                int connectionCountBefore = crackConnections.Count;
                CreateMissingConnections(path);
                crackProgressed |= crackConnections.Count > connectionCountBefore;
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
            terminalFragmentMaximumArea = Mathf.Max(0f, terminalFragmentMaximumArea);
            anchorFailureEnergy = Mathf.Max(0f, anchorFailureEnergy);
            boundaryCompletionDistance = Mathf.Max(0f, boundaryCompletionDistance);
            maxBoundaryCompletionCandidates = Mathf.Max(1, maxBoundaryCompletionCandidates);
        }

    }
}
