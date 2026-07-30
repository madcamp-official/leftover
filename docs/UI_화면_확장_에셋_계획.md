# UI 화면 확장 — 필요 에셋 목록 + AI 생성 프롬프트 (구현 완료)

> `배포_아키텍처_설계.md`가 확정한 구조(LAN 전용, 서버 없음, 실행파일 하나 + 호스트/클라이언트
> IP 직접 연결 + 혼자하기 봇)로 가려면, 지금 "게임 시작" 버튼 하나뿐인 Hub 화면과 텍스트뿐인
> 결과 화면으로는 부족하다. 이 문서는 새로 필요한 화면과 그 화면에 들어갈 이미지 에셋을 전부
> 정하고, 각 에셋을 AI 이미지 생성기에 그대로 넣을 수 있는 프롬프트까지 작성한다.
>
> **구현 현황(2026-07-30):** 에셋 25장 전부 생성·Unity Resources 동기화 완료. 화면 4개
> (`MultiplayerConnectScreen`, `HowToPlayScreen`, `SettingsScreen`, 최종 결과 화면 강화)와
> Hub 버튼 교체까지 전부 구현 완료 — `Assets/Scripts/GameFlow/`, `Assets/Scripts/Common/`
> 참고. 실제 Play 모드 플레이 테스트는 아직 안 했다.

## 0. 지금 상태

`Assets/Prefabs/StartScreenCanvas.prefab`에 이미 있는 것: `Background`, `Logo`,
`ButtonGameStart`(버튼 1개짜리 "게임 시작"), `ButtonHowToPlay`, `ButtonSettings`,
`ButtonExit`. 뒤 3개는 버튼 이미지(`image/screens/start/buttons/how_to_play.png` 등)까지
이미 있지만 클릭하면 `HubController.cs`가 "준비 중입니다!" 토스트만 띄운다(진짜 화면 없음).
네트워크 호스트/접속 UI는 전용 이미지 없이 `OnGUI`로 임시로 그리고 있다(`HubController.cs`
`DrawNetworkPanel`). 최종 결과 화면도 전용 아트 없이 텍스트만 있다.

## 1. 화면 흐름 (site map)

```
[Hub - 시작 화면]
 ├─ 2인 플레이 → [멀티플레이 연결 화면] → 호스트 시작 / IP 입력 접속 → 로딩 화면 → 매치
 ├─ 1인 플레이 → 로딩 화면(혼자하기 봇 자동 on) → 매치
 ├─ 게임 방법 → [게임 방법 화면] → 뒤로가기 → Hub
 ├─ 설정 → [설정 화면] → 뒤로가기 → Hub
 └─ 종료 → 앱 종료

[매치 7판 종료] → [최종 결과 화면] → 다시하기 → Hub(시작 전) / 메인으로 → Hub
```

새 화면 3개(멀티플레이 연결/게임 방법/설정)는 지금 `startScreenCanvas`/`resultScreenCanvas`가
Hub 씬 안에서 켜고 끄는 것과 같은 방식(같은 씬 안의 별도 Canvas, `HubController`가
`SetActive`로 전환)으로 만들면 된다 — 씬을 새로 만들 필요는 없다.

## 2. 공통 아트 스타일 (모든 프롬프트에 재사용)

기존 에셋(`screens/start/buttons/game_start.png`, `exit.png`,
`common/ui/loading/message_panel.png`, `status_badge_base.png`)을 실제로 열어서 확인한
스타일 언어를 그대로 고정한다 — 새로 생성하는 에셋도 전부 이 문구를 프롬프트에 포함시켜야
화풍이 안 튄다.

**패널/버튼 공통 (밝은 돌판, 버튼용)**
```
Cartoon prehistoric caveman party-game UI panel. A warm tan cracked-stone plaque
bordered by a thick brown wooden log frame with visible bark texture and round log-end
rings, wrapped at intervals with dark olive-green vine rope ties, small cream-white
bone accents (femur-bone shapes) attached near the ends, tiny green leaf sprigs tucked
into a corner. Soft cel-shaded painterly rendering, bold thin brown outline, warm
saturated palette (tan, brown, olive green, cream). Flat, isolated on transparent
background, no photorealism. Matches a friendly Flintstones-esque prehistoric party
game art style.
```

