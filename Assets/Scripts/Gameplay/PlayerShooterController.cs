using PolygonRendering.Input;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// プレイヤーの移動と統合破砕弾の発射を担当します。
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
        [SerializeField] private BulletStatus bulletStatus;

        [SerializeField] private Vector2 Movelimitmin;
        [SerializeField] private Vector2 Movelimitmax;

        private KeyboardInputState inputState;
        private float nextFireTime;
        private LineRenderer lr;

        public Vector2 MoveLimitMin => Movelimitmin;
        public Vector2 MoveLimitMax => Movelimitmax;
        public BulletStatus BulletStatus => bulletStatus;
        public Projectile ProjectilePrefab => projectilePrefab;
        public float MoveSpeed => moveSpeed;
        public float FireInterval => fireInterval;

        private void Awake()
        {
            inputState = GetComponent<KeyboardInputState>();

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
            RenderMovementLimit();

            if (inputState.SpaceDown && Time.time >= nextFireTime)
            {
                Fire();
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

        private void RenderMovementLimit()
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
            if (projectilePrefab == null || bulletStatus == null)
            {
                return;
            }

            Vector3 spawnPosition = firePoint != null
                ? firePoint.position
                : transform.position;
            Projectile projectile = Instantiate(
                projectilePrefab,
                spawnPosition,
                Quaternion.identity);
            BulletStatus copy = projectile.TryGetComponent(out BulletStatus existingStatus)
                ? existingStatus
                : projectile.gameObject.AddComponent<BulletStatus>();
            copy.CopyFrom(bulletStatus);

            nextFireTime = Time.time + fireInterval;
        }
    }
}
