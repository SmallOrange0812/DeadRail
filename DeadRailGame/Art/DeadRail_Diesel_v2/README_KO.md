# DeadRail 디젤기관차 v2

`DeadRail_Diesel_v2.blend`를 Blender 4.4.1에서 열면 됩니다. 이전 v1 파일과 Unity 프로젝트 코드는 그대로 유지했습니다.

- 탐조등 바로 옆 P2 관측 카메라: 같은 yaw/pitch 피벗에 연결, 콘솔 화면용 소켓 제공.
- P3 옥상: 사격석 4면에 높이 1.15m 난간·중간 난간·발끝막이, 해치 안전문 추가.
- Left / Straight / Right 3갈래 선로: 경로별 출구·웨이포인트, 사격식 변환기 3상태 표시.
- 중앙 직진 블랙아웃 터널: 내부가 빈 24m 아치형, 양 끝 출입구·서비스 보행로·비상등.
- 터널 입구/출구 봉쇄 목재, 각각 12개 파편, 경고등이 있는 노랑/검정 바리케이드.
- 기존 내부 엔진·배터리·출입구·통로 유지.

`FBX` 폴더에는 Unity에 가져갈 개별 모델 20개가 있습니다. 파편 FBX도 검증했으며, 시작 시 비활성으로 배치해야 합니다. 이전 2갈래 모듈은 `.blend`의 숨긴 Legacy 컬렉션에 보존하고 v2 FBX 묶음에서는 제외했습니다. 직선 연장 선로는 8m 모듈을 반복 연결하면 됩니다.

독립 실행 생성기: `DeadRail_Diesel_v2_Blender_4_4_1.py`. Blender Text Editor에서 Alt+P로 실행하면 새 씬을 만듭니다. 이 파일은 다른 Python 파일 없이 실행 가능합니다. 저장된 `.blend`에도 생성기를 텍스트로 포함했습니다.

실행 세션 내 Python Console:

```python
bpy.app.driver_namespace['deadrail_shot_switch']('LEFT')
bpy.app.driver_namespace['deadrail_shot_switch']('STRAIGHT')
bpy.app.driver_namespace['deadrail_shot_switch']('RIGHT')
bpy.app.driver_namespace['deadrail_cutaway'](True)
bpy.app.driver_namespace['deadrail_barricade'](True) # 정상 목재 숨김, 파편 표시; 비산 애니메이션 아님
```

렌더: 01 전체 열차, 02 P2 카메라, 03 옥상 울타리, 04 세 갈래와 터널, 05 터널 입구.

실제 카메라 영상, 추락 방지 충돌체, 피격 판정, 목재 비산, Timeline, 네트워크 연결은 Unity에서 설정해야 합니다. 단계별 제작법과 프로젝트 코드 변경 지점은 `Unity_Cutscene_Guide_KO.md`에 정리했습니다.
