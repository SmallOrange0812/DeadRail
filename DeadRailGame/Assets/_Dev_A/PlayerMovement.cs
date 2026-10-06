using Fusion;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.Collections.Unicode;

public class PlayerMovement : NetworkBehaviour
{
    private CharacterController _controller;
    public float moveSpeed = 5f;
    public float rotationSpeed = 10f; // 새로 추가됨: 캐릭터가 회전하는 속도

    [Header("애니메이션")]
    public Animator anim;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority == false) return;

        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        Vector3 move = new Vector3(moveX, 0, moveZ);

        if (move.sqrMagnitude > 0)
        {
            move.Normalize();

            // 수정된 부분: 즉시 꺾지 않고, 목표 방향(move)을 향해 부드럽게 회전합니다.
            transform.forward = Vector3.Slerp(transform.forward, move, Runner.DeltaTime * rotationSpeed);

            _controller.Move(move * moveSpeed * Runner.DeltaTime);
        }

        if (anim != null)
        {
            anim.SetFloat("Speed", move.sqrMagnitude);
        }
    }
}