using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Fusion;

public class JunctionBranchDirector : NetworkBehaviour
{
    [Header("분기점 연결 참조")]
    [Tooltip("전방 선로 변환기 (비워두면 씬에서 자동으로 찾습니다)")]
    public TrackSwitch targetSwitch;

    [Header("분기점 돌파 조건 설정 (기획안 반영)")]
    [Tooltip("고위험·고보상 구역 선로를 돌파하기 위해 필요한 최소 기차 속도 (예: 80km/h)")]
    public float requiredHighRiskSpeed = 80f;
    [Tooltip("현재 기차 속도 (TreadmillManager 연동 전 테스트용 수동 입력값)")]
    public float testCurrentTrainSpeed = 85f;

    [Header("카메라 틸트(기울기) & 덜컹거림 연출 설정")]
    [Tooltip("곡선 선로 진입 시 카메라가 기울어지는 최대 각도 (도)")]
    public float maxTiltAngle = 12f;
    [Tooltip("선로 변경 시 덜컹거리는 카메라 진동 세기")]
    public float shakeIntensity = 0.08f;
    [Tooltip("분기점 통과 및 터널 전환 전체 소요 시간 (초)")]
    public float transitionDuration = 3.5f;

    [Header("터널 진입 암전(Tunnel Darkness) 연출")]
    [Tooltip("체크하면 터널 통과 시 화면 가장자리와 전방이 어두워졌다가 밝아집니다")]
    public bool enableTunnelDarkness = true;
    [Range(0f, 1f)] public float maxTunnelDarknessAlpha = 0.85f;

    private float _currentTunnelAlpha = 0f;
    private bool _isTransitioning = false;

    public override void Spawned()
    {
        if (targetSwitch == null)
        {
            targetSwitch = FindFirstObjectByType<TrackSwitch>();
        }
    }

    private void Update()
    {
        // 개발 테스트용: 방장(Host)이 키보드 J키를 누르면 즉시 분기점 진입 판정 및 연출 실행!
        if (HasStateAuthority && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && !_isTransitioning)
        {
            EvaluateAndTriggerJunction();
        }
    }

    // 💡 기차가 분기점 트리거에 닿았을 때 또는 J키를 눌렀을 때 호출되는 핵심 판정 함수
    [ContextMenu("🚄 분기점 진입 판정 & 연출 즉시 테스트")]
    public void EvaluateAndTriggerJunction()
    {
        if (!HasStateAuthority) return;

        // 1. 기차 현재 속도 가져오기 (개발자 A의 TreadmillManager가 있다면 속도값 참조)
        float currentSpeed = GetCurrentTrainSpeed();

        // 2. 선로 변환기(TrackSwitch) 상태 확인
        TrackSwitch.TrackDirection switchDir = (targetSwitch != null)
            ? targetSwitch.CurrentDirection
            : TrackSwitch.TrackDirection.Right_HighRiskZone;

        bool enterHighRiskZone = false;
        bool isLeftTurn = true;

        // 3. [핵심 기획 판정] 고위험 구역 선로가 선택되었는가? + 요구 속도(관성) 이상인가?
        if (switchDir == TrackSwitch.TrackDirection.Right_HighRiskZone)
        {
            if (currentSpeed >= requiredHighRiskSpeed)
            {
                enterHighRiskZone = true;
                isLeftTurn = false; // 오른쪽(고위험·고보상) 선로로 진입!
                Debug.Log($"🔥 [분기점 돌파 성공!] 요구 속도({requiredHighRiskSpeed}km) 달성! (현재: {currentSpeed}km) -> 고위험·고보상 구역 진입!");
            }
            else
            {
                enterHighRiskZone = false;
                isLeftTurn = true; // 속도 부족으로 관성 돌파 실패 -> 안전/저보상 선로로 강제 이탈!
                Debug.LogWarning($"⚠️ [관성 돌파 실패!] 기차 속도 부족! (현재: {currentSpeed}km / 요구: {requiredHighRiskSpeed}km) -> 선로가 밀려 저보상 구역으로 우회합니다!");
            }
        }
        else
        {
            enterHighRiskZone = false;
            isLeftTurn = true;
            Debug.Log($"🟢 [안전 분기 진입] 왼쪽 안전·저보상 구역 선로로 진입합니다. (현재 속도: {currentSpeed}km)");
        }

        // 4. 모든 클라이언트(P1~P4)에게 카메라 틸트 및 터널 전환 연출 RPC 전송!
        Rpc_PlayJunctionTransition(isLeftTurn, enterHighRiskZone);
    }

