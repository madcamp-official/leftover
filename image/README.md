# 이미지 에셋 구조

`image/`는 제작·수정에 사용하는 원본 PNG 보관소다. Unity에서 실제로 읽는 파일은
`pc-game/Assets/Resources/`에 별도로 복사되어 있으므로, 원본을 갱신한 뒤 필요한 에셋만
Resources 쪽에 반영한다.

## 폴더 구성

```text
image/
├── characters/
│   ├── character1/{front,back,faces}/
│   └── character2/{front,back,faces}/
├── common/
│   ├── props/
│   └── ui/{hud,game_icons,match_results}/
├── games/
│   ├── fruit_jump/{background,hud,previews}/
│   └── stone_throw/{background,hud,previews}/
└── screens/
    └── start/{background,buttons,logo,previews}/
```

## 사용 규칙

- 캐릭터 파츠 파일명은 캐릭터 폴더 안에서 동일하게 유지한다. 예: `front/head.png`,
  `back/right_lower_leg_foot.png`.
- 모든 게임에서 사용하는 남은 시간판은 `common/ui/hud/time_remaining.png` 하나만 사용한다.
- 6게임 공통 승패판과 O/X/무승부 표시는 `common/ui/match_results/`에 둔다.
- 여러 게임에서 재사용하는 돌·바나나·코코넛·과일은 `common/props/`에 둔다.
- 코코넛 깨기와 과일 점프의 게임 아이콘은 각각
  `common/props/coconut_break_left.png`, `common/props/fruit_grapes.png`를 재사용한다.
- `previews/` 이미지는 배치 참고용이며 Unity에서 직접 조립할 개별 에셋이 아니다.
- 새 게임을 추가할 때는 `games/<game_name>/background`, `hud`, `previews` 구조를 따른다.

## 정리 원칙

- 개별 PNG 파츠와 내용이 겹치는 스프라이트 시트는 보관하지 않는다.
- AI 생성 원본이나 크로마키 작업 파일은 최종 에셋을 만든 뒤 저장소에서 제거한다.
- 같은 기능의 UI는 게임 폴더마다 복사하지 않고 `common/ui/`에서 공유한다.
