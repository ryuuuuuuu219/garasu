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
        public struct ModuleDetail
        {
            public GameObject module;
            public float healingRate;
            public float healingtimer;
        }

        public ModuleDetail[] modules;

        private void Awake()
        {
            switch (gameObject.name)
            {
                case "Boss_A":
                    // noop: Boss_A固有処理は今後ここへ追加する。
                    CrackProcessingComponent crackProcessing = modules[0].module.GetComponent<CrackProcessingComponent>();
                    modules[0].healingtimer -= Time.deltaTime;
                    if (modules[0].healingtimer <= 0f)
                    {
                        crackProcessing.HealCracks();
                        modules[0].healingtimer += modules[0].healingRate; // Reset the healing timer
                    }

                    break;
            }
        }
    }
}