**패널 변형 (어두운 안내판, 텍스트/정보 표시용)**
```
Same frame language as the stone-tablet button (thick bark-textured wooden log border,
vine rope ties, cream bone accents, leaf sprigs) but the interior fill is a dark brown
weathered wood-plank / leather texture instead of tan stone, for holding lighter text
or icons on top. Soft cel-shaded painterly rendering, bold thin brown outline,
transparent background.
```

**텍스트 (버튼/패널에 들어가는 한글 문구)**
```
Korean text "{TEXT}" carved/painted directly onto the stone or wood surface in a
bold, thick, rounded hand-painted display font, cream-white fill with a thick
dark-brown outline and soft drop shadow — matching the logo lettering style exactly.
```

**캐릭터 아이콘 메달**
```
A small circular stone medallion icon: a carved bone-white stone ring border with tiny
cream bone-stud accents at top and bottom, containing a simple bold flat-color
pictogram of {ACTION}. Thick brown outline, warm saturated colors, centered
composition, transparent background, same prehistoric party-game art style as the
rest of the UI.
```

아래 각 프롬프트는 이 네 블록을 그대로 이어붙여서 쓴 것이다 — AI 생성기에 넣을 때는 해당
항목의 전체 프롬프트를 통째로 복사하면 된다.

## 3. Hub 시작 화면 — 버튼 교체

`ButtonGameStart` 하나를 지우고 아래 2개로 교체.

| 파일 | 크기(px) | 용도 |
|---|---|---|
| `screens/start/buttons/2p_play.png` | 3100×1300 | "2인 플레이" — 멀티플레이 연결 화면으로 이동 |
| `screens/start/buttons/1p_play.png` | 3100×1300 | "1인 플레이" — 혼자하기 봇 켜고 바로 로딩 화면으로 |

**2p_play.png 프롬프트**
```
Cartoon prehistoric caveman party-game UI panel. A warm tan cracked-stone plaque
bordered by a thick brown wooden log frame with visible bark texture and round log-end
rings, wrapped at intervals with dark olive-green vine rope ties, small cream-white
bone accents (femur-bone shapes) attached near the ends, tiny green leaf sprigs tucked
into a corner. Soft cel-shaded painterly rendering, bold thin brown outline, warm
saturated palette (tan, brown, olive green, cream). Flat, isolated on transparent
background, no photorealism. Matches a friendly Flintstones-esque prehistoric party
game art style.

Korean text "2인 플레이" carved/painted directly onto the stone surface in a bold,
thick, rounded hand-painted display font, cream-white fill with a thick dark-brown
outline and soft drop shadow — matching the logo lettering style exactly.

Small decorative icon to the left of the text: two simple caveman silhouette heads
side by side (friendly, not detailed) carved into the stone, echoing the game's two
playable characters.
```

**1p_play.png 프롬프트**
```
(패널/버튼 공통 블록 그대로)

Korean text "1인 플레이" carved/painted directly onto the stone surface, same lettering
style as above.

Small decorative icon to the left of the text: a single simple caveman silhouette head
carved into the stone.
```

`how_to_play.png`/`settings.png`/`exit.png`는 이미 있고 화풍도 이미 맞으니 재사용 —
새로 안 만들어도 됨.

## 4. 멀티플레이 연결 화면 (신규)

지금 `OnGUI`로 임시로 그리던 호스트/접속 패널을 정식 이미지 UI로 교체.

| 파일 | 크기(px) | 용도 |
|---|---|---|
| `screens/multiplayer/panel_main.png` | 2400×1600 | 화면 중앙 큰 연결 패널(빈 속지) |
| `screens/multiplayer/tab_host.png` | 900×400 | "방 만들기" 탭 버튼 |
| `screens/multiplayer/tab_join.png` | 900×400 | "참가하기" 탭 버튼 |
| `screens/multiplayer/ip_display_frame.png` | 1400×400 | 호스트 자기 IP 표시용 속지 |
| `screens/multiplayer/ip_input_frame.png` | 1400×400 | 클라이언트 IP 입력 텍스트필드 배경 |
| `screens/multiplayer/button_host_start.png` | 1400×500 | "호스트로 시작" 버튼 |
| `screens/multiplayer/button_connect.png` | 1400×500 | "접속하기" 버튼 |
| `screens/multiplayer/button_back.png` | 500×500 | 뒤로가기(공용, 다른 신규 화면에서도 재사용) |

