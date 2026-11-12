# [DeadRail] 프로젝트 인수인계 문서 (Handover Document)

## 1. 프로젝트 개요
*   **DeadRail 프로젝트의 목적과 핵심 콘셉트:** 멀티플레이어 환경(Photon Fusion 기반)에서 동작하는 3D 게임 (세부 장르 및 스토리: **확인 필요**)
*   **주요 기능 및 사용자에게 제공하는 경험:** 
    *   플레이어 캐릭터(`Player_A`)의 무기 조준 및 장비 사용.
    *   상하체 애니메이션 분리(Upper-Body IK)를 통한 자연스러운 런타임 조준/시선 처리.
*   **현재 개발 단계:** 캐릭터 리깅(Animation Rigging) 및 애니메이션 네트워크 동기화 트러블슈팅 후, 타 개발자(A) 작업물 병합을 위한 롤백(순정 상태 복구) 완료 단계.
*   **사용 중인 프로그래밍 언어, 프레임워크, 라이브러리 및 개발 도구:**
    *   **엔진/언어:** Unity 3D / C#
    *   **멀티플레이어 프레임워크:** Photon Fusion
    *   **애니메이션/리깅:** Unity Animation Rigging, Network Mecanim Animator
    *   **컴파일러:** Burst Compiler (최적화 연산용)

## 2. Git 및 작업 상태
*   **현재 브랜치 이름:** **확인 필요**
*   **관련 커밋 해시와 커밋 내용:** **확인 필요** (현재 리깅 시스템을 지우고 롤백한 '순정 상태'가 로컬에 커밋되어 있을 것으로 추정)
*   **작업 중인 브랜치와 다른 브랜치의 관계:** 현재 작업 브랜치에 **'개발자 A'의 최신 작업물을 병합(Merge/Pull)**해야 하는 상태.
*   **커밋하지 않은 변경사항:** **확인 필요**
*   **Git 관련 주의사항:** 프리팹(`Player_A.prefab`) 충돌(Conflict) 발생 가능성이 높으므로, 병합 시 애니메이터 및 리깅 컴포넌트 유실/중복에 각별히 주의할 것.

## 3. 프로젝트 구조 및 관련 파일
*   **주요 폴더 구조:** `C:/Projects/DeadRail/DeadRailGame/` (에러 로그 기반 확인)
*   **핵심 파일의 경로와 역할:**
    *   `Player_A.prefab`: 플레이어 메인 프리팹. (네트워크, 애니메이션, 물리 컴포넌트 포함)
    *   `X Bot.fbx` / `X BotAvatar`: 사용 중인 기본 3D 캐릭터 모델(Mixamo 기반 추정) 및 아바타.
*   **주요 기능이 구현된 파일:**
    *   `EquipmentPivotController.cs`: 장비/무기 피벗 및 뼈대 자동 세팅 스크립트.
    *   `PlayerAnim.controller`: 메인 애니메이터 컨트롤러.
    *   `UpperBodyMask.mask`: 상체 리깅 및 애니메이션 분리를 위한 아바타 마스크.
*   **파일 간 의존 관계:** `EquipmentPivotController`는 런타임 시 캐릭터의 뼈대(Transform) 데이터를 참조함.
*   **수정 시 주의해야 할 파일:** `Player_A.prefab` (멀티플레이 스폰 대상이므로 반드시 씬이 아닌 Project 창의 프리팹 원본에 Overrides -> Apply All로 저장해야 함).

## 4. 완료한 작업
*   **지금까지 구현한 기능:**
    *   Photon Fusion 기반 `Network Mecanim Animator` 셋업 및 기본 애니메이션 동기화.
*   **수정한 오류 및 버그 (실제 테스트 완료):**
    *   **Burst Compiler 에러 (`PropertyStreamHandle`):** 꼬여있던 리깅 캐시를 지우고 순정 상태(Base Layer만 남김, Rig Builder 삭제)로 롤백하여 해결 완료.
    *   **캐릭터 동결(T포즈/A포즈) 이슈:** Culling Mode를 `Always Animate`로 변경하고, `X Bot.fbx`를 Reimport한 뒤, Network Animator를 Reset하여 해결 완료.
*   **UI 및 디자인 개선 사항:** **확인 필요**
*   **성능 및 안정성 개선 사항:** 잘못된 뼈대 구조로 인한 런타임 크래시 유발 요인 제거.
*   **각 기능의 구현 상태:** 상하체 분리 IK 기능은 원인 규명 후 재구현을 위해 임시로 삭제(0%)된 상태. 기본 애니메이션은 정상(100%).

## 5. 확정된 개발 결정
*   **기술 스택을 선택한 이유:** 멀티플레이어 물리/애니메이션 동기화를 위해 Photon Fusion 및 내장 Network Animator 사용.
*   **기능 구현 방식:** 무기 조준 및 상체 움직임은 Unity Animation Rigging의 `Two Bone IK Constraint` 및 `Multi-Aim Constraint`를 사용.
*   **기존에 논의하고 확정한 개발 규칙:**
    *   **뼈대 최적화 해제:** `X Bot.fbx` 임포트 세팅에서 `Optimize Game Objects`는 **반드시 체크 해제** (런타임 뼈대 증발 방지).
    *   **올바른 관절 순서 연결 (필수):** Two Bone IK 설정 시 `Arm(위팔) -> ForeArm(아래팔) -> Hand(손)` 순서를 엄수할 것. (ForeArm을 건너뛰면 Burst 에러 무한 발생).
    *   **컴포넌트 위치:** `Rig Builder`는 반드시 `Animator`가 부착된 최상단 객체에 위치해야 함. IK Target 객체들도 반드시 Animator의 자식으로 배치할 것.
