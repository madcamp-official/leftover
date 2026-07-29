# Unity Editor 수동 편집 가이드

이 프로젝트는 배경·캐릭터·정적 소품·HUD를 씬/프리팹에 저장한다. 따라서 **Play 버튼이 파란색이
아닌 Edit Mode에서** 수정하고 `Cmd+S`(Windows: `Ctrl+S`)로 저장하면 다음 실행에도 유지된다.
날아가는 돌/바나나, 코코넛 파편, 다가오는 포즈 벽처럼 한 번 쓰고 사라지는 연출만 실행 중 생성된다.

## 1. 안전한 작업 순서

1. Project 창에서 `Assets/Scenes`를 열고 수정할 씬을 더블클릭한다.
2. Play가 꺼져 있는지 확인한다. Play 중 Inspector 변경은 테스트용이며 종료하면 원복될 수 있다.
3. Hierarchy에서 `EditableLayout`을 펼친다.
4. 작은 변경 하나를 하고 `Cmd+S`로 저장한다.
5. Play로 확인하고, 다시 Play를 끈 뒤 다음 변경을 한다.

`Tools > Uga Uga > Rebuild All Editable Scene Layouts...`는 초기 레이아웃 전체 복구 도구다.
누르면 5개 씬의 `EditableLayout` 수동 수정이 기본값으로 덮어써진다. 평소 편집에는 사용하지 않는다.

## 2. Hub 버튼과 로고 수정

1. Project 창에서 `Assets/Prefabs/StartScreenCanvas.prefab`을 더블클릭해 Prefab Mode로 들어간다.
2. `Background`, `Logo`, `ButtonGameStart`, `ButtonHowToPlay`, `ButtonSettings`, `ButtonExit` 중 하나를 고른다.
3. Rect Tool(단축키 `T`)로 이동·크기 변경을 한다.
4. Inspector의 `Rect Transform`에서 다음 값을 주로 쓴다.
   - `Anchor Presets`: 화면 크기가 달라져도 어느 모서리/중앙을 기준으로 붙을지 결정한다.
   - `Pos X / Pos Y`: 앵커 기준 위치다.
   - `Width / Height`: UI 크기다.
   - `Pivot`: 회전·크기 변경 기준점이다. 보통 중앙 `(0.5, 0.5)`이 편하다.
5. 이미지 비율이 찌그러지면 `Image > Preserve Aspect`를 켠다.
6. Prefab Mode를 나가면 Hub 씬 인스턴스에 자동 반영된다.

Hub 씬에 배치된 시작 화면 인스턴스와 결과 화면은 미니게임 HUD와 마찬가지로 카메라 크기에
맞춘 World Space Canvas다. Hub 씬의 Canvas 루트 Position/Scale은 건드리지 말고, 프리팹 안의
Background/Logo/Button Rect Transform을 조절한다.

Hub의 `Background`에는 `CameraBackgroundImageFitter`가 붙어 있다. 미니게임 배경과 똑같이
원본 비율을 유지한 cover 방식으로 카메라 프레임 전체를 덮기 때문에 화면비가 달라져도 빈 공간이
생기지 않는다. Background의 Rect Transform 크기는 자동 계산되므로 배경 구도는 원본 PNG에서
조절하고, 로고와 버튼 위치는 각각의 Rect Transform으로 수정한다.

최종 결과 화면은 Hub 씬의 `ResultScreenCanvas`에 별도로 저장돼 있다. 평소 비활성 상태이므로
Hierarchy 체크박스로 잠시 켜서 `ResultPanel`, `ResultText`, `RestartButton`을 편집한 뒤 다시
꺼서 저장한다.

버튼 이름과 `Button` 컴포넌트는 삭제하지 않는 것이 안전하다. 위치와 이미지, Transition 색상은 자유롭게
바꿔도 된다. Hub의 버튼 클릭 참조는 `HubController` Inspector에서 확인할 수 있다.

## 3. 캐릭터 관절과 파츠 수정

