using System;
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

        private LineRenderer lineRenderer;

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
            Rebuild();
        }

        /// <summary>クラック先端へ新しい点を追加します。</summary>
        public void AppendPoint(Vector2 point)
        {
            int oldCount = crackPoints?.Length ?? 0;
            Array.Resize(ref crackPoints, oldCount + 1);
            crackPoints[oldCount] = point;
            Rebuild();
        }

        /// <summary>描画中のクラックを消去します。</summary>
        public void Clear()
        {
            crackPoints = Array.Empty<Vector2>();
            Rebuild();
        }

        [ContextMenu("Rebuild Crack")]
        public void Rebuild()
        {
            if (!TryGetComponent(out lineRenderer))
            {
                return;
            }

            int count = crackPoints?.Length ?? 0;
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = false;
            lineRenderer.positionCount = count;
            lineRenderer.startColor = startColor;
            lineRenderer.endColor = endColor;
            lineRenderer.startWidth = startWidth;
            lineRenderer.endWidth = endWidth;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.numCapVertices = 2;
            lineRenderer.sortingOrder = sortingOrder;

            for (int i = 0; i < count; i++)
            {
                Vector2 point = crackPoints[i];
                lineRenderer.SetPosition(i, new Vector3(point.x, point.y, 0f));
            }
        }
    }
}
