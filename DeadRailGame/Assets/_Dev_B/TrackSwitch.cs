using UnityEngine;
using Fusion;

public class TrackSwitch : NetworkBehaviour
{
    public enum TrackDirection { Left_SafeZone = 0, Right_HighRiskZone = 1 }

    [Header("현재 선택된 선로 방향 (네트워크 동기화)")]
    [Networked] public TrackDirection CurrentDirection { get; set; } = TrackDirection.Left_SafeZone;

    [Header("시각 연출용 레버 & 표시등 (없으면 비워둬도 됨)")]
    public Transform leverPivot;
    public Light statusLight;
    public Renderer lampRenderer;

    [Header("레버 회전 각도 설정")]
    public Vector3 leftAngle = new Vector3(0f, 0f, -35f);
    public Vector3 rightAngle = new Vector3(0f, 0f, 35f);
    public float rotateSpeed = 8f;

    // RaycastWeapon.cs 및 HarpoonFarming.cs의 SendMessageUpwards("OnSwitchHit")에서 자동 호출됨!
    public void OnSwitchHit()
    {
        Rpc_RequestToggleSwitch();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_RequestToggleSwitch()
    {
        // 방장(StateAuthority) 권한으로 선로 방향 토글
        CurrentDirection = (CurrentDirection == TrackDirection.Left_SafeZone)
            ? TrackDirection.Right_HighRiskZone
            : TrackDirection.Left_SafeZone;

        Debug.Log($"🔀 [TrackSwitch] 선로 변환 완료! 현재 방향: {CurrentDirection}");
    }

    public override void Render()
    {
        // 1. 레버 피벗이 연결되어 있다면 부드럽게 회전 연출
        if (leverPivot != null)
        {
            Quaternion targetRot = Quaternion.Euler(
                CurrentDirection == TrackDirection.Left_SafeZone ? leftAngle : rightAngle
            );
            leverPivot.localRotation = Quaternion.Slerp(leverPivot.localRotation, targetRot, Time.deltaTime * rotateSpeed);
        }

        // 2. 방향에 따른 표시등 색상 변경 (안전 구역 = 초록색 / 고위험·고보상 구역 = 주황·빨간색)
        Color dirColor = (CurrentDirection == TrackDirection.Left_SafeZone) ? Color.green : new Color(1f, 0.3f, 0f);

        if (statusLight != null) statusLight.color = dirColor;
        if (lampRenderer != null && lampRenderer.material != null)
        {
            lampRenderer.material.color = dirColor;
        }
    }
}