**panel_main.png**
```
(패널/버튼 공통 블록, 단 인테리어는 넓은 빈 tan stone 속지 — 위쪽에 제목이 들어갈 여유
공간을 남기고, 텍스트/버튼은 없음)

Large wide stone tablet, mostly empty flat tan cracked-stone interior area meant to
hold UI elements on top later, thick bark-textured wooden log border all around with
vine rope ties at the four corners, bone accents at top-left and bottom-right corners,
small leaf sprigs. No text baked in. Transparent background.
```

**tab_host.png / tab_join.png**
```
(패널/버튼 공통 블록, 작은 크기의 탭형 버튼)

Korean text "{방 만들기 / 참가하기}" carved into the stone, same lettering style as the
logo. Small icon to the left: {a simple hut/campfire icon for 방 만들기 / a simple
footprint-trail icon for 참가하기}.
```

**ip_display_frame.png / ip_input_frame.png**
```
(어두운 안내판 블록 — dark wood-plank/leather interior, thinner and wider horizontal
proportions, like an engraved nameplate)

A long horizontal dark wood-plank nameplate frame with a thin bark-textured log border,
small bone accents at both ends, empty dark interior meant to hold text on top later.
No text baked in. Transparent background.
```
(둘 다 같은 프롬프트 재사용 가능 — 용도만 다름: 하나는 IP 표시 전용, 하나는 입력 필드
배경으로 씀)

**button_host_start.png**
```
(패널/버튼 공통 블록)

Korean text "호스트로 시작" carved into the stone, same lettering style as the logo.
Small icon to the left: a simple flag or torch planted in the ground, signaling
"starting/claiming a spot".
```

**button_connect.png**
```
(패널/버튼 공통 블록)

Korean text "접속하기" carved into the stone, same lettering style as the logo. Small
icon to the left: a simple vine/rope loop linking two circles, signaling "connect".
```

**button_back.png**
```
A small round cartoon prehistoric UI button: a tan cracked-stone circle bordered by a
thick bark-textured wooden log ring with a single vine rope tie, one small cream bone
accent at the bottom. Centered on the stone: a bold thick brown left-pointing arrow
carved into the surface (no text). Soft cel-shaded painterly rendering, bold thin
brown outline, transparent background, matches the game's prehistoric party-game UI
style.
```

## 5. 게임 방법 화면 (신규, 확정)

**결정됨**: 아이콘/삽화 없이 **텍스트 영역만 페이지별로 넘기는 구조**로 간다. 패널은
"빈 속지"만 그림으로 만들고, 제목("1. 돌던지기" 등)·본문·페이지 번호("1/7")는 전부 Unity
`Text` 컴포넌트로 코드에서 채운다(이미지로 굽지 않음) — 그래서 미니게임별 아이콘 7장은
목록에서 뺐다. 패널·화살표만 있으면 된다.

| 파일 | 크기(px) | 용도 |
|---|---|---|
| `screens/howto/panel_instruction.png` | 2600×1800 | 제목/본문 텍스트 영역이 들어갈 빈 패널 |
| `screens/howto/button_page_prev.png` | 500×500 | 이전 페이지 화살표 |
| `screens/howto/button_page_next.png` | 500×500 | 다음 페이지 화살표 |

`button_back.png`(4장)를 "닫기"로 재사용.

**panel_instruction.png**
```
(패널/버튼 공통 블록, 큰 사이즈)

Large wide stone tablet with a spacious flat tan cracked-stone interior, completely
empty (no text, no illustration baked in) so a title and paragraph of text can be
overlaid on top later by the UI system, thick bark-textured wooden log border with
vine rope ties at all four corners, bone accents top-center and bottom-center, leaf
sprigs at two corners. Transparent background.
```

