using System;
using System.Collections.Generic;
using UnityEngine;

namespace PolygonRendering
{
    /// <summary>
    /// ガラス内部のクラックを、ローカル XY 平面上の開いた折れ線として描画します。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class CrackLineRenderer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("クラックの経路です。ローカル XY 座標で指定します。")]
        private Vector2[] crackPoints = Array.Empty<Vector2>();

        [SerializeField] private Color startColor = new Color(0.75f, 0.95f, 1f, 1f);
        [SerializeField] private Color endColor = new Color(0.2f, 0.65f, 1f, 0.35f);
        [SerializeField, Min(0f)] private float startWidth = 0.035f;
        [SerializeField, Min(0f)] private float endWidth = 0.012f;
        [SerializeField] private int sortingOrder = 1;

        private readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();
        private bool rendererCacheInitialized;

        public IReadOnlyList<LineRenderer> LineRenderers => lineRenderers;

        public int PointCount => crackPoints?.Length ?? 0;

        private void Awake()
        {
            Rebuild();
        }

        private void Reset()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            startWidth = Mathf.Max(0f, startWidth);
            endWidth = Mathf.Max(0f, endWidth);
            Rebuild();
        }

        /// <summary>クラック全体を指定した点列へ置き換えます。</summary>
        public void SetPoints(Vector2[] points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            crackPoints = (Vector2[])points.Clone();
            SetCracks(new[] { crackPoints });
        }

        /// <summary>クラック先端へ新しい点を追加します。</summary>
        public void AppendPoint(Vector2 point)
        {
            int oldCount = crackPoints?.Length ?? 0;
            Array.Resize(ref crackPoints, oldCount + 1);
            crackPoints[oldCount] = point;
            SetCracks(new[] { crackPoints });
        }

        /// <summary>描画中のクラックを消去します。</summary>
        public void Clear()
        {
            crackPoints = Array.Empty<Vector2>();
            SetCracks(Array.Empty<Vector2[]>());
        }

        /// <summary>複数の独立したクラックを、それぞれ別の LineRenderer で描画します。</summary>
        public void SetCracks(IReadOnlyList<Vector2[]> cracks)
        {
            int requiredCount = cracks?.Count ?? 0;
            EnsureRendererCount(requiredCount);

            for (int i = 0; i < lineRenderers.Count; i++)
            {
                LineRenderer renderer = lineRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                bool active = i < requiredCount && cracks[i] != null && cracks[i].Length > 0;
                renderer.enabled = active;
                if (!active)
                {
                    renderer.positionCount = 0;
                    continue;
                }

                Vector2[] points = cracks[i];
                renderer.positionCount = points.Length;
                for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                {
                    renderer.SetPosition(pointIndex, points[pointIndex]);
                }
            }
        }

        [ContextMenu("Rebuild Crack")]
        public void Rebuild()
        {
            // Inspector変更や手動Rebuildでは、描画設定も全Rendererへ反映し直す。
            rendererCacheInitialized = false;
            if (crackPoints == null || crackPoints.Length == 0)
            {
                SetCracks(Array.Empty<Vector2[]>());
                return;
            }

            SetCracks(new[] { crackPoints });
        }

        private void EnsureRendererCount(int requiredCount)
        {
            requiredCount = Mathf.Max(1, requiredCount);

            if (!rendererCacheInitialized || HasMissingCachedRenderer())
            {
                RebuildRendererCache();
            }

            LineRenderer rootRenderer = lineRenderers.Count > 0
                ? lineRenderers[0]
                : null;
            while (lineRenderers.Count < requiredCount)
            {
                int rendererIndex = lineRenderers.Count;
                GameObject childObject = new GameObject($"CrackLine_{rendererIndex}");
                childObject.transform.SetParent(transform, false);
                LineRenderer created = childObject.AddComponent<LineRenderer>();
                if (created == null)
                {
                    Debug.LogError("Failed to create a child LineRenderer for crack rendering.", this);
                    if (Application.isPlaying)
                    {
                        Destroy(childObject);
                    }
                    else
                    {
                        DestroyImmediate(childObject);
                    }
                    break;
                }

                if (rootRenderer != null)
                {
                    created.sharedMaterial = rootRenderer.sharedMaterial;
                }
                ConfigureRenderer(created);
                lineRenderers.Add(created);
            }

            rendererCacheInitialized = true;
        }

        private void RebuildRendererCache()
        {
            // Renderer系は同一GameObjectへ複数追加できないため、先頭以外は子Objectで管理する。
            lineRenderers.Clear();
            LineRenderer rootRenderer = GetComponent<LineRenderer>();
            if (rootRenderer != null)
            {
                lineRenderers.Add(rootRenderer);
            }

            for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                Transform child = transform.GetChild(childIndex);
                if (!child.name.StartsWith("CrackLine_", StringComparison.Ordinal))
                {
                    continue;
                }

                LineRenderer childRenderer = child.GetComponent<LineRenderer>();
                if (childRenderer != null)
                {
                    lineRenderers.Add(childRenderer);
                }
            }

            for (int i = 0; i < lineRenderers.Count; i++)
            {
                ConfigureRenderer(lineRenderers[i]);
            }

            rendererCacheInitialized = true;
        }

        private bool HasMissingCachedRenderer()
        {
            for (int i = 0; i < lineRenderers.Count; i++)
            {
                if (lineRenderers[i] == null)
                {
                    return true;
                }
            }
            return lineRenderers.Count == 0;
        }

        private void OnTransformChildrenChanged()
        {
            rendererCacheInitialized = false;
        }

        private void ConfigureRenderer(LineRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.useWorldSpace = false;
            renderer.loop = false;
            renderer.startColor = startColor;
            renderer.endColor = endColor;
            renderer.startWidth = startWidth;
            renderer.endWidth = endWidth;
            renderer.numCornerVertices = 2;
            renderer.numCapVertices = 2;
            renderer.sortingOrder = sortingOrder;
        }
    }
}
