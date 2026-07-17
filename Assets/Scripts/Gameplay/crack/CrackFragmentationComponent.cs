using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>完成したクラックによる領域分割と破片生成を担当する機能コンポーネントです。</summary>
    [DisallowMultipleComponent]
    public sealed class CrackFragmentationComponent : MonoBehaviour
    {
        [SerializeField] private CrackProcessingComponent processing = null;

        internal void Bind(CrackProcessingComponent owner)
        {
            processing = owner;
        }

        internal bool TrySeparateAlongCrack(Vector2[] crack)
        {
            return Processing.TrySeparateAlongCrackCore(crack);
        }

        internal bool TryExtendCrackToBoundary(int crackIndex, Vector2 newCrackPosition)
        {
            return Processing.TryExtendCrackToBoundaryCore(crackIndex, newCrackPosition);
        }

        private CrackProcessingComponent Processing
        {
            get
            {
                if (processing == null)
                {
                    processing = GetComponent<CrackProcessingComponent>();
                }

                return processing;
            }
        }
    }
}