**button_page_prev.png / button_page_next.png**
```
(button_back.png와 동일한 작은 원형 버튼 프롬프트, 화살표 방향만 반대로: prev는 왼쪽
화살표, next는 오른쪽 화살표)
```

## 6. 설정 화면 (신규, 확정)

**결정됨**: 음악/효과음 on-off + **카메라·마이크 장치 선택 포함**. 장치 선택 드롭다운은
"카메라"용, "마이크"용 두 개가 화면에 나란히 있어야 하는데, 프레임 이미지는 하나만
만들어서 두 번 배치하면 된다(라벨 텍스트도 이미지에 굽지 않고 Unity `Text`로 처리). 다만
드롭다운은 "누르면 펼쳐진다"는 게 한눈에 보여야 하니 화살표 아이콘을 하나 추가한다.

| 파일 | 크기(px) | 용도 |
|---|---|---|
| `screens/settings/panel_main.png` | 2400×1800 | 설정 화면 큰 패널 |
| `screens/settings/toggle_on.png` | 400×220 | 토글 스위치 켜짐 상태 |
| `screens/settings/toggle_off.png` | 400×220 | 토글 스위치 꺼짐 상태 |
| `screens/settings/slider_track.png` | 1200×160 | 볼륨 슬라이더 트랙 |
| `screens/settings/slider_handle.png` | 260×260 | 볼륨 슬라이더 손잡이 |
| `screens/settings/dropdown_frame.png` | 1400×400 | 카메라 선택 / 마이크 선택 배경(같은 파일을 두 번 배치) |
| `screens/settings/dropdown_arrow.png` | 300×300 | 드롭다운 펼침 화살표(프레임 오른쪽 끝에 겹쳐 배치) |

`button_back.png` 재사용.

**dropdown_arrow.png**
```
A small cartoon prehistoric UI icon: a bold thick brown downward-pointing triangle
arrow carved from a tiny flat stone chip, thin darker-brown outline, soft cel-shaded
painterly rendering, transparent background, matches the game's prehistoric
party-game UI style. Simple and small enough to sit at the end of a nameplate frame
as a "tap to expand" affordance.
```

**panel_main.png** — 4장 `panel_instruction.png`와 같은 프롬프트, 크기만 다름(재사용).

**toggle_on.png**
```
A small cartoon prehistoric toggle switch in the ON position: a rounded tan
cracked-stone pill-shaped track with a thin bark-textured wood-log outline, a round
cream bone-shaped knob pushed to the right side, a tiny lit torch-flame icon glowing
on the left side of the track to signal "on". Soft cel-shaded painterly rendering,
bold thin brown outline, transparent background, matches the game's prehistoric
party-game UI style.
```

**toggle_off.png**
```
Same toggle switch, but the round cream bone-shaped knob is pushed to the left side,
and the torch-flame icon on the track is unlit/greyed out (extinguished, small wisp of
smoke instead of flame) to signal "off". Same style, transparent background.
```

**slider_track.png**
```
A long horizontal cartoon prehistoric slider track: a shallow carved groove in tan
stone with a thin bark-textured wood-log border along the top and bottom edges, small
bone accents at both ends. No handle included (added separately). Transparent
background, matches the game's prehistoric party-game UI style.
```

**slider_handle.png**
```
A small round cartoon prehistoric slider handle: a cream bone-shaped knob with a thin
brown outline and soft shading, matching the bone accents used throughout the game's
UI. Transparent background.
```

**dropdown_frame.png** — `ip_display_frame.png`와 같은 프롬프트 재사용(카메라/마이크
장치 이름을 표시할 어두운 속지).

## 7. 최종 결과 화면 (신규)

지금 텍스트만 있는 결과 화면을 대체. (참고: 예전에 있던 `match_results/` 이미지들은
MatchScoreboard 시스템 자체가 삭제되면서 같이 정리됐다 — 이번엔 새로 설계.)