*   **채택하지 않은 대안과 그 이유:** 꼬여버린 리깅 상태에서 부분 수정을 시도했으나, Unity Burst 컴파일러 캐시 고착화 문제로 인해 **'전체 삭제 후 재구축'** 방식으로 우회함.

## 6. 현재 남아 있는 문제
*   **해결되지 않은 버그:** 없음 (초기화 완료).
*   **미완성 기능 (중요도: 높음):** 상하체 애니메이션 분리 및 상체 무기 조준 리깅(IK). (현재 롤백 상태)
*   **성능/UI 문제:** **확인 필요**
*   **기술적 한계/추가 검토 사항:** `EquipmentPivotController.cs` 내부의 `AutoSetupRigAndBones()` 함수가 런타임에 뼈대 레퍼런스를 초기화하여 버그를 유발할 가능성이 있음 (현재 주석 처리/비활성화 상태).

## 7. 앞으로 구현할 작업
*   **남은 기능 목록:**
    1.  상하체 분리 애니메이션 레이어(`UpperBody Layer`) 재설정.
    2.  `Rig Builder` 및 `Two Bone IK` (양팔), `Multi-Aim` (시선/척추) 재설정.
*   **구현 순서 (기존 합의된 계획):**
    1.  **[선행 작업]** 개발자 A의 최신 작업물 병합 (Git Merge).
    2.  병합 후 기본 애니메이션 정상 작동 여부 런타임 테스트.
    3.  `Rig Builder` 추가 및 뼈대 순서(Arm->ForeArm->Hand)에 맞춰 Rig Hierarchy 재구축.
    4.  `UpperBodyMask`에 Target 뼈대 및 Rig 폴더를 모두 활성화(초록색 체크)하여 가림 현상 방지.
    5.  `EquipmentPivotController` 재활성화 및 연동.
*   **예상되는 수정 파일:** `Player_A.prefab`, `PlayerAnim.controller`, `UpperBodyMask.mask`
*   **완료 기준:** 멀티플레이 런타임 환경에서 Burst 에러 없이 상체(무기 조준)와 하체(이동)가 분리되어 부드럽게 렌더링될 것.

## 8. 프로젝트 실행 및 테스트 방법
*   **개발 환경 구성 방법:** Unity Hub를 통해 프로젝트 오픈.
*   **필요한 프로그램 및 버전:** Unity Editor (**버전 확인 필요**), Animation Rigging 패키지, Photon Fusion 패키지.
*   **명령어 (빌드/테스트 등):** **확인 필요** (에디터 내 Play 버튼으로 테스트 진행 중).
*   **오류 발생 시 확인해야 할 사항:** 
    *   Console에 `PropertyStreamHandle` 에러가 뜨면 100% 뼈대 할당 누락, 관절 순서 오류, 또는 FBX 최적화 옵션 켜짐 현상임.
    *   캐릭터가 굳는다면 Animator의 Culling Mode와 Network Mecanim Animator의 Reset 여부 확인.

## 9. Codex에서 작업을 이어가기 위한 지침
*   **먼저 확인해야 할 파일:** `Player_A.prefab` 내부 구조 및 `EquipmentPivotController.cs` 스크립트.
*   **기존 코드를 수정할 때 주의할 점:** `Player_A` 프리팹 수정 시 Scene에 있는 객체가 아니라 Project 창의 원본 프리팹을 더블클릭(Prefab Mode)하여 수정할 것.
*   **회귀 테스트 방법:** 리깅 추가 후 Play 버튼을 눌렀을 때 1) `NullReferenceException`이나 `Burst` 에러가 없는지, 2) 타 클라이언트 화면에서도 상체 조준이 동기화되는지 확인.
*   **충돌 방지 개발 방법:** 뼈대를 스크립트로 강제 할당하는 로직과 Unity Rig Builder의 자동 초기화 로직이 겹치지 않도록 생명주기(Awake, Spawned) 관리에 주의.
*   **작업 완료 후 보고해야 할 내용:** 리깅 재구축 과정에서 추가적인 패키지 충돌이나 Network Animator 동기화 지연 문제가 발생했는지 여부.

---

## 10. 🚀 [필독] 새로운 개발자가 가장 먼저 수행해야 할 작업
*   **수행 작업:** **개발자 A의 작업 분량 병합(Merge) 및 순정 상태 작동 검증**
*   **필요한 이유:** 현재 꼬여있던 데이터를 모두 삭제하고 가장 깨끗한 상태(롤백)로 대기 중입니다. 지금 병합하지 않고 리깅 작업을 진행하면 나중에 프리팹 Conflict 발생 시 뼈대 데이터가 다시 증발할 위험이 매우 높습니다.
*   **작업 순서:**
    1.  Git을 통해 개발자 A의 브랜치/커밋을 현재 작업 브랜치로 병합(Pull/Merge).
    2.  충돌(Conflict) 해결 (특히 `Player_A.prefab` 주의).
    3.  Unity Editor에서 로비 -> 인게임 접속을 통해 캐릭터가 정상 스폰되고 기본 이동/대기(Idle) 애니메이션이 에러 없이 출력되는지 확인.
*   **검증 방법:** Console 창에 `PropertyStreamHandle` 관련 Burst 에러가 단 한 줄도 없어야 하며, 캐릭터가 T포즈로 굳지 않아야 합니다. 이 상태가 확인되면 즉시 **7번 항목의 '구현 순서 3번(Rig Builder 재구축)'**으로 넘어가 상하체 분리 작업을 재개하십시오.