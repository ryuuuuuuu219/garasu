using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// 画面上方へ直進し、表示範囲外で破棄されるプロトタイプ用の弾です。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float speed = 12f;
        [SerializeField] private float destroyY = 6.5f;

        private void Update()
        {
            transform.position += Vector3.up * (speed * Time.deltaTime);

            if (transform.position.y >= destroyY)
            {
                Destroy(gameObject);
            }
        }
    }
}