    private float GetCurrentTrainSpeed()
    {
        // 만약 개발자 A의 TreadmillManager에 속도 프로퍼티가 있다면 리플렉션으로 안전하게 읽어옴 (컴파일 에러 원천 방지!)
        if (TreadmillManager.Instance != null)
        {
            var type = TreadmillManager.Instance.GetType();
            var field = type.GetField("currentSpeed") ?? type.GetField("scrollSpeed") ?? type.GetField("speed");
            if (field != null && field.FieldType == typeof(float))
                return (float)field.GetValue(TreadmillManager.Instance);

            var prop = type.GetProperty("CurrentSpeed") ?? type.GetProperty("ScrollSpeed");
            if (prop != null && prop.PropertyType == typeof(float))
                return (float)prop.GetValue(TreadmillManager.Instance);
        }
        return testCurrentTrainSpeed;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_PlayJunctionTransition(NetworkBool isLeftTurn, NetworkBool isHighRiskSuccess)
    {
        if (!_isTransitioning)
        {
            StartCoroutine(JunctionTransitionRoutine(isLeftTurn, isHighRiskSuccess));
        }
    }

    private IEnumerator JunctionTransitionRoutine(bool isLeftTurn, bool isHighRiskSuccess)
    {
        _isTransitioning = true;

        // 내 로컬 플레이어의 PlayerMovement 찾기
        PlayerMovement localPlayer = null;
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (pm.HasStateAuthority)
            {
                localPlayer = pm;
                break;
            }
        }

        // 왼쪽 회전이면 카메라 Z축을 +방향, 오른쪽 회전이면 -방향으로 기울임
        float targetRoll = isLeftTurn ? maxTiltAngle : -maxTiltAngle;
        float elapsed = 0f;

        // 환경 안개(Fog) 백업 (터널 진입 시 시야가 좁아지는 효과)
        bool origFog = RenderSettings.fog;
        float origFogDensity = RenderSettings.fogDensity;
        Color origFogColor = RenderSettings.fogColor;

        if (enableTunnelDarkness)
        {
            RenderSettings.fog = true;
            RenderSettings.fogColor = Color.black;
        }

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = elapsed / transitionDuration; // 0.0 -> 1.0

            // 부드러운 종 모양 곡선(Sin 곡선: 0 -> 1 -> 0)으로 기울기와 터널 암전 계산
            float curve = Mathf.Sin(normalizedTime * Mathf.PI);

            if (localPlayer != null)
            {
                // 1. 카메라 좌/우 틸트 (원심력 연출)
                localPlayer.ExternalCameraRoll = targetRoll * curve;

                // 2. 선로 분기점 덜컹거림 (초반과 중반에 강한 진동)
                float currentShake = shakeIntensity * curve;
                localPlayer.ExternalCameraShake = new Vector3(
                    Random.Range(-currentShake, currentShake),
                    Random.Range(-currentShake, currentShake),
                    0f
                );
            }

            // 3. 터널 통과 암전 농도 업데이트
            if (enableTunnelDarkness)
            {
                _currentTunnelAlpha = maxTunnelDarknessAlpha * curve;
                RenderSettings.fogDensity = Mathf.Lerp(origFogDensity, 0.15f, curve);
            }

            yield return null;
        }

        // 연출 종료 후 원상 복구
        if (localPlayer != null)
        {
            localPlayer.ExternalCameraRoll = 0f;
            localPlayer.ExternalCameraShake = Vector3.zero;
        }

        if (enableTunnelDarkness)
        {
            _currentTunnelAlpha = 0f;
            RenderSettings.fog = origFog;
            RenderSettings.fogDensity = origFogDensity;
            RenderSettings.fogColor = origFogColor;
        }

        _isTransitioning = false;
        Debug.Log("✅ [분기점 연출 완료] 새로운 선로 구역 진입 완료!");
    }

    // 별도 UI 프리팹 없이도 터널 통과 시 어두워지는 효과를 그려주는 경량 OnGUI (개발자 A의 Canvas와 충돌 0%)
    private void OnGUI()
    {
        if (!enableTunnelDarkness || _currentTunnelAlpha <= 0.01f) return;

        Color prevColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, _currentTunnelAlpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prevColor;
    }

    // 기차 앞부분 콜라이더가 분기점 트리거에 닿았을 때도 자동 실행되도록 지원
    private void OnTriggerEnter(Collider other)
    {
        if (!HasStateAuthority || _isTransitioning) return;

        if (other.CompareTag("Train") || other.transform.root.name.Contains("Train"))
        {
            EvaluateAndTriggerJunction();
        }
    }
}