공통 캐릭터 원본은 `Assets/Prefabs/Caveman_P1.prefab`과 `Caveman_P2.prefab`이다. 원본을
고치면 FruitJump, CoconutCrack 씬에 한꺼번에 반영된다.

Hierarchy 구조의 핵심은 다음과 같다.

```text
Caveman_P1/P2
├── Head, Torso, 다리 파츠
├── LeftShoulderPivot
│   └── LeftUpperArm
├── LeftElbowPivot
│   └── LeftLowerArmHand
├── RightShoulderPivot
│   └── RightUpperArm
└── RightElbowPivot
    └── RightLowerArmHand
```

관절 편집 요령:

1. 프리팹을 더블클릭하고 Scene 창을 2D 모드로 둔다.
2. 어깨 위치를 바꾸려면 `Left/RightShoulderPivot`의 Position을 이동한다.
3. 팔꿈치 기준점을 바꾸려면 `Left/RightElbowPivot`을 이동한다.
4. 상완/하완 이미지는 피벗의 자식이다. 파츠의 **위쪽 끝이 부모 피벗에 닿도록** 위치시킨다.
5. 파츠 길이를 바꾸려면 해당 파츠 Scale을 조절하고, 팔꿈치 피벗도 새 상완 끝으로 옮긴다.
6. `CavemanSilhouette` 컴포넌트의 9개 참조가 `None`이 아닌지 확인한다.

포즈 추적 중 코드는 어깨/팔꿈치 피벗을 회전시키고 팔꿈치 위치를 상완 길이에 맞춰 갱신한다.
파츠 GameObject 이름 변경은 가능하지만, 컴포넌트 참조를 끊지 않도록 한다. P1/P2의 몸 비율을
다르게 할 수도 있지만 공정한 시각 피드백을 위해 전체 높이는 비슷하게 유지하는 편이 좋다.

한 씬에서만 캐릭터를 다르게 만들려면 Hierarchy의 캐릭터를 선택하고 우클릭한 뒤
`Prefab > Unpack Completely`를 선택한다. 그 인스턴스는 이후 공통 프리팹 변경을 받지 않는다.

## 4. 미니게임별 오브젝트 편집

각 씬에서 `EditableLayout` 아래를 수정한다.

| 씬 | 주요 편집 대상 |
|---|---|
| StoneThrow | `Background`, P1/P2 캐릭터, `StoneThrowHud` |
| FruitJump | 캐릭터, `P1/P2 Fruits`의 3단 과일, `FruitJumpHud` |
| CoconutCrack | 캐릭터, StoneTable, 캐릭터 자식 Coconut, HUD |
| StoneOrBanana | 캐릭터, 캐릭터 자식 CoverBush, HUD/턴 안내판 |
| StaringContest | P1/P2 Head, StareClash, HUD/위험 게이지/규칙 배너 |

월드 오브젝트는 Move(`W`), Rotate(`E`), Scale(`R`) 또는 Rect가 아닌 Transform Inspector로
조정한다. `SpriteRenderer > Order in Layer` 기본 순서는 배경 `-100`, 테이블 `-1`, 캐릭터
`-1~2`, 수풀/코코넛 `3`, 순간 투사체 `5`다. 오브젝트가 뒤에 가려지면 이 값을 확인한다.

배경은 카메라를 덮도록 미리 Scale이 설정돼 있다. Camera의 `Orthographic Size`를 바꾸면 보이는
월드 범위가 달라지므로 배경 Scale도 함께 확인한다. 캐릭터 배치만 바꿀 때는 Camera를 먼저 고정한다.

### Scene 창의 서로 다른 화면 크기와 흰색 사각형

모든 씬의 최종 출력 기준과 UI 기준 해상도는 16:9, `2048 × 1152`로 같으며, Main Camera의
`Orthographic Size`도 **5**로 통일돼 있다. 즉, 모든 씬이 세로 10 월드 유닛을 동일하게 보여 준다.
이 값은 카메라가 세로로 보여 주는 월드 높이의 절반이다.

