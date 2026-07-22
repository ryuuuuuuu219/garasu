using System;
using System.Collections.Generic;
using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
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
                InitialCrackPointData[] generated = glassStatus.GenerateInitialCrackPointData(
                    outline,
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
                            Mathf.Max(GeometryEpsilon, Vector2.Distance(from.localPosition, to.localPosition)));
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
            float energyRatio = Mathf.Max(0f, impactEnergy);

            // 面積の平方根とエネルギー比の平方根から一次走査距離を得る。
            float scanRadius = Mathf.Sqrt(glassArea) * Mathf.Sqrt(energyRatio);
            return Mathf.Clamp(scanRadius, minimumScanRadius, maximumScanRadius);
        }

    }
}
