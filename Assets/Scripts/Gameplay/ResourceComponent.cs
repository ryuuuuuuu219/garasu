using System;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>シーンをまたいで保持するプレイヤー資源です。</summary>
    [DisallowMultipleComponent]
    public sealed class ResourceComponent : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float resource = 100f;

        private static ResourceComponent instance;

        public static ResourceComponent Instance => EnsureInstance();
        public float Resource => resource;
        public event Action<float> ResourceChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateBeforeFirstScene()
        {
            EnsureInstance();
        }

        public void Add(float amount)
        {
            if (amount <= 0)
            {
                return;
            }

            resource += amount;
            ResourceChanged?.Invoke(resource);
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || resource < amount)
            {
                return false;
            }

            resource -= amount;
            ResourceChanged?.Invoke(resource);
            return true;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            if (!TryGetComponent(out GrowthStatusComponent _))
            {
                gameObject.AddComponent<GrowthStatusComponent>();
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private static ResourceComponent EnsureInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindAnyObjectByType<ResourceComponent>();
            if (instance != null)
            {
                return instance;
            }

            var root = new GameObject("PersistentGrowthData");
            return root.AddComponent<ResourceComponent>();
        }
    }
}
