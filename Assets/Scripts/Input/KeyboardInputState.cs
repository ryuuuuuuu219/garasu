using UnityEngine;

namespace PolygonRendering.Input
{
    /// <summary>
    /// 矢印キーと左 Shift キーの入力状態を、他コンポーネントから参照できる形で保持します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardInputState : MonoBehaviour
    {
        [Header("Current Input (Read Only)")]
        [SerializeField] private Vector2 arrowDirection;
        [SerializeField] private bool upArrowHeld;
        [SerializeField] private bool downArrowHeld;
        [SerializeField] private bool leftArrowHeld;
        [SerializeField] private bool rightArrowHeld;
        [SerializeField] private bool leftShiftDown;
        [SerializeField] private bool leftShiftActive;

        /// <summary>矢印キーの入力方向です。各軸は -1、0、1 になります。</summary>
        public Vector2 ArrowDirection => arrowDirection;

        public bool UpArrowHeld => upArrowHeld;
        public bool DownArrowHeld => downArrowHeld;
        public bool LeftArrowHeld => leftArrowHeld;
        public bool RightArrowHeld => rightArrowHeld;

        /// <summary>左 Shift を押したフレームだけ true になります。</summary>
        public bool LeftShiftDown => leftShiftDown;

        /// <summary>左 Shift を押している間 true。ほかのコンポーネント反映用のフラグです。</summary>
        public bool LeftShiftActive => leftShiftActive;

        public bool SpaceDown => UnityEngine.Input.GetKeyDown(KeyCode.Space);

        private void Update()
        {
            upArrowHeld = UnityEngine.Input.GetKey(KeyCode.UpArrow);
            downArrowHeld = UnityEngine.Input.GetKey(KeyCode.DownArrow);
            leftArrowHeld = UnityEngine.Input.GetKey(KeyCode.LeftArrow);
            rightArrowHeld = UnityEngine.Input.GetKey(KeyCode.RightArrow);

            float horizontal = (rightArrowHeld ? 1f : 0f) - (leftArrowHeld ? 1f : 0f);
            float vertical = (upArrowHeld ? 1f : 0f) - (downArrowHeld ? 1f : 0f);
            arrowDirection = new Vector2(horizontal, vertical);

            leftShiftDown = UnityEngine.Input.GetKeyDown(KeyCode.LeftShift);
            leftShiftActive = UnityEngine.Input.GetKey(KeyCode.LeftShift);
        }
    }
}
