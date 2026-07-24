# pc-game

메인 게임: 보스전, 3D 캐릭터/애니메이션, 웹캠 비전 연동, 판정/데미지 처리, UI.
담당: **B (게임/비주얼)** — 기획서 6장 역할 분담 참고.

## 프로젝트 생성 (아직 안 만들어짐 — Unity Editor 필요)

1. Unity Hub 실행 → **Installs** 탭에서 Editor 버전 설치 (2022 LTS 권장, 팀 공용 버전으로 고정)
2. Unity Hub → **Projects** → **New Project**
3. 템플릿: **3D (URP)** 선택 — 이펙트/조명 퀄리티가 데모에서 중요하므로 URP 권장
4. 프로젝트 이름: `pc-game`, 위치: 이 폴더(`leftover/pc-game`) **바로 안**에 생성
   (즉 `leftover/pc-game/Assets`, `leftover/pc-game/ProjectSettings` 형태가 되어야 함)
5. Build Settings에서 플랫폼은 **PC, Mac & Linux Standalone** 유지

## 프로젝트 생성 후 할 일

1. `.gitignore`는 Unity 프로젝트 생성 시 Unity Hub가 자동으로 안 만들어주면
   [Unity 공식 .gitignore 템플릿](https://github.com/github/gitignore/blob/main/Unity.gitignore) 추가
   (`Library/`, `Temp/`, `Obj/`, `Build/` 등 커밋되지 않게)
2. `Assets/Scripts/Network/` 폴더 만들고 [../shared/unity-reference/TimeSyncClient.cs](../shared/unity-reference/TimeSyncClient.cs),
   [SensorReceiver.cs](../shared/unity-reference/SensorReceiver.cs) 복사
3. 빈 GameObject(`NetworkManager` 등)에 두 스크립트 붙이고 `phoneIp` 인스펙터에서 설정

## 참고 문서

- 통신 프로토콜: [../shared/PROTOCOL.md](../shared/PROTOCOL.md)
- 전체 가이드: [../GETTING_STARTED.md](../GETTING_STARTED.md)
