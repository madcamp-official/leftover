# 이미지 에셋 구조

`image/`는 제작·수정용 원본 PNG 보관소입니다. Unity가 런타임에 읽는 복사본은
`pc-game/Assets/Resources/`에 있으므로, 원본 수정 뒤 Unity용 파일도 함께 갱신해야 합니다.

## 폴더 구성

```text
image/
├── characters/
│   ├── character1/
│   │   ├── common/{idle,jump}/
│   │   ├── games/{01_stone_throw,02_coconut_smash,04_eye_fight,
│   │   │          05_stone_or_banana,07_scream_duel,08_feather_flight}/
│   │   ├── joints/{front,back}/
│   │   └── reference/
│   └── character2/                  character1과 같은 기준
├── common/
│   ├── props/
│   └── ui/{hud,loading}/
├── games/
│   ├── stone_throw/
│   ├── fruit_jump/
│   ├── coconut_break/
│   ├── stone_or_banana/
│   ├── staring_contest/
│   ├── scream_duel/
│   └── feather_flight/
└── screens/
    ├── start/
    ├── loading/
    ├── multiplayer/
    ├── howto/
    ├── settings/
    └── result/
```

## 사용 규칙

- 두 캐릭터의 대응 프레임은 같은 폴더명과 번호 체계를 유지합니다.
- 캐릭터 공통 기본 자세와 점프는 `characters/<character>/common/`에 둡니다.
- 특정 게임에만 쓰는 표정·동작은 `characters/<character>/games/<game>/`에 둡니다.
- 여러 게임이 함께 쓰는 돌·바나나·코코넛·과일은 `common/props/`에 둡니다.
- 남은 시간판 같은 공용 HUD는 `common/ui/hud/`, 로딩 조립 에셋은
  `common/ui/loading/`에 둡니다.
- 게임 배경·HUD·환경 오브젝트는 `games/<game>/`에, 전체 화면 UI는
  `screens/<screen>/`에 둡니다.
- `previews/`와 `reference/`는 배치·생성 참고용이며 Unity 런타임 의존 대상으로 사용하지
  않습니다.
- 투명 PNG의 배경 제거 상태와 캐릭터 피벗을 확인한 뒤 Resources에 복사합니다.

## Unity 반영 위치

| 원본 종류 | Unity 위치 |
|---|---|
| 캐릭터 프레임 | `pc-game/Assets/Resources/Characters/` |
| 게임 배경·HUD | `pc-game/Assets/Resources/<GameName>/` |
| 로딩 화면 | `pc-game/Assets/Resources/Loading/` |
| 시작·설정·결과 UI | `pc-game/Assets/Resources/UI/` |

화면에서의 위치·크기는 PNG 자체가 아니라 각 씬의 `EditableLayout` 또는 프리팹에서
조정합니다. 로딩 화면은 `pc-game/Assets/Resources/Loading/LoadingScreenCanvas.prefab`,
시작 화면은 `pc-game/Assets/Prefabs/StartScreenCanvas.prefab`이 편집 기준입니다.

## 정리 원칙

- 최종 개별 프레임과 내용이 중복되는 스프라이트 시트·생성 중간물은 남기지 않습니다.
- AI 생성 원본이나 크로마키 작업 파일은 최종 결과 검수 후 제거합니다.
- 실제로 쓰지 않는 Resources 복사본도 함께 제거해 빌드 용량과 검색 혼선을 줄입니다.
- 삭제 전 `rg`와 Unity 참조를 확인하고, 삭제 내역은 루트
  [DEVELOPMENT_LOG.md](../DEVELOPMENT_LOG.md)에 기록합니다.
