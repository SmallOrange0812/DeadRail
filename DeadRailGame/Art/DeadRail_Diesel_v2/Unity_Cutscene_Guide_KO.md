# DeadRail v2 — Unity 컷씬 제작·연결 안내

## 이번 전달물과 프로젝트 기준

Blender 모델, 개별 FBX, 실제 모델 렌더 이미지와 제작 안내입니다. Unity 씬·C#·패키지는 변경하지 않았고, 재생 가능한 Timeline이나 네트워크 기능은 아직 연결하지 않았습니다.

현재 프로젝트: Unity **6000.3.25f1**, URP **17.3.0**, Timeline **1.8.13**. `Packages/manifest.json`에 Cinemachine은 없습니다. 아래 기본 절차는 일반 Camera와 Timeline만으로 가능합니다. Cinemachine은 부드러운 카메라 블렌드가 필요할 때 추가하는 선택 사항입니다.

스토리보드 해석: **좌/우 우회선 + 중앙 직진 터널**, 경고 바리케이드 충돌 → 입구 목재 파괴 → 전력 차단 → 터널 정차·파밍 → 반대쪽 봉쇄물 파괴·탈출. 중앙 터널을 블랙아웃 실패 경로로 배치했습니다. 어떤 정상 선택이든 배터리 고갈/실패 판정 시 최종 경로를 중앙 터널로 강제하는 것은 게임 상태 코드에서 처리해야 합니다.

## 1. 에셋 가져오기

`FBX` 폴더에서 필요한 파일을 Unity의 `Assets/Art/DeadRailV2`로 복사하세요. Train, Track_ThreeWay, TrackSwitch, Blackout_Tunnel, Entry_Boards, Entry_Debris, Exit_Boards, Exit_Debris, Approach_Roadblock, Exit_Roadblock이 핵심입니다. 전체 열차 FBX에는 P2 카메라·탐조등·콘솔·울타리가 이미 포함됩니다. 별도 카메라 FBX를 또 겹쳐 놓지 마세요.

단위는 미터입니다. Blender +Y 전방 / +Z 위에서 FBX -Z Forward / Y Up으로 변환했습니다. Unity에서 실제 렌즈와 기관차 전면을 확인해 루트 방향을 맞추세요. 개별 FBX는 모두 루트 원점에서 내보내므로 아래 배치값은 별도로 적용합니다.

| 에셋 | Blender 프리뷰 배치 / 의미 |
|---|---|
| ThreeWay | Y=28, 공용 입구에서 세 경로로 분기 |
| Tunnel | Y=60, 길이 24m, 중심선 X=0 |
| Entry Boards | Y=59.1, 터널 입구 앞 |
| Entry Roadblock | Y=55.8, 목재보다 먼저 충돌 |
| Exit Boards | Y=84.9, 반대쪽 출구 |
| Exit Roadblock | Y=85.8 |

Unity에서 이 값은 진행 방향 축으로 옮깁니다. 지붕 울타리는 높이 1.15m의 시각 모델입니다. **울타리 각 변에 연속 Box Collider**를 추가해야 실제 낙하 방지가 됩니다. 얇은 난간 메시만으로 캐릭터 충돌을 처리하지 마세요. 해치 안전문은 닫힌 상태를 기본으로 하고 등반 중에만 열도록 연결합니다.

터널은 내부가 빈 아치형이며 폭 9.6m, 최고 높이 7.8m입니다. 정적 터널에는 비볼록 Mesh Collider 또는 분할 Box Collider를 사용하고, 입구 전체를 하나의 박스 콜라이더로 막지 마세요. 이동할 파편에는 단순 Box Collider와 Rigidbody를 사용합니다. URP의 불투명·유리·발광 재질은 Unity에서 다시 설정하세요.

## 2. P2 카메라 화면 연결

`P2_InspectionCamera_ROOT`가 탐조등의 `Searchlight_PitchPivot` 아래에 있으므로 탐조등과 함께 yaw/pitch가 움직입니다.

