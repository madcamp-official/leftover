# Common 입력·런타임 계층

미니게임과 Python 입력 파이프라인 사이의 공용 계약입니다. 게임 코드는 UDP 패킷이나
MediaPipe 자료형을 직접 다루지 않고 `PoseInputHub`의 상태만 읽습니다.

## 입력 흐름

```text
vision-server
  └─ UDP 9100 JSON
       └─ PoseStreamReceiver
            └─ PoseInputHub.ApplyFrame(...)
                 └─ PlayerPoseState(P1/P2)
                      └─ 각 Minigame Game 스크립트
```

카메라 미리보기는 별도 UDP `9101`을 `CameraPreviewReceiver`가 받습니다. 네트워크
클라이언트에서는 호스트가 TCP `9200`으로 중계한 JPEG를 같은 수신기에 주입합니다.

## 미니게임에서 사용하는 API

```csharp
PlayerPoseState p1 = PoseInputHub.Instance?.Get(PlayerId.P1);
PlayerPoseState p2 = PoseInputHub.Instance?.Get(PlayerId.P2);
```

주요 값과 메서드는 다음과 같습니다.

- `IsTracked`: 최근 포즈 패킷이 유효한지
- `Joints`: 코·어깨·팔꿈치·손목·엉덩이·무릎·발목 좌표
- `IsHandRaised(...)`: 손 들기 판정
- `HandToHeadDistance(...)`: 코코넛 타격 왕복 판정
- `HeadTiltRatio`: 돌 or 바나나 회피
- `MouthOpenRatio`, `IsMouthOpen(...)`: 먹기 동작
- `EyeAspectRatio`, `IsEyeClosedNow(...)`: 눈빛싸움
- `VoiceLevel`, `IsVoiceTracked`: 소리지르기

정확한 와이어 필드는 [shared/PROTOCOL.md](../../../../shared/PROTOCOL.md)를 참고하세요.

## 공용 런타임

- `GameBootstrap`: 미니게임 씬 단독 실행 시 입력·매치·네트워크 싱글턴 생성
- `LoadingScreenController`: 씬 사이 랜덤 배경·팁·캘리브레이션
- `FrameAnimatedCharacter`: 완성 PNG 프레임 애니메이션
- `SceneFadeTransition`: 중복 로드를 막는 페이드 씬 전환
- `SoloBotController`: 혼자하기에서 P2 상태 생성
- `GameSfx`, `GameBgm`: 효과음·배경음악

`DebugPoseController`는 테스트 전용입니다. 실제 vision-server를 사용할 때 활성화하면 매
프레임 가짜 자세가 실제 입력을 덮어쓸 수 있으므로 씬에서 비활성화하거나 제거합니다.

