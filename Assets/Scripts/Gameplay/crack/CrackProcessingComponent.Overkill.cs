using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    public sealed partial class CrackProcessingComponent
    {
        private sealed class OverkillRay
        {
            public float angle;
            public Vector2 boundaryPoint;
            public float length;
            public readonly List<Vector2> virtualPoints = new List<Vector2>();
        }

        private sealed class OverkillPlan
        {
            public readonly List<Vector2[]> radialRegions = new List<Vector2[]>();
            public readonly List<Vector2[]> triangles = new List<Vector2[]>();
            public float totalRadialLength;
            public float totalCutLength;
            public float longestRadialLength;
            public float longestPlannedCutLength;
        }

        private bool TryHandleFirstImpactOverkill(Vector2 impactLocalPosition, float cutImpactEnergy)
        {
            if (cutImpactEnergy <= GeometryEpsilon ||
                !TryBuildOverkillPlan(impactLocalPosition, out OverkillPlan plan))
            {
                return false;
            }

            float area = Mathf.Abs(SignedArea(outline));
            float uncappedReach = Mathf.Sqrt(area) * Mathf.Sqrt(cutImpactEnergy);
            bool radialSatisfied =
                uncappedReach + GeometryEpsilon >= plan.longestRadialLength &&
                cutImpactEnergy > plan.totalRadialLength;
            if (!radialSatisfied)
            {
                return false;
            }

            bool triangleSatisfied =
                plan.triangles.Count > 0 &&
                uncappedReach + GeometryEpsilon >= plan.longestPlannedCutLength &&
                cutImpactEnergy > plan.totalCutLength;
            return ExecuteOverkillFragments(
                triangleSatisfied ? plan.triangles : plan.radialRegions,
                triangleSatisfied);
        }

        private bool TryBuildOverkillPlan(Vector2 impact, out OverkillPlan plan)
        {
            plan = null;
            if (outline == null || outline.Length < 3 || initCrackPoint == null || initCrackPoint.Length == 0)
            {
                return false;
            }

            float maximumDistance = CalculateOutlineDiagonal() * 2f + 1f;
            var rays = new List<OverkillRay>();
            for (int i = 0; i < initCrackPoint.Length; i++)
            {
                Vector2 delta = initCrackPoint[i] - impact;
                if (delta.sqrMagnitude <= GeometryEpsilon * GeometryEpsilon)
                {
                    continue;
                }

                Vector2 direction = delta.normalized;
                if (!TryGetFirstBoundaryIntersection(
                        impact,
                        direction,
                        maximumDistance,
                        out Vector2 boundaryPoint) ||
                    Vector2.Distance(impact, boundaryPoint) + GeometryEpsilon < delta.magnitude ||
                    !IsOverkillSegmentInside(impact, boundaryPoint))
                {
                    continue;
                }

                float angle = Mathf.Atan2(direction.y, direction.x);
                OverkillRay ray = FindSameDirectionRay(rays, angle);
                if (ray == null)
                {
                    ray = new OverkillRay
                    {
                        angle = angle,
                        boundaryPoint = boundaryPoint,
                        length = Vector2.Distance(impact, boundaryPoint)
                    };
                    rays.Add(ray);
                }
                ray.virtualPoints.Add(initCrackPoint[i]);
            }

            if (rays.Count < 2)
            {
                return false;
            }
            rays.Sort((a, b) => a.angle.CompareTo(b.angle));

            var built = new OverkillPlan();
            built.radialRegions.Add((Vector2[])outline.Clone());
            for (int rayIndex = 0; rayIndex < rays.Count; rayIndex++)
            {
                OverkillRay ray = rays[rayIndex];
                ray.virtualPoints.Sort((a, b) =>
                    (a - impact).sqrMagnitude.CompareTo((b - impact).sqrMagnitude));
                var cut = new List<Vector2>(ray.virtualPoints.Count + 2) { impact };
                cut.AddRange(ray.virtualPoints);
                cut.Add(ray.boundaryPoint);

                bool split = false;
                for (int regionIndex = 0; regionIndex < built.radialRegions.Count; regionIndex++)
                {
                    if (!TrySplitPolygon(
                            built.radialRegions[regionIndex],
                            cut.ToArray(),
                            out Vector2[] first,
                            out Vector2[] second))
                    {
                        continue;
                    }

                    built.radialRegions[regionIndex] = first;
                    built.radialRegions.Insert(regionIndex + 1, second);
                    split = true;
                    break;
                }
                if (!split)
                {
                    return false;
                }

                built.totalRadialLength += ray.length;
                built.longestRadialLength = Mathf.Max(built.longestRadialLength, ray.length);
            }

            built.totalCutLength = built.totalRadialLength;
            built.longestPlannedCutLength = built.longestRadialLength;
            for (int i = 0; i < built.radialRegions.Count; i++)
            {
                if (!TryTriangulateFromVirtualPoint(
                        built.radialRegions[i],
                        out List<Vector2[]> regionTriangles,
                        out float addedLength,
                        out float longestAdded))
                {
                    built.triangles.Clear();
                    break;
                }
                built.triangles.AddRange(regionTriangles);
                built.totalCutLength += addedLength;
                built.longestPlannedCutLength = Mathf.Max(
                    built.longestPlannedCutLength,
                    longestAdded);
            }

            plan = built;
            return true;
        }

        private OverkillRay FindSameDirectionRay(List<OverkillRay> rays, float angle)
        {
            const float angleEpsilon = 0.0001f;
            for (int i = 0; i < rays.Count; i++)
            {
                float difference = Mathf.Abs(Mathf.DeltaAngle(
                    rays[i].angle * Mathf.Rad2Deg,
                    angle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (difference <= angleEpsilon)
                {
                    return rays[i];
                }
            }
            return null;
        }

        private bool IsOverkillSegmentInside(Vector2 from, Vector2 to)
        {
            return IsPointInsideOrOnOutline(Vector2.Lerp(from, to, 0.25f)) &&
                IsPointInsideOrOnOutline(Vector2.Lerp(from, to, 0.5f)) &&
                IsPointInsideOrOnOutline(Vector2.Lerp(from, to, 0.75f));
        }

        private float CalculateOutlineDiagonal()
        {
            Vector2 minimum = outline[0];
            Vector2 maximum = outline[0];
            for (int i = 1; i < outline.Length; i++)
            {
                minimum = Vector2.Min(minimum, outline[i]);
                maximum = Vector2.Max(maximum, outline[i]);
            }
            return Vector2.Distance(minimum, maximum);
        }

        private bool TryTriangulateFromVirtualPoint(
            Vector2[] region,
            out List<Vector2[]> bestTriangles,
            out float bestAddedLength,
            out float longestAddedLength)
        {
            bestTriangles = null;
            bestAddedLength = float.PositiveInfinity;
            longestAddedLength = 0f;
            for (int baseIndex = 0; baseIndex < region.Length; baseIndex++)
            {
                if (!IsGeneratedVirtualPoint(region[baseIndex]))
                {
                    continue;
                }

                var candidate = new List<Vector2[]>(region.Length - 2);
                float addedLength = 0f;
                float candidateLongest = 0f;
                bool valid = true;
                for (int offset = 1; offset < region.Length - 1; offset++)
                {
                    Vector2 a = region[baseIndex];
                    Vector2 b = region[(baseIndex + offset) % region.Length];
                    Vector2 c = region[(baseIndex + offset + 1) % region.Length];
                    var triangle = new[] { a, b, c };
                    if (Mathf.Abs(SignedArea(triangle)) <= GeometryEpsilon ||
                        !IsPointInsidePolygonLocal((a + b + c) / 3f, region))
                    {
                        valid = false;
                        break;
                    }
                    candidate.Add(triangle);

                    if (offset > 1)
                    {
                        float diagonalLength = Vector2.Distance(a, b);
                        addedLength += diagonalLength;
                        candidateLongest = Mathf.Max(candidateLongest, diagonalLength);
                    }
                }

                if (valid && candidate.Count == region.Length - 2 && addedLength < bestAddedLength)
                {
                    bestTriangles = candidate;
                    bestAddedLength = addedLength;
                    longestAddedLength = candidateLongest;
                }
            }
            return bestTriangles != null;
        }

        private bool IsGeneratedVirtualPoint(Vector2 point)
        {
            float epsilonSquared = GeometryEpsilon * GeometryEpsilon * 4f;
            for (int i = 0; i < initCrackPoint.Length; i++)
            {
                if ((initCrackPoint[i] - point).sqrMagnitude <= epsilonSquared)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsPointInsidePolygonLocal(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private bool ExecuteOverkillFragments(List<Vector2[]> regions, bool grantInstantReward)
        {
            float minimumArea = glassStatus != null ? glassStatus.MinimumBreakableArea : 0.04f;
            int fragmentCount = 0;
            for (int i = 0; i < regions.Count; i++)
            {
                if (Mathf.Abs(SignedArea(regions[i])) > minimumArea + GeometryEpsilon)
                {
                    fragmentCount++;
                }
            }
            if (fragmentCount == 0)
            {
                return false;
            }

            isSeparating = true;
            pooledImpactEnergy = 0f;
            if (TryGetComponent(out Collider2D sourceCollider))
            {
                sourceCollider.enabled = false;
            }

            var fragments = new List<GameObject>(fragmentCount);
            int pieceIndex = 0;
            for (int i = 0; i < regions.Count; i++)
            {
                GameObject fragment = CreateFragment(regions[i], pieceIndex++, true);
                if (fragment != null)
                {
                    fragments.Add(fragment);
                }
            }

            // 三角形化オーバーキルだけが即時報酬を持つ。落下回収分は各破片に残す。
            if (grantInstantReward && fragments.Count > 0 && ResourceComponent.Instance != null)
            {
                float worldArea = CalculateWorldFragmentArea(Mathf.Abs(SignedArea(outline)));
                float reward = worldArea * worldArea *
                    (1f + Mathf.Pow(1.01f, fragments.Count));
                ResourceComponent.Instance.Add(reward);
            }

            BossGlassComponent boss = GetComponentInParent<BossGlassComponent>();
            boss?.ReplaceModule(gameObject, fragments);
            Destroy(gameObject);
            return true;
        }
    }
}
