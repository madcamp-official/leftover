# pc-game

Unity 프로젝트 (2D). 우가우가게임 미니게임 6종 + 진행 관리. 전체 개요는
[../docs/게임_구동방식_정리.md](../docs/게임_구동방식_정리.md), 기획은
[../docs/우가우가게임_기획_프롬프트.md](../docs/우가우가게임_기획_프롬프트.md) 참고.

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
                  CavemanSilhouette(씬/프리팹 캐릭터 리깅), GameBootstrap
  GameFlow/     - MatchController(6판 진행/점수), HubController(시작/결과 화면)
  Minigames/    - 게임팀 담당. 완성된 미니게임 6종의 판정/연출/HUD
```

게임팀은 `PoseInputHub.Instance.Get(PlayerId.P1 또는 P2)`가 리턴하는 `PlayerPoseState`의
public 메서드(`IsHandRaised`, `HeadTiltRatio`, `IsMouthOpen`, `IsEyeClosedNow`,
`HandToHeadDistance`, `Joints.*`)만 보고 작업하면 되고, UDP나 vision-server의 존재를 몰라도
된다. 라운드가 끝나면 `MatchController.Instance.ReportRoundResult(winner)` →
`MatchController.Instance.LoadNextRound()` 두 줄만 호출하면 다음 미니게임으로 자동 전환된다
(`StaringContestGame.cs`가 이 패턴의 예시).

`MatchController`의 모든 씬 이동은 `SceneFadeTransition`을 거친다. 검은 화면으로 0.45초
fade out한 뒤 새 씬을 로드하고 0.45초 fade in하며, 전환 중 중복 클릭도 차단한다.

## 실행

[../docs/게임_구동방식_정리.md](../docs/게임_구동방식_정리.md) "6. 전체 실행 절차 요약" 참고 - 요약하면 vision-server를
켜고 Unity에서 `Hub` 씬을 Play.

## 에디터에서 화면 편집

- 각 미니게임 씬의 `EditableLayout` 아래에 배경, 캐릭터 프리팹 인스턴스, 정적 소품, Canvas가
  저장되어 있다. Play 모드가 아닐 때 이동/크기/앵커를 바꾸고 씬을 저장하면 유지된다.
- 캐릭터 공통 원본은 `Assets/Prefabs/Caveman_P1.prefab`, `Caveman_P2.prefab`이다.
  Prefab Mode에서 어깨/팔꿈치 피벗과 파츠를 조정하면 5개 전신 게임에 함께 반영된다.
- Hub는 `Assets/Prefabs/StartScreenCanvas.prefab`, 게임 HUD는 각 씬의 Canvas를 직접 편집한다.
  Hub와 6개 미니게임 Canvas는 모두 카메라 프레임 크기의 World Space로 저장돼 Scene 창에서
  배경/캐릭터/UI가 같은 크기 기준으로 보인다.
- `Tools > Uga Uga > Rebuild All Editable Scene Layouts...`는 기본 레이아웃 전체 복구용이다.
  실행하면 `EditableLayout`의 수동 수정이 덮어써지므로 평소에는 누르지 않는다.

자세한 단계별 강좌는 [../docs/UNITY_EDITOR_편집_가이드.md](../docs/UNITY_EDITOR_편집_가이드.md)를 참고한다.
