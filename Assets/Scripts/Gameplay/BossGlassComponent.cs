using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// ボスガラス本体を識別するマーカーです。
    /// ボス固有の蓄積破砕エネルギーや回復状態も今後ここで管理します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossGlassComponent : MonoBehaviour
    {
        [Serializable]
        public struct ModuleDetail
        {
            public GameObject Module;
            [Min(0.01f)] public float HealingInterval;
            [HideInInspector] public float RemainingHealingTime;

            public ModuleDetail(GameObject module, float healingInterval)
            {
                Module = module;
                HealingInterval = Mathf.Max(0.01f, healingInterval);
                RemainingHealingTime = HealingInterval;
            }
        }

        [SerializeField] private List<ModuleDetail> modules = new List<ModuleDetail>();

        public IReadOnlyList<ModuleDetail> Modules => modules;

        public void AddModule(GameObject module, float healingInterval)
        {
            if (module == null)
            {
                Debug.LogWarning("Cannot register a null boss module.", this);
                return;
            }

            modules.Add(new ModuleDetail(module, healingInterval));
        }

        private void Update()
        {
            for (int moduleIndex = modules.Count - 1; moduleIndex >= 0; moduleIndex--)
            {
                ModuleDetail detail = modules[moduleIndex];
                if (detail.Module == null)
                {
                    modules.RemoveAt(moduleIndex);
                    continue;
                }

                detail.RemainingHealingTime -= Time.deltaTime;
                if (detail.RemainingHealingTime > 0f)
                {
                    modules[moduleIndex] = detail;
                    continue;
                }

                if (detail.Module.TryGetComponent(out CrackProcessingComponent crackProcessing))
                {
                    crackProcessing.HealCracks();
                }

                detail.HealingInterval = Mathf.Max(0.01f, detail.HealingInterval);
                detail.RemainingHealingTime += detail.HealingInterval;
                modules[moduleIndex] = detail;
            }
        }
    }
}
