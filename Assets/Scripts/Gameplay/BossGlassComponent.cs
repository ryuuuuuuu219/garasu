using System;
using System.Collections.Generic;
using Gameplay;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// ボスガラス本体を識別するマーカーです。
    /// ボス固有の蓄積破砕エネルギーや回復状態も今後ここで管理します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyDefeatComponent))]
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
        [SerializeField, Min(0.01f)] private float inviolableAreaRadius = 6f;

        public IReadOnlyList<ModuleDetail> Modules => modules;

        private void Awake()
        {
            if (TryGetComponent(out EnemyDefeatComponent coreDefeat))
            {
                coreDefeat.Defeated += ReleaseBossDebris;
            }
        }

        private void Start()
        {
            PlayerOnlyCircularCollider inviolableArea =
                TryGetComponent(out PlayerOnlyCircularCollider existingArea)
                    ? existingArea
                    : gameObject.AddComponent<PlayerOnlyCircularCollider>();
            inviolableArea.SetRadius(inviolableAreaRadius);

            BossAppearanceManager appearanceManager =
                GetComponentInParent<BossAppearanceManager>();
            if (appearanceManager == null)
            {
                appearanceManager = FindAnyObjectByType<BossAppearanceManager>();
            }

            if (appearanceManager == null)
            {
                Debug.LogWarning(
                    "BossAppearanceManagerが見つからないため、ゲームオーバー判定対象へ登録できません。",
                    this);
                return;
            }

            appearanceManager.RegisterActiveTarget(gameObject);
        }

        private void OnDestroy()
        {
            if (TryGetComponent(out EnemyDefeatComponent coreDefeat))
            {
                coreDefeat.Defeated -= ReleaseBossDebris;
            }
        }

        public void AddModule(GameObject module, float healingInterval)
        {
            if (module == null)
            {
                Debug.LogWarning("Cannot register a null boss module.", this);
                return;
            }

            modules.Add(new ModuleDetail(module, healingInterval));
        }

        /// <summary>
        /// 破砕で消滅するモジュールを、そこから生成された破片へ置き換えます。
        /// 回復間隔と次の回復までの残り時間は元モジュールから引き継ぎます。
        /// </summary>
        public bool ReplaceModule(GameObject originalModule, IReadOnlyList<GameObject> fragments)
        {
            if (originalModule == null || fragments == null)
            {
                return false;
            }

            int originalIndex = modules.FindIndex(detail => detail.Module == originalModule);
            if (originalIndex < 0)
            {
                return false;
            }

            ModuleDetail inheritedDetail = modules[originalIndex];
            modules.RemoveAt(originalIndex);

            int insertionIndex = originalIndex;
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                GameObject fragment = fragments[fragmentIndex];
                if (fragment == null)
                {
                    continue;
                }

                ModuleDetail fragmentDetail = inheritedDetail;
                fragmentDetail.Module = fragment;
                modules.Insert(insertionIndex, fragmentDetail);
                insertionIndex++;
            }

            return true;
        }

        private void ReleaseBossDebris()
        {
            Transform debrisParent = transform.parent;
            MarkAsDebris(gameObject);

            for (int moduleIndex = modules.Count - 1; moduleIndex >= 0; moduleIndex--)
            {
                GameObject module = modules[moduleIndex].Module;
                if (module == null)
                {
                    modules.RemoveAt(moduleIndex);
                    continue;
                }

                if (module != gameObject)
                {
                    module.transform.SetParent(debrisParent, true);
                }

                if (module.TryGetComponent(out CrackProcessingComponent crackProcessing))
                {
                    crackProcessing.ReleaseFromAnchor();
                }
                else
                {
                    Rigidbody2D body = module.TryGetComponent(out Rigidbody2D existingBody)
                        ? existingBody
                        : module.AddComponent<Rigidbody2D>();
                    body.bodyType = RigidbodyType2D.Dynamic;
                    body.constraints = RigidbodyConstraints2D.None;
                    body.gravityScale = 1f;
                }

                MarkAsDebris(module);
            }
        }

        private static void MarkAsDebris(GameObject target)
        {
            if (!target.TryGetComponent(out GlassFragment _))
            {
                target.AddComponent<GlassFragment>();
            }
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
