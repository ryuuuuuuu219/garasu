using UnityEngine;
using UnityEngine.Rendering;

namespace PolygonRendering
{
    /// <summary>
    /// LineRenderer でローカル XY 平面上に正多角形を描画します。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RegularPolygonLineRenderer : MonoBehaviour
    {
        [SerializeField, Min(3)]
        [Tooltip("正多角形の頂点数です。3以上を指定します。")]
        private int vertexCount = 3;

        [SerializeField, Min(0f)]
        [Tooltip("中心から各頂点までの距離（外接円の半径）です。")]
        private float size = 1f;

        [SerializeField]
        [Tooltip("オンならプレイヤー側として上向き、オフなら反対側として下向きにします。")]
        private bool isPlayerSide = true;

        [SerializeField]
        [Tooltip("オンなら上下方向に頂点、オフなら上下方向に辺の中央を向けます。")]
        private bool pointVertexVertically = true;

        public Color color;

        private LineRenderer lineRenderer;

        public int VertexCount
        {
            get => vertexCount;
            set
            {
                vertexCount = Mathf.Max(3, value);
                Rebuild();
            }
        }

        public float Size
        {
            get => size;
            set
            {
                size = Mathf.Max(0f, value);
                Rebuild();
            }
        }

        public bool IsPlayerSide
        {
            get => isPlayerSide;
            set
            {
                isPlayerSide = value;
                Rebuild();
            }
        }

        public bool PointVertexVertically
        {
            get => pointVertexVertically;
            set
            {
                pointVertexVertically = value;
                Rebuild();
            }
        }

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            Rebuild();
        }

        private void Reset()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            vertexCount = Mathf.Max(3, vertexCount);
            size = Mathf.Max(0f, size);
            Rebuild();
        }

        [ContextMenu("Rebuild Polygon")]
        public void Rebuild()
        {
            if (!TryGetComponent(out lineRenderer))
            {
                return;
            }

            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = vertexCount;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;

            // 上向きは +Y、下向きは -Y。辺を向ける場合は半頂点分だけ回します。
            float verticalAngle = isPlayerSide ? 90f : -90f;
            float edgeOffset = pointVertexVertically ? 0f : 180f / vertexCount;
            float startAngle = verticalAngle + edgeOffset;
            float angleStep = 360f / vertexCount;

            for (int i = 0; i < vertexCount; i++)
            {
                float angleRadians = (startAngle + angleStep * i) * Mathf.Deg2Rad;
                Vector3 position = new Vector3(
                    Mathf.Cos(angleRadians) * size,
                    Mathf.Sin(angleRadians) * size,
                    0f);

                lineRenderer.SetPosition(i, position);
            }
        }
    }
}