1. `P2_Camera_ViewSocket` 아래에 Unity Camera를 만듭니다. Camera의 로컬 +Z가 모델 렌즈 밖을 향하도록 회전을 확인합니다.
2. Render Texture를 만들고 1024×576으로 시작합니다. Camera의 Target Texture에 지정합니다.
3. `P2_CameraFeed_Screen`에 URP Unlit 재질을 붙이고 해당 Render Texture를 Base Map에 지정합니다. 또는 콘솔용 World Space Canvas의 RawImage.texture에 연결합니다.
4. 콘솔 UI·화면 레이어는 P2 Camera의 Culling Mask에서 빼서 화면이 자기 자신을 촬영하지 않게 합니다. P2 Camera에는 AudioListener를 추가하지 않습니다.
5. P2 조준 입력은 공용 yaw/pitch 피벗을 회전시키고, 줌은 Camera FOV를 예를 들어 60→20으로 조절합니다. 블랙아웃에서는 실제 Spot Light, 화면 발광, 카메라 업데이트를 끄고 `NO SIGNAL` UI를 표시합니다. 업데이트만 끄면 마지막 화면이 남으므로 검은 화면 처리가 필요합니다.

Camera → RenderTexture → 재질 구조는 [Unity 6.3 Render Texture 문서](https://docs.unity.com/en-us/engine/6000.3/manual/materials-and-shaders/textures/render-texture/class-render-texture-create)를 참고하세요.

## 3. 컷씬을 두 개로 나누기

추천 흐름은 **진입 컷씬 10초 → 실제 파밍 플레이 60초 → 탈출 컷씬 10~15초**입니다. 첨부 스토리보드의 35~45초는 파밍 장면을 20초로 압축한 영상 구성이고, 패널에는 60초 타이머도 나옵니다. 실제 플레이에서 파밍까지 강제로 45초 영상에 넣지 말고, 소개 영상이 필요할 때만 파밍 몽타주를 별도로 만드세요. 이 시간은 제안값이며 조정할 수 있습니다.

Hierarchy 예시:

```text
TrainRoot                         # 원점 고정, 기존 네트워크 플레이어 유지
BlackoutSetRoot                   # 터널·목재·장애물·해당 선로 묶음
  Tunnel
  EntryBoards_Intact
  EntryBoards_Debris              # 시작 시 비활성
  EntryRoadblock
  ExitBoards_Intact
  ExitBoards_Debris               # 시작 시 비활성
CutsceneRig                      # 열차/환경 루트와 별도
  CutsceneCamera
  EntryDirector
  ExitDirector
  CutsceneSignals
```

1. `Window > Sequencing > Timeline`에서 `EntryDirector`를 선택하고 `Blackout_Entry.playable`을 만듭니다. Playable Director의 Play On Awake를 끄고 Wrap Mode를 None으로 설정합니다.
2. `CutsceneCamera`의 Animator에 Animation Track을 바인딩합니다. 위치·회전·FOV를 샷별로 키프레임화합니다. 이동은 부드러운 보간, 순간 컷은 별도 카메라 전환이나 constant 키프레임으로 만듭니다. 전체 구간에 원치 않는 자동 보간이 생기지 않는지 확인하세요.
3. 환경 이동은 `BlackoutSetRoot` Animation Track에 기록합니다. 충돌음·목재 파손음·터널 잔향은 Audio Track, 먼지·스파크는 Control Track 또는 Signal로 연결합니다.
4. Signal Track에 이벤트를 배치하고 Signal Receiver에서 프로젝트용 공개 함수에 연결합니다. 아래 함수명은 구현할 연결 인터페이스 예시이며 이번 파일에 포함된 C# 함수가 아닙니다.
5. `ExitDirector`에도 별도 Timeline을 만듭니다. Director 자신이나 부모를 그 Director의 Activation Track으로 꺼 버리지 마세요. 루트 관리와 상태 정리는 별도 컨트롤러에서 담당합니다.

Timeline은 씬 오브젝트를 트랙에 바인딩하고 Director가 재생합니다. [Playable Director](https://docs.unity3d.com/Packages/com.unity.timeline@1.8/manual/playable-director.html), [Signal 연결](https://docs.unity3d.com/Packages/com.unity.timeline@1.8/manual/wf-signals.html), [Activation Track](https://docs.unity.cn/Packages/com.unity.timeline%401.8/manual/insp-trk-act.html).

## 4. 스토리보드별 타이밍

| 진입 시간 | 화면 / 실제 동작 | 연결 이벤트 예시 |
|---|---|---|
| 0~1.5초 | 지붕 뒤 상공에서 3갈래와 중앙 터널 확인 | BeginEntry: 로컬 조작 잠금, 게임 카메라 백업, 컷씬 카메라 전환 |
| 1.5~3초 | 기관차 측면 낮은 샷, 경고등 바리케이드 접근 | BlackoutSetRoot를 열차 쪽으로 이동 |
| 3초 | 바리케이드 충돌 | BreakRoadblock: 장애물 콜라이더 해제, 본체를 옆으로 날리는 애니메이션, 스파크·충돌음 |
| 3~4.5초 | 터널 입구의 교차 목재 파괴 클로즈업 | BreakEntryBoards: 정상 목재 끄기, 파편 켜기 |
| 4.5~6초 | 열차 옆 흔들림, 터널 안으로 진입 | 카메라 짧은 충격, 먼지, 터널 환경 계속 이동 |
| 6~8초 | 콘솔·탐조등 꺼짐 | SetPowerOff: 실제 조명·발광·P2 영상·콘솔 UI 차단, 비상등 유지 |
| 8~10초 | 터널 암전, BLACKOUT PHASE 타이틀 | 페이드 후 터널 정차 위치 확정 |
| 10초 | 플레이어 시점 복귀 | EndEntry: 조작 복원, 60초 파밍 타이머 시작 |

탈출은 플레이어의 출구 파괴가 완료되거나 기획된 출발 조건이 만족되면 시작합니다.

| 탈출 시간 | 화면 / 동작 |
|---|---|
| 0~3초 | P3 사격과 출구 바리케이드·목재 파괴. 실제 사격으로 이미 파괴됐다면 중복 파괴를 생략 |
| 3~6초 | 맞은편 출구 쪽에서 나무 조각이 흩어지고 기관차가 나타나는 샷: 손그림 2~4 반영 |
| 6~10초 | 빛을 향해 가속, 먼지·파편을 지나가는 측면 샷 |
| 10~15초 | 터널 밖에서 열차를 보여준 뒤 게임 시점·주행으로 복귀 |

입구와 출구 카메라는 서로 반대쪽에 놓습니다. “입구로 들어감”과 “반대편에서 부수고 나옴”이 같은 방향의 샷으로 보이지 않도록 하세요.

## 5. 목재 파괴와 파편

각 봉쇄물은 정상 목재 세트와 12개 파편 세트가 있습니다. 파편은 원래 목재와 같은 위치에서 시작하며 한쪽 끝이 불규칙하게 깨져 있습니다. 아직 폭발/비산 애니메이션이나 물리는 들어 있지 않습니다.

정상 세트에는 충돌용 Box Collider, 파편 세트에는 조각별 Rigidbody + Box Collider를 붙입니다. 파편 세트는 시작 시 비활성으로 둡니다. 충돌 이벤트에서는 정상 세트와 콜라이더를 끄고 파편을 켠 뒤 바깥쪽·위쪽으로 제한된 impulse를 줍니다. 플레이어와 파편 레이어 충돌은 끄는 편이 컷씬 중 밀려남을 방지하기 쉽습니다. 조각은 짧은 시간 뒤 정리합니다.

멀티플레이에서 동일한 화면 구도가 필요하면 랜덤 물리 대신 미리 키프레임한 파편 이동을 권장합니다. 물리로 처리할 때는 조각을 네트워크 동기화할지, 로컬 장식으로 둘지 정하고, 실제 통행 가능 상태는 방장이 관리합니다. 본체가 살아 있는 상태에서 파편만 켜서 길을 계속 막는 일이 없도록 하세요.

## 6. 현재 프로젝트에서 함께 고칠 부분

현재 코드 확인 결과이며, 아래 변경은 아직 적용하지 않았습니다.

| 파일 | 현재 상태 | 필요한 변경 |
|---|---|---|
| `Assets/_Dev_B/TrackSwitch.cs` | Left_SafeZone / Right_HighRiskZone 두 enum 값, 이진 토글 | Straight_Blackout 추가. 기존 직렬화 값 Left=0 / Right=1을 유지하고 Straight=2 추가 권장. 순환은 enum 산술 대신 Left→Straight→Right 명시. 상태별 회전·표시등 3개 처리 |
| `Assets/_Dev_A/Scripts/MapNodeManager.cs` | IsLeftSwitchSelected, lastTurnWasLeft가 bool | 공통 3방향 enum으로 경로 선택을 표현. 실제 확정 경로와 사용자가 고른 경로 분리. 직진은 회전각 0 |
| `Assets/_Dev_B/JunctionBranchDirector.cs` | 속도 실패 시 왼쪽 안전 경로로 보냄, 좌우 bool RPC | 실패/배터리 고갈 시 중앙 Dark Node로 보내고 EntryDirector 시작. 두 분기 시스템 중 하나가 최종 판정 주체가 되도록 통합 |
| `Assets/_Dev_B/TreadmillJunctionPack.cs` | 반복 배치 시 왼쪽으로 초기화 | 3상태 기본값 정의, 컷씬 중 이동과 자동 반복 정지 |
| `Assets/_Dev_A/Scripts/TreadmillManager.cs` | CurrentSpeed와 속도 변경 RPC 제공 | 컷씬 제어 상태에서 게임 이동 갱신과 Timeline 이동이 경쟁하지 않게 분리 |

선로 모델의 `Exit_Left`, `Exit_Straight`, `Exit_Right`와 `Path_Left/Path_Straight/Path_Right`를 연결 마커로 사용합니다. 프리뷰에서는 중앙이 터널로 이어집니다. `TrackSwitch_Lever_Pivot` 회전은 Blender Y축 기준 -25/0/+25도이므로 FBX 후 Unity 축을 확인하여 Inspector 회전값을 지정하세요.

## 7. 트레드밀·네트워크와 컷씬

기차와 플레이어는 기존처럼 원점에 두고 `BlackoutSetRoot`만 뒤로 이동시키는 구성이 적합합니다. 컷씬 중에는 해당 환경의 TreadmillScenery/TreadmillJunctionPack 이동을 멈춰 Timeline과 이중 이동하지 않도록 합니다. 씬 전체의 `Time.timeScale=0`으로 잠그면 타임라인·물리까지 멈출 수 있으므로 로컬 입력과 경쟁하는 이동 컴포넌트를 개별 제어하세요.

추천 상태: Running → BlackoutEntry → TunnelLoot → BlackoutExit → Running. 방장이 상태, SequenceId, 시작 네트워크 tick/time, 봉쇄물 파괴 여부를 결정하고 동기화합니다. 클라이언트는 공유된 시작 시각으로부터 경과 시간을 계산해 Director의 재생 위치를 맞춥니다. 전환 RPC만 보내면 지연 입장한 플레이어는 이벤트를 놓칠 수 있으므로 지속되는 네트워크 상태가 필요합니다. 중간부터 재생할 때 과거 Signal이 모두 실행된다고 가정하지 말고, 현재 상태로 정상 목재/파편/전력/카메라를 직접 복원하세요.

컷씬 종료·스킵·연결 종료·오브젝트 Disable 모두에서 공통 정리 함수를 호출해 입력, 카메라, AudioListener, FOV, 안개/노출, 이동 제어를 복구합니다. 단, 블랙아웃 진입 종료 때 전력을 자동 복원하지 않고 현재 게임 상태를 따릅니다. 보상·체력·경로 선택은 로컬 Timeline Signal에서 각자 확정하지 말고 방장 상태 전환에서 한 번만 처리합니다.

## 검수 순서

1. P2 Camera 프리뷰와 콘솔 화면이 같은 방향을 보는지 확인.
2. 지붕 울타리·해치문 콜라이더로 모든 변의 추락을 막는지 캐릭터로 확인.
3. Left / Straight / Right 세 경로와 직진 실패 진입 판정 확인.
4. 목재 파괴 시 원래 콜라이더가 사라지고 출구가 실제로 열리는지 확인.
5. 컷씬 종료 후 이동·마우스·P2 영상·카메라가 게임 상태에 맞게 복원되는지 확인.
6. 호스트+클라이언트, 지연 접속, 스킵, 중복 트리거, 파밍 도중 연결 종료를 테스트.