| Orthographic Size | 씬 | 의미 |
|---:|---|---|
| 5 | Hub와 미니게임 5개 전체 | 세로 10 월드 유닛을 보는 공통 구도 |

Scene 창에서 보이는 **배경 이미지의 사각형**은 SpriteRenderer에 배치된 PNG 자체의 실제 영역이다.
그 바깥 또는 안쪽에 보이는 **흰색 선 사각형**은 Main Camera가 실제 게임에 출력하는 영역이다.
즉, 배경 테두리보다 흰 카메라 선을 최종 화면 기준으로 삼는다. 각 미니게임의
`CameraBackgroundFitter`와 Hub의 `CameraBackgroundImageFitter`가 현재 화면비에 맞춰 배경을
자동 확대하므로 흰 선 전체가 항상 덮인다.
원본 비율은 유지하며, 화면비가 다른 경우 배경의 남는 위·아래 또는 좌·우 부분만 카메라 밖으로 잘린다.

Game 창의 비율이 `Free Aspect`이면 창 모양에 따라 흰 카메라 사각형 비율도 계속 바뀌므로 16:9
배경 테두리가 흰 선보다 크게 보일 수 있지만 실제 Game 화면에는 빈 공간이 생기지 않는다. 정확한 16:9
구도를 다듬을 때는 Game 탭 왼쪽 위 비율 메뉴를 `16:9` 또는 `1920 × 1080`으로 고정한다.
Scene 창의 줌은 단순한 편집용 확대라 결과 화면 크기와 관계없다.
카메라 값은 직접 바꾸지 말고 캐릭터·소품의 Transform을 조절해 구도를 다듬는다. 배경과 HUD Canvas는
공통 카메라 크기에 맞춰져 있으므로 Canvas 루트 Scale 대신 자식 UI의 Rect Transform을 수정한다.

## 5. HUD 크기와 배치 수정

각 HUD Canvas는 `Render Mode = World Space`, 기준 해상도 `2048 × 1152`이며 카메라 높이에
맞는 Scale이 자동 설정돼 있다. 따라서 Scene 창에서도 배경·캐릭터·UI가 같은 카메라 프레임
안에 실제 크기로 보인다. Game 창을 16:9로 두고 작업하는 것이 가장 예측 가능하다. Canvas
루트의 Position/Scale은 카메라 정렬값이므로, UI 크기는 루트 Scale이 아니라 아래 패널들의
Rect Transform으로 조절한다.

1. HUD Canvas를 펼쳐 `P1Plate`, `P2Plate`, `TimerPlate`, `EventText` 등을 선택한다.
2. Rect Tool로 이동하고 모서리 핸들로 크기를 바꾼다.
3. 좌측 HUD는 왼쪽 위, 우측 HUD는 오른쪽 위, 타이머는 위 중앙 앵커를 유지하면 해상도 변화에 강하다.
4. 숫자 Text를 선택해 Font Size, Alignment, Color, Outline을 조절한다.
5. StaringContest의 Gauge는 `Image Type = Filled`, `Fill Method = Horizontal`을 유지한다.
6. StoneOrBanana의 ThrowPrompt/ReceivePrompt는 런타임에 활성/비활성만 바뀐다. 편집 중 보려면
   Hierarchy 체크박스로 잠시 켜고 배치한 뒤 두 오브젝트를 다시 꺼서 저장한다.

Canvas 전체 Transform Scale로 UI를 줄이기보다 각 패널의 Rect Transform을 조절하는 편이 좋다.

### 공용 타이머

미니게임 5개 씬의 `TimerPlate`는 모두 `Assets/Resources/UI/time_remaining.png`를 사용한다.

