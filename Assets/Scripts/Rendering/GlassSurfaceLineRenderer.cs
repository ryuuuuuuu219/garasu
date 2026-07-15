using System;
using UnityEngine;

namespace PolygonRendering
{
    /// <summary>
    /// ガラス表面の外周を、ローカル XY 平面上の閉じたラインとして描画します。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class GlassSurfaceLineRenderer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("ガラス外周の頂点です。ローカル XY 座標で3点以上を指定します。")]
        private Vector2[] outlinePoints =
        {
            new Vector2(-4f, -1.5f),
            new Vector2(-4f, 1.5f),
            new Vector2(4f, 1.5f),
            new Vector2(4f, -1.5f)
        };

        [SerializeField] private Color color = new Color(0.2f, 0.85f, 1f, 0.8f);
        [SerializeField, Min(0f)] private float lineWidth = 0.05f;
        [SerializeField] private int sortingOrder = 0;

        private LineRenderer lineRenderer;

        public int PointCount => outlinePoints?.Length ?? 0;
        public Vector2[] OutlinePoints => outlinePoints == null
            ? Array.Empty<Vector2>()
            : (Vector2[])outlinePoints.Clone();

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
            lineWidth = Mathf.Max(0f, lineWidth);
            Rebuild();
        }

        /// <summary>任意形状のガラス外周へ置き換えます。</summary>
        public void SetOutline(Vector2[] points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Length < 3)
            {
                throw new ArgumentException("Glass outline requires at least three points.", nameof(points));
            }

            outlinePoints = (Vector2[])points.Clone();
            Rebuild();
        }

        /// <summary>中心が原点の長方形へ設定します。</summary>
        public void SetRectangle(Vector2 size)
        {
            Vector2 halfSize = new Vector2(
                Mathf.Max(0f, size.x) * 0.5f,
                Mathf.Max(0f, size.y) * 0.5f);

            outlinePoints = new[]
            {
                new Vector2(-halfSize.x, -halfSize.y),
                new Vector2(-halfSize.x, halfSize.y),
                new Vector2(halfSize.x, halfSize.y),
                new Vector2(halfSize.x, -halfSize.y)
            };

            Rebuild();
        }

        [ContextMenu("Rebuild Glass Surface")]
        public void Rebuild()
        {
            if (!TryGetComponent(out lineRenderer))
            {
                return;
            }

            int count = outlinePoints?.Length ?? 0;
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = count >= 3;
            lineRenderer.positionCount = count;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.numCapVertices = 2;
            lineRenderer.sortingOrder = sortingOrder;

            for (int i = 0; i < count; i++)
            {
                Vector2 point = outlinePoints[i];
                lineRenderer.SetPosition(i, new Vector3(point.x, point.y, 0f));
            }
        }
    }
}
