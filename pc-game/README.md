# pc-game

Unity 프로젝트 (2D). 우가우가게임 미니게임 6종 + 진행 관리. 전체 개요는
[../GETTING_STARTED.md](../GETTING_STARTED.md), 기획은
[../우가우가게임_기획_프롬프트.md](../우가우가게임_기획_프롬프트.md) 참고.

## 씬 구조

- `Assets/Scenes/Hub.unity` — 시작 화면 + 최종 결과 화면. 게임의 진입점.
- `Assets/Scenes/StoneThrow.unity`, `PoseCopy.unity`, `FruitJump.unity`, `CoconutCrack.unity`,
  `StoneOrBanana.unity`, `StaringContest.unity` — 미니게임 6종. `MatchController`가 이 순서로
  하나씩 로드한다.

## 스크립트 구조 (분업 경계)

```
Assets/Scripts/
  Common/       - 입력팀 담당. PoseInputHub(공용 API), PoseStreamReceiver(UDP 9100 수신),
                  EyeCloseTimer/JumpHeightCalibrator(상태 있는 판정 유틸),
                  RuntimeSpriteFactory/CavemanSilhouette(도형 캐릭터 표현), GameBootstrap
  GameFlow/     - MatchController(6판 진행/점수), HubController(시작/결과 화면)
  Minigames/    - 게임팀 담당. 게임별 폴더 분리 (StaringContest는 완전 구현 예시)
```

게임팀은 `PoseInputHub.Instance.Get(PlayerId.P1 또는 P2)`가 리턴하는 `PlayerPoseState`의
public 메서드(`IsHandRaised`, `HeadTiltRatio`, `IsMouthOpen`, `IsEyeClosedNow`,
`HandToHeadDistance`, `Joints.*`)만 보고 작업하면 되고, UDP나 vision-server의 존재를 몰라도
된다. 라운드가 끝나면 `MatchController.Instance.ReportRoundResult(winner)` →
`MatchController.Instance.LoadNextRound()` 두 줄만 호출하면 다음 미니게임으로 자동 전환된다
(`StaringContestGame.cs`가 이 패턴의 예시).

## 실행

[../GETTING_STARTED.md](../GETTING_STARTED.md) "실행 방법" 참고 - 요약하면 vision-server를
켜고 Unity에서 `Hub` 씬을 Play.

## 캐릭터 아트

별도 리소스 없이 `RuntimeSpriteFactory`가 코드로 원/캡슐 스프라이트를 생성하고,
`CavemanSilhouette`가 그걸로 머리+몸통+양손 4파츠 원시인을 조립한다 (P1=파랑, P2=빨강).
손 든 상태/머리 기울기 같은 공용 제스처는 이 컴포넌트가 자동으로 반영하므로, 미니게임
스크립트는 `silhouette.ApplyPose(state)`만 매 프레임 호출하면 된다.
