using System;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>単一の仮想点弱点と、敵の撃破状態を保持します。</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyDefeatComponent : MonoBehaviour
    {
        private const int WeakPointSegments = 8;
        private const float WeakPointRadius = 0.06f;
        private const float WeakPointLineWidth = 0.005f;
        private static readonly Color WeakPointColor = new Color(1f, 0f, 0f, 0.4f);

        [SerializeField] private bool hasWeakPoint;
        [SerializeField] private Vector2 weakPointLocalPosition;
        [SerializeField] private bool isDefeated;

        private LineRenderer weakPointRenderer;
        private Material weakPointMaterial;

        public bool HasWeakPoint => hasWeakPoint;
        public Vector2 WeakPointLocalPosition => weakPointLocalPosition;
        public bool IsDefeated => isDefeated;

        public event Action Defeated;

        public void InitializeWeakPoint(Vector2 localPosition)
        {
            if (hasWeakPoint)
            {
                EnsureWeakPointRenderer();
                return;
            }

            hasWeakPoint = true;
            weakPointLocalPosition = localPosition;
            EnsureWeakPointRenderer();
        }

        public bool IsWeakPoint(Vector2 localPosition, float tolerance)
        {
            return hasWeakPoint &&
                (weakPointLocalPosition - localPosition).sqrMagnitude <= tolerance * tolerance;
        }

        public bool MarkDefeated()
        {
            if (isDefeated)
            {
                return false;
            }

            isDefeated = true;
            if (weakPointRenderer != null)
            {
                weakPointRenderer.enabled = false;
            }

            // 現在のボスは回復対象が同じBossGlassComponentに集約されているため、
            // コアまたは装甲の弱点破壊時点で以後の回復を停止する。
            BossGlassComponent boss = GetComponentInParent<BossGlassComponent>();
            if (boss != null)
            {
                boss.enabled = false;
            }

            Defeated?.Invoke();
            return true;
        }

        private void Awake()
        {
            if (hasWeakPoint)
            {
                EnsureWeakPointRenderer();
            }
        }

        private void EnsureWeakPointRenderer()
        {
            if (weakPointRenderer == null)
            {
                Transform existing = transform.Find("WeakPointIndicator");
                if (existing != null)
                {
                    weakPointRenderer = existing.GetComponent<LineRenderer>();
                }
            }

            if (weakPointRenderer == null)
            {
                GameObject indicator = new GameObject("WeakPointIndicator");
                indicator.transform.SetParent(transform, false);
                weakPointRenderer = indicator.AddComponent<LineRenderer>();
            }

            Transform indicatorTransform = weakPointRenderer.transform;
            indicatorTransform.localPosition = weakPointLocalPosition;
            indicatorTransform.localRotation = Quaternion.identity;
            indicatorTransform.localScale = Vector3.one;

            weakPointRenderer.useWorldSpace = false;
            weakPointRenderer.loop = true;
            weakPointRenderer.positionCount = WeakPointSegments;
            weakPointRenderer.startWidth = WeakPointLineWidth;
            weakPointRenderer.endWidth = WeakPointLineWidth;
            weakPointRenderer.startColor = WeakPointColor;
            weakPointRenderer.endColor = WeakPointColor;
            weakPointRenderer.sortingOrder = 600;

            if (weakPointRenderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    weakPointMaterial = new Material(shader);
                    weakPointRenderer.sharedMaterial = weakPointMaterial;
                }
            }

            for (int i = 0; i < WeakPointSegments; i++)
            {
                float angle = Mathf.PI * 2f * i / WeakPointSegments;
                weakPointRenderer.SetPosition(
                    i,
                    new Vector3(
                        Mathf.Cos(angle) * WeakPointRadius,
                        Mathf.Sin(angle) * WeakPointRadius,
                        -0.05f));
            }
        }

        private void OnDestroy()
        {
            if (weakPointMaterial != null)
            {
                Destroy(weakPointMaterial);
                weakPointMaterial = null;
            }
        }
    }
}
