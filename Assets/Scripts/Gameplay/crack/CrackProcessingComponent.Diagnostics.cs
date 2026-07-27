using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
        internal int CrackRandomSeedForDiagnostics => crackRandomSeed;

        private void ResolveDiagnosticsLogger()
        {
            if (EnvironmentManager.Instance == null ||
                !EnvironmentManager.Instance.CrackDiagnosticsEnabled)
            {
                crackDiagnosticsLogger = null;
                return;
            }

            if (crackDiagnosticsLogger == null)
            {
                crackDiagnosticsLogger = GetComponent<CrackDiagnosticsLogger>();
            }
            if (crackDiagnosticsLogger == null && Application.isPlaying)
            {
                crackDiagnosticsLogger = gameObject.AddComponent<CrackDiagnosticsLogger>();
            }
            crackDiagnosticsLogger?.Bind(this);
        }

        private int GetNodeDegree(CrackNode node)
        {
            if (node == null)
            {
                return 0;
            }

            int degree = 0;
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                if (connection.nodeAId == node.id || connection.nodeBId == node.id)
                {
                    degree++;
                }
            }
            return degree;
        }

        private void DiagnoseWeakPointState(string stage)
        {
            if (crackDiagnosticsLogger == null ||
                enemyDefeat == null ||
                !enemyDefeat.HasWeakPoint)
            {
                return;
            }

            Vector2 position = enemyDefeat.WeakPointLocalPosition;
            CrackNode node = FindNodeAt(position);
            crackDiagnosticsLogger.ObserveWeakPoint(
                stage,
                position,
                node != null,
                IsPointOnOutline(position),
                FindOutlineVertex(position) >= 0,
                GetNodeDegree(node),
                crackNodes.Count,
                crackConnections.Count);
        }

        private bool IsWeakPointNode(CrackNode node)
        {
            return node != null && IsEnemyWeakPoint(node.localPosition);
        }

        private void DiagnoseNodeVulnerabilityState(string stage)
        {
            if (crackDiagnosticsLogger == null)
            {
                return;
            }

            for (int i = 0; i < crackNodes.Count; i++)
            {
                CrackNode node = crackNodes[i];
                bool isWeakPoint = IsEnemyWeakPoint(node.localPosition);
                bool isVertex = FindOutlineVertex(node.localPosition) >= 0;
                bool isOnOutline = IsPointOnOutline(node.localPosition);
                bool isFallback = IsBoundaryFallbackPoint(node.localPosition);
                if (isWeakPoint)
                {
                    crackDiagnosticsLogger.RecordVulnerabilityInvariant(
                        "WEAKPOINT_VULNERABILITY_MISMATCH",
                        stage,
                        node.localPosition,
                        WeakPointVulnerability,
                        node.vulnerability);
                    continue;
                }
                if (isVertex)
                {
                    crackDiagnosticsLogger.RecordVulnerabilityInvariant(
                        "OUTLINE_VERTEX_VULNERABILITY_NONZERO",
                        stage,
                        node.localPosition,
                        0f,
                        node.vulnerability);
                    continue;
                }
                if (isFallback)
                {
                    crackDiagnosticsLogger.RecordVulnerabilityInvariant(
                        "BOUNDARY_FALLBACK_VULNERABILITY_MISMATCH",
                        stage,
                        node.localPosition,
                        1f,
                        node.vulnerability);
                    continue;
                }

                if (node.isSurfaceFlaw || isOnOutline)
                {
                    crackDiagnosticsLogger.RecordVulnerabilityInvariant(
                        node.isSurfaceFlaw
                            ? "SURFACE_FLAW_VULNERABILITY_NONZERO"
                            : "OUTLINE_POINT_VULNERABILITY_NONZERO",
                        stage,
                        node.localPosition,
                        0f,
                        node.vulnerability);
                    continue;
                }

                crackDiagnosticsLogger.RecordVulnerabilityRangeInvariant(
                    "INTERNAL_POINT_VULNERABILITY_OUT_OF_RANGE",
                    stage,
                    node.localPosition,
                    MinimumInternalNodeVulnerability,
                    MaximumInternalNodeVulnerability,
                    node.vulnerability);
                crackDiagnosticsLogger.TrackInternalVulnerability(
                    stage,
                    node.localPosition,
                    node.vulnerability,
                    false);
            }
        }

        private void DiagnoseConnectionBypassesWeakPoint(
            CrackNode from,
            CrackNode to,
            string stage)
        {
            if (crackDiagnosticsLogger == null ||
                enemyDefeat == null ||
                !enemyDefeat.HasWeakPoint ||
                from == null ||
                to == null)
            {
                return;
            }

            Vector2 weakPoint = enemyDefeat.WeakPointLocalPosition;
            if (Approximately(from.localPosition, weakPoint) ||
                Approximately(to.localPosition, weakPoint))
            {
                return;
            }

            Vector2 closest = ClosestPointOnSegment(
                weakPoint,
                from.localPosition,
                to.localPosition);
            float distance = Vector2.Distance(weakPoint, closest);
            if (distance > GeometryEpsilon)
            {
                return;
            }

            CrackNode weakPointNode = FindNodeAt(weakPoint);
            crackDiagnosticsLogger.RecordWeakPointBypass(
                stage,
                from.localPosition,
                to.localPosition,
                weakPoint,
                distance,
                GetNodeDegree(weakPointNode));
        }

        private bool IsWeakPointOnPath(
            Vector2[] path,
            out bool explicitlyInPath)
        {
            explicitlyInPath = false;
            if (enemyDefeat == null ||
                !enemyDefeat.HasWeakPoint ||
                path == null ||
                path.Length < 2)
            {
                return false;
            }

            Vector2 weakPoint = enemyDefeat.WeakPointLocalPosition;
            for (int i = 0; i < path.Length; i++)
            {
                if (Approximately(path[i], weakPoint))
                {
                    explicitlyInPath = true;
                    return true;
                }
            }
            for (int i = 0; i + 1 < path.Length; i++)
            {
                if (IsPointOnSegment(weakPoint, path[i], path[i + 1]))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
