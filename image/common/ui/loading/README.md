# 공통 미니게임 로딩 UI 에셋

모든 미니게임 로딩·캘리브레이션 화면에서 공통으로 사용하는 PNG다.

| 파일 | 용도 |
|---|---|
| `camera_frame.png` | 로컬 웹캠 `RawImage` 위에 겹치는 투명 프레임 |
| `loading_ring.png` | Unity에서 `RectTransform`을 계속 회전시키는 원형 로딩 아이콘 |
| `logo_base.png` | 게임별 로고 제작에 사용하는 빈 공통 로고판 |
| `message_panel.png` | 안내 문구를 TextMeshPro로 올리는 빈 패널 |
| `status_badge_base.png` | 본인/상대방 준비 상태를 조립하는 빈 배지 |
| `status_icon_loading.png` | `status_badge_base` 원형 슬롯에 넣는 준비 중 아이콘 |
| `status_icon_ready.png` | `status_badge_base` 원형 슬롯에 넣는 준비 완료 아이콘 |
| `tip_panel.png` | 화면 하단의 순환 팁 문구용 빈 패널 |

## 공통 조립 규칙

- 로딩 배경은 새로 복제하지 않고 `image/screens/loading/`의 기존 배경 중 하나를 매번 랜덤으로
  선택한다.
- Unity에서 실제 레이아웃을 편집할 때는 별도 씬을 찾지 말고
  `pc-game/Assets/Resources/Loading/LoadingScreenCanvas.prefab`을 Prefab Mode로 연다.
- 로딩 링은 PNG 프레임 교체가 아니라 `loading_ring.png` 하나를 Unity에서 회전시킨다.
- 안내문, 팁, 본인/상대 상태 문구는 베이스 PNG에 굽지 않고 TextMeshPro로 표시한다.
- `status_badge_base.png`의 원형 슬롯에는 `status_icon_loading.png` 또는
  `status_icon_ready.png`만 상태에 따라 교체한다.
- 카메라 프레임 안쪽에는 각 플레이어의 로컬 실시간 영상을 표시한다.