| 파일 | 크기(px) | 용도 |
|---|---|---|
| `screens/result/panel_victory.png` | 2800×1200 | "{P1/P2} 승리!" 배너 |
| `screens/result/panel_draw.png` | 2800×1200 | "무승부!" 배너 |
| `screens/result/scoreboard_panel.png` | 2000×900 | P1 : P2 점수 표시판 |
| `screens/result/crown_icon.png` | 700×700 | 승자 옆에 붙는 왕관 장식 |
| `screens/result/button_replay.png` | 1600×550 | "다시하기" 버튼 |
| `screens/result/button_main_menu.png` | 1600×550 | "메인으로" 버튼 |

**panel_victory.png**
```
(패널/버튼 공통 블록, 넓고 낮은 배너 형태로)

A wide celebratory banner: a warm tan cracked-stone plaque bordered by a thick
bark-textured wooden log frame, decorated with small confetti-like scattered leaves,
flower petals, and tied vine bunting along the top edge, bone accents at both ends.

Korean text "승리!" carved/painted directly onto the stone surface in a bold, thick,
rounded hand-painted display font, cream-white fill with a thick dark-brown outline
and soft drop shadow, slightly larger and more ornate than a regular button — matching
the logo lettering style. Leave room to the left for a player-name insert.
```

**panel_draw.png** — 위와 동일하되 텍스트는 "무승부!", 장식은 색종이/꽃 대신 살짝 차분한
느낌(양쪽에 작은 물음표 두 개를 마주보게 배치)으로.

**scoreboard_panel.png**
```
(어두운 안내판 블록, 가로로 넓고 중앙에 구분선이 있는 형태)

A wide dark wood-plank scoreboard nameplate with a thick bark-textured log border and
bone accents at both ends, split into two equal halves by a vertical carved wooden
divider in the middle. Each half is empty, meant to hold a player label and a large
score number on top later. Transparent background, matches the game's prehistoric
party-game UI style.
```

**crown_icon.png**
```
A small cartoon prehistoric "champion" crown icon: a simple crown shape woven from
braided vines and small leaves, decorated with three round cream bone studs like
jewels along the front. Thick brown outline, warm saturated colors, transparent
background, matches the game's prehistoric party-game UI style — playful, not
regal/realistic.
```

**button_replay.png**
```
(패널/버튼 공통 블록)

Korean text "다시하기" carved into the stone, same lettering style as the logo. Small
icon to the left: a simple circular arrow (refresh/replay symbol) carved into the
stone.
```

**button_main_menu.png**
```
(패널/버튼 공통 블록)

Korean text "메인으로" carved into the stone, same lettering style as the logo. Small
icon to the left: a simple hut icon (matching the huts in the start-screen background),
signaling "go back home".
```

## 8. 요약 — 전체 신규 에셋 개수

| 화면 | 신규 파일 수 |
|---|---|
| Hub 시작 화면(버튼 교체) | 2 |
| 멀티플레이 연결 화면 | 8 (공용 뒤로가기 버튼 포함) |
| 게임 방법 화면 | 3 (아이콘 없이 패널 + 페이지 화살표 2, 뒤로가기는 재사용) |
| 설정 화면 | 6 (패널은 게임방법 패널 재사용) |
| 최종 결과 화면 | 6 |
| **합계** | **25장** |

## 9. TODO

- [x] 게임 방법 화면 구조 — **"텍스트만 페이지별로 넘기기"로 확정.** 아이콘/삽화 없음.
- [x] 설정 화면 옵션 — **음악/효과음 on-off + 카메라·마이크 장치 선택 포함으로 확정.**
      실제로 장치 선택 드롭다운이 무엇을 제어하는지는 vision-server 번들링 작업
      (`배포_아키텍처_설계.md` 1장)이 끝나야 확정되지만, UI 자체는 지금 만들어도 된다 —
      값을 어디 저장/전달할지만 나중에 연결하면 됨.
- [ ] 위 프롬프트로 생성한 결과물의 배경 제거(투명 PNG화), 캔버스 크기 통일은 기존
      워크플로(포토샵 보정 → `image/screens/...`에 반영 → 필요한 것만 `Resources`로
      임포트) 그대로 따를 것
