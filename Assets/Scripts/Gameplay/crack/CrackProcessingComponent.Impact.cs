using System;
using System.Collections.Generic;
using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
        /// <summary>クラックの末端をランダムに1つ選び、末端側の一区間を修復します。</summary>
        internal bool HealCracksCore()
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
            bool preservesWeakPoint = IsEnemyWeakPoint(crackNodes[terminalNodeId].localPosition);
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

            if (preservesWeakPoint)
            {
                cracks = BuildRenderableCrackPaths();
                RenderCracks();
                return true;
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

        internal void HandleProjectileImpactCore(Vector2 projectileWorldPosition, BulletStatus bulletStatus)
        {
            EnsureGeometryInitialized();
            // Triggerには接触点情報がないため、弾中心をガラス外周へ投影して代表接触点にする。
            Vector2 projectileLocalPosition = transform.InverseTransformPoint(projectileWorldPosition);
            Vector2 impactLocalPosition = GetClosestPointOnOutline(projectileLocalPosition);
            Vector2 impactWorldPosition = transform.TransformPoint(impactLocalPosition);
            HandleBulletImpact(impactWorldPosition, bulletStatus);
        }

        private float pooledImpactEnergy;
        internal void HandleBulletImpactCore(Vector2 impactWorldPosition, BulletStatus bulletStatus)
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

            if (enemyDefeat == null &&
                TryGetComponent(out GlassFragment _) &&
                glassStatus != null &&
                Mathf.Abs(SignedArea(outline)) + GeometryEpsilon < glassStatus.MinimumBreakableArea)
            {
                return;
            }

            Debug.Log("1");

            EnsureCrackGraphInitialized();
            float enemyEnergyMultiplier = glassStatus != null
                ? glassStatus.EnemyCrackEnergyMultiplier
                : 1f;
            float newImpactEnergy = bulletStatus.CalculateKineticEnergy()
                * bulletStatus.CrackConversionEfficiency
                * enemyEnergyMultiplier;

            // オーバーキルは、このCrackProcessingComponentが受けた最初の有効攻撃だけで判定する。
            // 判定失敗も消費扱いとし、蓄積エネルギーは一切含めない。
            if (!overkillEvaluationConsumed)
            {
                overkillEvaluationConsumed = true;
                Vector2 overkillImpact = GetClosestPointOnOutline(impactLocalPosition);
                if (TryHandleFirstImpactOverkill(overkillImpact, newImpactEnergy))
                {
                    return;
                }
            }

            CrackNode surfaceFlaw = FindOrCreateSurfaceFlaw(impactLocalPosition);
            CrackNode startNode = FindCrackTipFromSurfaceRootOrFallback(
                surfaceFlaw,
                impactLocalPosition);
            Vector2 referenceDirection = ResolveReferenceDirection(startNode, bulletStatus.CurrentVelocity);

            float impactEnergy = newImpactEnergy + pooledImpactEnergy;
            float scanRadius = CalculateScanRadius(impactEnergy);

            ResetImpactDebugData(impactLocalPosition, scanRadius);
            List<CrackPathCandidate> paths = BuildCrackPathCandidates(
                startNode,
                referenceDirection,
                scanRadius,
                impactEnergy);

            // 実際にクラック形成へ使われなかった分だけを、ガラス全体で次回へ持ち越す。
            // 候補がない場合や全候補が予算超過の場合は、全量がそのまま残る。
            pooledImpactEnergy = ApplyCrackPathsWithinBudget(
                paths,
                impactEnergy,
                out bool crackProgressed);
            EvaluateWeakPointDefeat();
            cracks = BuildRenderableCrackPaths();

            AccumulateFailedImpactAndReleaseTerminalFragment(
                crackProgressed,
                newImpactEnergy);

            // ボス本体は蓄積破砕を前提とするため、着弾で縮小させない。
            // 分離後の通常破片にはBossGlassComponentを継承しないので、
            // 追撃分だけ最終回収面積が減る。
            bool preventsImpactShrink = enemyDefeat != null ||
                TryGetComponent(out BossGlassComponent _);
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

        private void AccumulateFailedImpactAndReleaseTerminalFragment(
            bool crackProgressed,
            float newImpactEnergy)
        {
            if (crackProgressed ||
                isReleasedFromAnchor ||
                terminalFragmentMaximumArea <= 0f ||
                !TryGetComponent(out GlassFragment _) ||
                (enemyDefeat != null && !enemyDefeat.IsDefeated))
            {
                return;
            }

            float currentArea = Mathf.Abs(SignedArea(outline));
            if (currentArea > terminalFragmentMaximumArea + GeometryEpsilon)
            {
                return;
            }

            // pooledImpactEnergyには過去の未使用分も含まれるため、二重計上を避けて
            // 今回の着弾で新しく入った分だけを固定破壊エネルギーへ加える。
            anchorFailureEnergy += Mathf.Max(0f, newImpactEnergy);
            float releaseThreshold = glassStatus != null
                ? glassStatus.FixedPositionStrength
                : 1f;
            if (anchorFailureEnergy + GeometryEpsilon < releaseThreshold)
            {
                return;
            }

            anchorFailureEnergy = 0f;
            isReleasedFromAnchor = true;
            ApplyAnchorState();
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

    }
}
