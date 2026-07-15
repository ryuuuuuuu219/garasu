using PolygonRendering;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// ステータス設定用のコンポーネント　生成時に反映させる
    /// </summary>
    [DisallowMultipleComponent]
    public class GlassStatus : MonoBehaviour
    {
        public int InitCrackPoint;
        public Vector2[] Crackpositions(float width, float height)
        {
            Vector2[] positions = new Vector2[InitCrackPoint];
            for (int i = 0; i < InitCrackPoint; i++)
            {
                float x = Random.Range(-width / 2f, width / 2f);
                float y = Random.Range(-height / 2f, height / 2f);
                positions[i] = new Vector2(x, y);
            }
            return positions;
        }

    }
}
