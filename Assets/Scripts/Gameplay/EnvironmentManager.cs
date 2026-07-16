using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>ゲーム全体の表示倍率と2D重力を管理します。</summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class EnvironmentManager : MonoBehaviour
    {
        [Header("Global Scale")]
        [SerializeField, Min(0.0001f)] private float globalScaleMultiplier = 1f;
        [SerializeField] private Transform scaleRoot = null;

        [Header("Global Gravity")]
        [SerializeField] private Vector2 baseGravity = new Vector2(0f, -9.81f);
        [SerializeField, Min(0f)] private float gravityMultiplier = 0.1f;

        private Vector3 scaleRootBaseScale = Vector3.one;
        private Transform cachedScaleRoot;

        public static EnvironmentManager Instance { get; private set; }
        public float GlobalScaleMultiplier => globalScaleMultiplier;
        public float GravityMultiplier => gravityMultiplier;
        public Vector2 EffectiveGravity => baseGravity * gravityMultiplier;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstanceExists()
        {
            if (FindAnyObjectByType<EnvironmentManager>() == null)
            {
                new GameObject("EnvironmentManager").AddComponent<EnvironmentManager>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // EnvironmentManagerがBulletStatusやGlassStatusと同居していても、
                // 重複時に全般マネージャー本体や他コンポーネントを巻き込まない。
                Destroy(this);
                return;
            }

            Instance = this;
            CacheScaleRoot();
            ApplyEnvironment();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            globalScaleMultiplier = Mathf.Max(0.0001f, globalScaleMultiplier);
            gravityMultiplier = Mathf.Max(0f, gravityMultiplier);
            if (Application.isPlaying)
            {
                if (cachedScaleRoot != scaleRoot)
                {
                    CacheScaleRoot();
                }
                ApplyEnvironment();
            }
        }

        public void SetGlobalScaleMultiplier(float value)
        {
            globalScaleMultiplier = Mathf.Max(0.0001f, value);
            ApplyEnvironment();
        }

        public void SetGravityMultiplier(float value)
        {
            gravityMultiplier = Mathf.Max(0f, value);
            ApplyEnvironment();
        }

        public void SetScaleRoot(Transform root)
        {
            scaleRoot = root;
            CacheScaleRoot();
            ApplyEnvironment();
        }

        public void ApplyEnvironment()
        {
            Physics2D.gravity = EffectiveGravity;
            if (scaleRoot != null)
            {
                if (cachedScaleRoot != scaleRoot)
                {
                    CacheScaleRoot();
                }
                scaleRoot.localScale = scaleRootBaseScale * globalScaleMultiplier;
            }
        }

        private void CacheScaleRoot()
        {
            cachedScaleRoot = scaleRoot;
            scaleRootBaseScale = scaleRoot != null ? scaleRoot.localScale : Vector3.one;
        }
    }
}
