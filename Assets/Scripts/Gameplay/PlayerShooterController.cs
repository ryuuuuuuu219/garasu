using PolygonRendering;
using PolygonRendering.Input;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// プレイヤーの左右移動と弾の発射を担当します。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeyboardInputState))]
    public sealed class PlayerShooterController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 7f;

        [Header("Shooting")]
        [SerializeField] private Projectile projectilePrefab = null;
        [SerializeField] private Transform firePoint = null;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.16f;

        private KeyboardInputState inputState;
        private float nextFireTime;

        [SerializeField] private BulletStatus bulletStatus;
        BulletType bulletType => bulletStatus.Type;

        private LineRenderer lr;

        [SerializeField] Vector2 Movelimitmin, Movelimitmax;

        private void Awake()
        {
            inputState = GetComponent<KeyboardInputState>();

            // LineRendererコンポーネント用の GameObjectを作成して子オブジェクトとして追加
            GameObject child = new GameObject("LineRenderer");
            child.transform.SetParent(transform);
            child.transform.localPosition = Vector3.zero;
            lr = child.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
        }

        private void Update()
        {
            Move();
            RenderingLimit();

            if (inputState.SpaceDown
                && Time.time >= nextFireTime)
            {
                Fire();
            }
            if (inputState.LeftShiftDown)
            {
                ModeChange();
            }

        }

        private void Move()
        {
            Vector3 position = transform.position;
            position += (Vector3)inputState.ArrowDirection * moveSpeed * Time.deltaTime;
            position.x = Mathf.Clamp(position.x, Movelimitmin.x, Movelimitmax.x);
            position.y = Mathf.Clamp(position.y, Movelimitmin.y, Movelimitmax.y);
            transform.position = position;
        }

        private void ModeChange()
        {
            if (bulletStatus != null)
            {
                bulletStatus.ModeChange();
            }
        }

        void RenderingLimit()
        {
            lr.positionCount = 4;
            lr.SetPosition(0, new Vector3(Movelimitmin.x, Movelimitmin.y, 0f));
            lr.SetPosition(1, new Vector3(Movelimitmax.x, Movelimitmin.y, 0f));
            lr.SetPosition(2, new Vector3(Movelimitmax.x, Movelimitmax.y, 0f));
            lr.SetPosition(3, new Vector3(Movelimitmin.x, Movelimitmax.y, 0f));
            lr.loop = true;
            lr.startColor = Color.white;
            lr.endColor = Color.white;
            lr.useWorldSpace = true;

        }

        private void Fire()
        {
            if (projectilePrefab == null)
            {
                return;
            }

            Vector3 spawnPosition = firePoint != null ? firePoint.position : transform.position;

            var projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
            int projectileVertex = bulletType switch
            {
                BulletType.CrackGenerator => 3,
                BulletType.CrackOpener => 4,
                _ => 3
            };
            bool pointVertexVertically = bulletType switch
            {
                BulletType.CrackGenerator => true,
                BulletType.CrackOpener => false,
                _ => true
            };
            projectile.TryGetComponent(out RegularPolygonLineRenderer polygonRenderer);
            polygonRenderer.VertexCount = projectileVertex;
            polygonRenderer.PointVertexVertically = pointVertexVertically;

            if(bulletStatus == null)
            {
                return;
            }

            BulletStatus original = bulletStatus;
            BulletStatus copy = projectile.TryGetComponent(out BulletStatus script) ? script : projectile.AddComponent<BulletStatus>();

            copy.statusCopy(original);

            nextFireTime = Time.time + fireInterval;
        }
    }
}
