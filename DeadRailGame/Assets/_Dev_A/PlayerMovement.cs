using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    public float treadmillMultiplier = 1f; // 런닝머신 밀림 속도 미세 조정 비율 (올바른 위치)

    private CharacterController _controller;
    public float moveSpeed = 5f;
    public float rotationSpeed = 10f; // 캐릭터가 회전하는 속도

    [Header("애니메이션")]
    public Animator anim;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority == false) return;

        // 1. 키보드 입력에 따른 플레이어의 자체 이동 방향
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        Vector3 move = new Vector3(moveX, 0, moveZ);

        // 플레이어의 이동 속도를 담을 변수 (방향 * 속도)
        Vector3 playerVelocity = Vector3.zero;

        if (move.sqrMagnitude > 0)
        {
            move.Normalize();
            // 캐릭터 회전
            transform.forward = Vector3.Slerp(transform.forward, move, Runner.DeltaTime * rotationSpeed);
            // 플레이어 자체 이동 속도 적용
            playerVelocity = move * moveSpeed;
        }

        // 최종 속도를 계산할 변수
        Vector3 finalVelocity = playerVelocity;

        // 2. 트레드밀(기차) 속도 강제 적용
        if (TreadmillManager.Instance != null && TreadmillManager.Instance.isSpawned)
        {
            float treadmillSpeed = TreadmillManager.Instance.CurrentSpeed;
            // Z축 뒤쪽(Vector3.back)으로 매니저 속도에 배수를 곱하여 밀어내는 힘을 더해줍니다.
            finalVelocity += Vector3.back * (treadmillSpeed * treadmillMultiplier);
        }

        // 3. 최종 속도로 캐릭터 이동 (FixedUpdateNetwork에서는 Runner.DeltaTime 사용)
        _controller.Move(finalVelocity * Runner.DeltaTime);

        if (anim != null)
        {
            anim.SetFloat("Speed", move.sqrMagnitude);
        }
    }
}