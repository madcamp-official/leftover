# pc-game

Unity `6000.5.5f1` 기반의 2D 본게임입니다. MediaPipe 입력을 받아 7개 미니게임을 진행하고,
로딩·캘리브레이션, LAN 호스트/클라이언트, 결과 화면, 효과음과 BGM을 관리합니다.

전체 실행법은 [루트 README](../README.md), 입력 프로토콜은
[shared/PROTOCOL.md](../shared/PROTOCOL.md)를 먼저 참고하세요.

## 씬과 진행 순서

- `Assets/Scenes/Hub.unity`: 시작, 솔로/네트워크 선택, 최종 결과
- `Assets/Scenes/StoneThrow.unity`: 돌던지기
- `Assets/Scenes/FruitJump.unity`: 점프해서 과일 따기
- `Assets/Scenes/CoconutCrack.unity`: 머리로 코코넛 깨기
- `Assets/Scenes/StoneOrBanana.unity`: 돌 or 바나나
- `Assets/Scenes/StaringContest.unity`: 눈빛싸움
- `Assets/Scenes/ScreamDuel.unity`: 소리지르기
- `Assets/Scenes/FeatherFlight.unity`: 깃털날기

미니게임 순서는 `Assets/Scripts/GameFlow/MatchController.cs`의 `RoundScenes`가 유일한 기준입니다.
씬을 추가하거나 이름을 바꿀 때는 Build Settings와 이 배열을 함께 확인합니다.

## 코드 구조

```text
Assets/Scripts/
├── Common/
│   ├── PoseInputHub.cs              P1/P2 입력 상태 API
│   ├── PoseStreamReceiver.cs        UDP 9100 수신
│   ├── CameraPreviewReceiver.cs     UDP 9101 카메라 프리뷰
│   ├── LoadingScreenController.cs   로딩·팁·캘리브레이션·프리뷰 중계
│   ├── FrameAnimatedCharacter.cs    프레임 애니메이션 공용 처리
│   ├── SoloBotController.cs         혼자하기의 P2 입력 생성
│   ├── GameSfx.cs                   효과음 재생
│   └── GameBgm.cs                   씬별 루프 BGM과 페이드
├── GameFlow/
│   ├── MatchController.cs           7라운드 진행·점수·결과
│   └── HubController.cs             시작/결과/네트워크 UI
├── Network/
│   ├── NetworkSession.cs            역할·연결 상태·이벤트 구독
│   ├── GameEventChannel.cs          TCP 9200 JSON Lines 채널
│   └── NetworkTypes.cs              공용 이벤트 자료형
└── Minigames/                       게임별 판정·연출·HUD
```

미니게임 코드는 UDP나 MediaPipe를 직접 읽지 않고
`PoseInputHub.Instance.Get(PlayerId.P1/P2)`의 `PlayerPoseState`만 사용합니다. 라운드 종료 시
`MatchController.Instance.ReportRoundResult(winner)`로 결과를 보고하고
`LoadNextRound()`로 다음 씬을 요청합니다.

## 네트워크 역할

- `Offline`: 로컬 Unity가 입력·판정·연출을 모두 처리합니다.
- `Host`: 양쪽 vision-server의 입력을 받고 게임을 판정한 뒤 TCP `9200` 이벤트를 보냅니다.
- `Client`: 호스트 이벤트를 받아 장면과 HUD를 재현합니다.

현재 네트워크 이벤트가 연결된 게임은 `StoneThrow`, `FruitJump`, `CoconutCrack`,
`StoneOrBanana`, `StaringContest`, `ScreamDuel`입니다. `FeatherFlight`는 로컬 게임 루프만
있으므로 LAN 2대 동기화 작업이 남아 있습니다.

## 로딩 화면 편집

로딩 화면은 별도 씬이 아니라 다음 Resource 프리팹을 런타임에 생성합니다.

```text
Assets/Resources/Loading/LoadingScreenCanvas.prefab
```

Prefab Mode에서 `LoadingRoot` 아래 요소를 편집합니다. 주요 오브젝트는 다음과 같습니다.

- 가운데 게임 안내판: `MessagePanel`
- 우측 카메라 영역: `CameraPreview`
- P1/P2 준비 배지: `P1Status`, `P2Status`
- 하단 팁: `TipText`
- 로딩 링: `LoadingRing`

오브젝트 이름은 `LoadingScreenController`가 검색하므로 바꾸지 않는 편이 안전합니다. 랜덤
배경은 `Assets/Resources/Loading/loading_*.png`, 팁 문장은
`Assets/Resources/Loading/tips.txt`에서 읽습니다.

## 씬과 프리팹 편집

- 각 게임의 배경·캐릭터·소품·HUD는 씬의 `EditableLayout` 아래에서 조정합니다.
- 시작 화면은 `Assets/Prefabs/StartScreenCanvas.prefab`을 편집합니다.
- 관절형 공용 원본은 `Assets/Prefabs/Caveman_P1.prefab`, `Caveman_P2.prefab`이지만,
  현재 여러 게임은 `Assets/Resources/Characters/`의 완성 프레임 애니메이션을 사용합니다.
- `Tools > Uga Uga > Rebuild All Editable Scene Layouts...`는 기본 배치를 재생성하므로 수동
  배치를 덮어쓸 수 있습니다. 복구가 필요한 경우에만 사용합니다.

## 오디오

- 런타임 효과음: `Assets/Resources/Audio/*.mp3`
- 런타임 BGM: `Assets/Resources/Audio/BGM/*.mp3`
- 원본: `../audio/`
- 재생 코드: `Assets/Scripts/Common/GameSfx.cs`, `GameBgm.cs`

`GameBgm`은 씬 전환 중에도 살아 있는 `DontDestroyOnLoad` 오브젝트이며 같은 곡을 쓰는 씬
사이에서는 재시작하지 않습니다. 음악 사용 여부와 음량은 `PlayerPrefs`에 저장됩니다.

## 단독 씬 테스트

각 미니게임은 `GameBootstrap`이 입력·매치 싱글턴을 보완하므로 Hub를 거치지 않고도 Play할
수 있습니다. 다만 실제 카메라 테스트 전에 Hierarchy의 테스트용 `DebugPoseController`
오브젝트가 활성화되어 있지 않은지 확인하세요. 활성 상태면 실제 UDP 입력을 가짜 자세로
덮어쓸 수 있습니다.