라운드별 승패를 아이콘으로 보여주는 매치 결과판(`MatchScoreboard`, `MatchScoreboardHud.cs`)은
화면에 띄우지 않기로 결정해 5개 씬 전부에서 제거했다 — 이제 각 HUD Canvas 아래에 해당
오브젝트가 없다. 관련 컴포넌트와 씬 생성 코드(`EditableSceneLayoutBuilder`,
`StoneOrBananaSceneSetup`)의 호출부도 같이 삭제했으므로, 새로 씬을 만들 때 이 결과판을 다시
추가할 필요는 없다.

## 6. 이미지 교체

Unity가 실제 사용하는 PNG는 `Assets/Resources` 아래에 있다. 원본 작업 파일은 저장소 루트의
`image` 폴더에 있다.

1. 새 PNG를 같은 Resources 경로에 넣거나 기존 파일을 교체한다.
2. Project 창에서 PNG를 선택하고 `Texture Type = Sprite (2D and UI)`인지 확인한다.
3. 투명 배경이면 `Alpha Is Transparency`를 켠다.
4. SpriteRenderer 또는 UI Image의 `Sprite` 슬롯에 새 스프라이트를 드래그한다.
5. 캐릭터 파츠는 Pixels Per Unit과 전체 비율 차이가 크게 나면 프리팹에서 Scale을 다시 맞춘다.

파일을 교체할 때 `.meta` 파일은 삭제하지 않아야 기존 씬 참조가 유지된다.

## 7. 게임 규칙 숫자 조정

각 씬의 `GameController`를 선택하면 제한시간, 임계값, 이동 속도 같은 public 값이 Inspector에 보인다.
예를 들어 StoneThrow의 `Fire Interval Seconds`, FruitJump의 Tier 배열, CoconutCrack의
Hit/Release Distance, StaringContest의 EAR 값을 조절할 수 있다.

한 번에 큰 폭으로 바꾸지 말고 10~20%씩 변경해 실제 카메라 환경에서 비교한다. Play 중 찾은 좋은
값은 메모한 뒤 Play를 끄고 Edit Mode에서 다시 입력해 저장한다.

## 8. 저장·되돌리기·문제 해결

- Scene 이름 옆 `*`는 저장되지 않은 변경이 있다는 뜻이다. `Cmd+S`로 저장한다.
- 프리팹 인스턴스의 굵은 Inspector 값은 Override다. `Overrides` 메뉴에서 Apply 또는 Revert한다.
- 캐릭터가 안 움직이면 GameController의 P1/P2 Silhouette 참조와 CavemanSilhouette의 관절 참조를 본다.
- UI가 클릭되지 않으면 Hub 씬에 `EventSystem`과 `InputSystemUIInputModule`이 있는지 확인한다.
- 이미지가 안 보이면 Sprite 참조, GameObject Active, SpriteRenderer Order, Camera 범위를 차례로 본다.
- Console의 빨간 오류를 먼저 해결한다. 첫 번째 오류가 뒤의 연쇄 오류 원인인 경우가 많다.
- 배치가 완전히 망가졌고 Git으로 복구할 수 없을 때만 전체 Rebuild 메뉴를 사용한다.

권장 품질 개선 순서는 `카메라/배경 → 캐릭터 관절 → 큰 소품 → HUD 패널 → 텍스트 → 판정값`이다.
각 단계마다 16:9 Game 창 스크린샷을 남기면 전후 비교가 쉽다.

## 9. 씬 전환 페이드

게임 시작, 미니게임 간 이동, 최종 Hub 복귀는 `SceneFadeTransition`이 공통 처리한다. 기본값은
검은 화면으로 0.45초 fade out 후 새 씬을 로드하고, 완전한 검정을 최소 한 프레임+0.08초
유지한 다음 0.45초 fade in한다. 따라서 새 씬은 항상 검은 화면에서 서서히 등장한다. 전환 오버레이는 실행
중에만 생성되는 `DontDestroyOnLoad` 오브젝트라 씬 배치 편집 대상이 아니다. 속도를 바꾸려면
`SceneFadeTransition.cs`의 `fadeOutSeconds`, `blackHoldSeconds`, `fadeInSeconds` 기본값을 조절한다.
