using UnityEngine;

public class Hammer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D pendulum;

    [Header("Hammer Movement")]
    [SerializeField] private float moveForce = 5f;

    [Header("Pendulum")]
    [SerializeField] private float radius = 2f;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        ConstrainPendulum();

        bool isClockwise = GetIsClockwise();

        // 確認用
        // Debug.Log(isClockwise ? "Clockwise" : "Counterclockwise");
    }


    [SerializeField, Min(0f)]
    private float pendulumDrag = 0.5f;

    private void ConstrainPendulum()
    {
        float deltaTime = Time.fixedDeltaTime;

        Vector2 hammerPosition = rb.position;
        Vector2 hammerVelocity = rb.linearVelocity;

        Vector2 radialDirection =
            (pendulum.position - hammerPosition).normalized;

        if (radialDirection.sqrMagnitude < 0.000001f)
        {
            radialDirection = Vector2.down;
        }

        // ハンマーに対する振り子の相対速度
        Vector3 relativeVelocity =
            pendulum.linearVelocity - hammerVelocity;

        // 半径方向成分を除き、接線方向の速度だけ残す
        Vector2 tangentialVelocity =
            Vector3.ProjectOnPlane(relativeVelocity, radialDirection);

        // 角速度に比例する抵抗
        float damping =
            Mathf.Exp(-pendulumDrag * deltaTime);

        tangentialVelocity *= damping;

        // 次の位置を予測
        Vector2 predictedPosition =
            pendulum.position
            + (hammerVelocity + tangentialVelocity) * deltaTime;

        // 半径radiusの円周上へ戻す
        Vector2 predictedDirection =
            predictedPosition - hammerPosition;

        if (predictedDirection.sqrMagnitude < 0.000001f)
        {
            predictedDirection = radialDirection;
        }

        Vector2 constrainedPosition =
            hammerPosition
            + predictedDirection.normalized * radius;

        // 拘束後の半径方向
        Vector2 constrainedRadial =
            (constrainedPosition - hammerPosition).normalized;

        // 接線速度を新しい半径方向に沿わせ直す
        tangentialVelocity =
            Vector3.ProjectOnPlane(
                tangentialVelocity,
                constrainedRadial
            );

        pendulum.linearVelocity =
            hammerVelocity + tangentialVelocity;

        pendulum.MovePosition(constrainedPosition);
    }

    private bool GetIsClockwise()
    {
        Vector2 radialDirection =
            pendulum.position - rb.position;

        Vector2 velocity = pendulum.linearVelocity;

        // XY平面上の外積Z成分
        float crossZ =
            radialDirection.x * velocity.y
            - radialDirection.y * velocity.x;

        // UnityのXY平面を正面から見た場合、
        // crossZ < 0 が時計回り
        return crossZ < 0f;
    }
}