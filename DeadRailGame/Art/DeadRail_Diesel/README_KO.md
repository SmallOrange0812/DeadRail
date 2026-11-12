# DeadRail 디젤기관차 수정본

`DeadRail_Diesel.blend`를 Blender 4.4.1에서 열면 완성된 모델을 확인할 수 있습니다. `DeadRail_Diesel_Blender_4_4_1.py`는 독립 실행 가능한 수정 생성기입니다. 원본 다운로드 파일은 변경하지 않았습니다.

## 반영한 디자인

- 사진의 짧은 전면 보닛, 높은 운전실, 긴 엔진룸, 라디에이터 루버, 6축 대차, 노란 난간을 조합했습니다. 도색은 첫 사진을 참고한 주황·차콜입니다. 로고는 복제하지 않았습니다.
- 길이 14.4m, 실내 폭 약 3.48m. 게임 내 이동을 위해 실물보다 넓은 기관실로 구성했습니다.
- 왼쪽 V12 디젤엔진 바로 뒤에 배터리 트레이를 붙이고 케이블로 연결했습니다. 배터리는 별도 루트로 분리됩니다.
- 엔진룸 오른쪽에 폭 1.55m의 연속 통로를 확보했습니다. 운전실 중앙 통로는 콘솔 사이 약 1.39m입니다.
- 양옆 출입구는 폭 1.4m, 높이 2.18m이며 실제 벽 개구부입니다. 문은 옆으로 열린 위치에 있고, 외부 계단·난간이 있습니다.
- P1 운전 / P2 탐조등 제어는 앞 운전실, P4 레이더는 기관실 앞쪽, P3 사격석은 뒤쪽 옥상에 배치했습니다. 옥상 해치와 사다리도 있습니다.
- `TrackSwitch_ROOT`: 별도 선로변환기. 노란 표적판, 중앙 명중 표적, 회전 레버, 좌우 표시등, 작동함과 연결봉으로 구성했습니다.
- 기존 직선·곡선·분기 선로 모듈을 유지했습니다.

## 확인 방법

렌더 이미지: `01_Diesel_Exterior.png`, `02_Diesel_Interior.png`, `03_TrackSwitch.png`.

스크립트를 Blender Text Editor에서 실행한 세션에서는 Python Console에서 다음을 사용할 수 있습니다.

```python
bpy.app.driver_namespace['deadrail_cutaway'](True)  # 내부 단면 표시
bpy.app.driver_namespace['deadrail_cutaway'](False) # 외관 복원
bpy.app.driver_namespace['deadrail_shot_switch']('RIGHT')
bpy.app.driver_namespace['deadrail_shot_switch']('LEFT')
bpy.app.driver_namespace['deadrail_blackout'](True)
bpy.app.driver_namespace['deadrail_export']()       # 개별 FBX 내보내기
```

저장 파일을 다시 연 경우 함수는 자동 등록되지 않습니다. 단면은 Outliner의 Roof, 오른쪽 벽 부품을 숨겨 보거나, 포함된 `DeadRail_Diesel_Generator.py` 텍스트를 실행해 새 씬에서 확인하세요. 실행은 기존 씬을 삭제하지 않습니다. FBX 저장 위치는 생성기 상단 `OUTPUT_DIR`로 설정합니다.

## Unity 연결 범위

이번 결과물은 모델과 외형 전환 프리뷰입니다. 실시간 총알 판정, 선로 선택, 네트워크 동기화, 실제 이동 가능 여부는 Unity에서 연결해야 합니다. `TrackSwitch_HitTarget`에는 반경 0.43m 구체 콜라이더 권장값, `TrackSwitch_Lever_Pivot`에는 회전축·각도 정보가 들어 있습니다. 엔진/배터리/문/무기/선로변환기는 각각 분리되어 있습니다.

Blender는 +Y 전방 / +Z 위, FBX는 -Z Forward / Y Up, 단위는 미터입니다. 실내 바닥·벽에는 단순 Box Collider를 따로 구성하세요. 사다리는 별도 등반 로직이 필요합니다. 출입 계단과 돌출 난간에 맞게 승강장·환경 배치를 조정하세요. FBX에 발광·투명 재질이 동일하게 재현되지는 않으므로 URP 재질을 설정해야 합니다.

`validation.json`은 Blender 실행 버전, 엔진룸 통로 메시 경계 검사, LEFT/RIGHT 프리뷰 전환 결과입니다. 캐릭터 컨트롤러나 네트워크 테스트 결과는 아닙니다.
