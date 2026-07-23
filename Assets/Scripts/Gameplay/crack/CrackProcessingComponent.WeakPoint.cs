using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
        private const int WeakPointTriggerDegree = 2;

        private void EnsureEnemyWeakPointInitialized()
        {
            if (enemyDefeat == null || enemyDefeat.HasWeakPoint ||
                initCrackPoint == null || initCrackPoint.Length == 0)
            {
                return;
            }

            Vector2 referencePosition = glassStatus != null
                ? glassStatus.CorePosition
                : CalculateCentroid(outline);
            if (!IsPointInsideOrOnOutline(referencePosition))
            {
                referencePosition = CalculateCentroid(outline);
            }

            Vector2 selected = initCrackPoint[0];
            float nearestDistanceSquared = (selected - referencePosition).sqrMagnitude;
            for (int i = 1; i < initCrackPoint.Length; i++)
            {
                float distanceSquared = (initCrackPoint[i] - referencePosition).sqrMagnitude;
                if (distanceSquared < nearestDistanceSquared)
                {
                    selected = initCrackPoint[i];
                    nearestDistanceSquared = distanceSquared;
                }
            }

            enemyDefeat.InitializeWeakPoint(selected);
        }

        private bool IsEnemyWeakPoint(Vector2 localPosition)
        {
            return enemyDefeat != null &&
                enemyDefeat.IsWeakPoint(localPosition, GeometryEpsilon);
        }

        private bool EvaluateWeakPointDefeat()
        {
            if (enemyDefeat == null || enemyDefeat.IsDefeated || !enemyDefeat.HasWeakPoint)
            {
                return false;
            }

            CrackNode weakPointNode = FindNodeAt(enemyDefeat.WeakPointLocalPosition);
            if (weakPointNode == null)
            {
                return false;
            }

            int degree = 0;
            for (int i = 0; i < crackConnections.Count; i++)
            {
                CrackConnection connection = crackConnections[i];
                if (connection.nodeAId == weakPointNode.id || connection.nodeBId == weakPointNode.id)
                {
                    degree++;
                }
            }

            if (degree < WeakPointTriggerDegree)
            {
                return false;
            }

            return MarkEnemyDefeated();
        }

        private bool MarkEnemyDefeated()
        {
            if (enemyDefeat == null || enemyDefeat.IsDefeated)
            {
                return false;
            }

            ReleaseFromAnchorAfterDefeat();
            return enemyDefeat.MarkDefeated();
        }

        private void ReleaseFromAnchorAfterDefeat()
        {
            isReleasedFromAnchor = true;
            anchorFailureEnergy = 0f;

            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
            }

            float area = Mathf.Abs(SignedArea(outline));
            body.mass = glassStatus != null
                ? glassStatus.CalculateMass(area)
                : Mathf.Max(0.05f, area);
            ApplyAnchorState();
        }
    }
}
