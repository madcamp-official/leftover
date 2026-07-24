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

## 보스전 프로토타입

`Assets/Scenes/SampleScene.unity`를 열고 Play를 누르면 런타임에 1대1 보스전
아레나가 자동으로 구성된다. 센서 연동 전에는 아래 키로 전투 루프를 검증한다.

| 키 | 동작 |
|---|---|
| `J` | 가로 베기 |
| `K` | 세로 베기 |
| `L` | 발차기 |
| `Space` 길게 누르기 | 기본 방어 (게이지 1.0) |
| `F` | 패링 (게이지 0.5, 짧은 판정 윈도우) |
| `S` | 앉아서 가로 공격 회피 |
| `A` / `D` | 좌우로 세로 공격·발차기 회피 |
| `R` | 라운드 재시작 |

상대는 가로/세로 공격을 주황색 문구로 예고하고, 플레이어 공격에 확률적으로
방어·패링·맞는 방향의 회피로 대응한다. 가로베기, 세로베기, 발차기, 방어,
패링, 회피와 동시 타격은 각각 다른 검광·충격파·스파크·배리어 효과를 여러 겹
재생한다. 공격 종류와 상대의 방어 결과에 따라서도 색상, 방향, 카메라 흔들림과
사운드 조합이 달라진다.
전투 시스템은 기존 `CombatInputHub` 이벤트를 사용하므로
나중에 키보드 입력을 폰 센서/비전 입력으로 교체해도 규칙 코드는 유지된다.

캐릭터는 Quaternius `Animated Knight Character`(CC0)의 갑옷 기사 모델에
`EEJANAI_Team/FreeSwordAnimations` 휴머노이드 전투 클립을 리타게팅하고,
검·방패는 Kevin Iglesias `Skeleton Animations FREE`의 장비 프리팹을
조합한다. 방패 메시 자체는 실제 왼손 뼈의 손등 소켓 자식으로 고정하고,
별도의 IK 목표가 팔꿈치와 손목을 이동시키므로 기본 방어에서 패링으로 이어질 때
방패가 손에서 분리되거나 몸을 관통하지 않는다. 검 공격은
`slash2`(가로베기)와 `slash9`(세로베기) 휴머노이드 클립을 상체에 직접
샘플링하고, 발 IK로 제자리 자세를 유지한다. 방패 팔과 발차기만 별도 IK로
제어하므로 방어 중 검이 방패를 따라 회전하지 않는다.
`Tools > Boss Duel > Audit Sword Clip Trajectories`에서 각 클립의 손 궤적을
다시 비교할 수 있다. `Tools > Boss Duel > Rebuild Asset Library`를
실행하면 Resources용 에셋 라이브러리와 전용 Animator Controller가 재생성된다.
원본 Toon 재질은 실행 시 URP/Lit으로 변환되어 현재 렌더 파이프라인에서도
정상 표시된다.

맵은 Quaternius의 `Medieval Village`와 `Simple Nature`(모두 CC0)를 섞은
중세 야외 마을로 구성한다. 기존 원형 발판, 금색/검정 링, 실내 벽은 사용하지
않으며 넓은 흙 결투장, 마을길, 목조 건물, 수목과 소품을 카메라 구도에 맞춰
배치한다. 원본 출처와 라이선스는 각 ThirdParty 폴더의 `LICENSE.txt`에
보존한다.
전투 이펙트는 Kenney `Particle Pack`(CC0)의 투명 PNG를 사용하며 라이선스는
`Assets/ThirdParty/KenneyParticlePack/License.txt`에 보존한다.
전투 사운드는 Kenney `RPG Audio`와 `Impact Sounds`(CC0)에서 선별한 검풍,
금속 충돌, 철판 타격과 중량 타격음을 최대 3개 레이어로 조합한다. 종소리와
절차적으로 생성하던 바운스/신시사이저 계열 효과음은 사용하지 않는다. 원본 라이선스는
각각 `Assets/ThirdParty/KenneyCombatAudio/RPG/License.txt`와
`Assets/ThirdParty/KenneyCombatAudio/Impact/License.txt`에 보존한다.

상대 AI는 플레이어의 공격 반복 횟수와 공격별 사용 빈도를 기억해 익숙한
공격일수록 방어/패링 확률을 높인다. 앉기 회피가 많으면 세로베기와 발차기를,
좌우 회피가 많으면 가로베기를 더 선택하며, 자신의 직전 공격은 반복 확률을
낮춘다. 체력이 줄거나 연속 압박에 성공할수록 예고와 회복 시간도 짧아진다.

### 전투 상성

- 앉기는 가로베기를 피하고 공격자를 경직시키지만, 세로베기 1.0과 발차기
  0.5 데미지를 받는다.
- 좌우이동은 세로베기를 피하고 공격자를 경직시키며 발차기도 피한다.
  가로베기에는 1.0 데미지를 받는다.
- 기본방어는 두 베기를 막는다. 발차기에는 방어가 깨져 경직되고 0.25
  데미지를 받는다.
- 패링은 두 베기와 발차기를 모두 막고 공격자를 경직시킨다.
- 검 공격끼리는 서로 1.0 데미지, 발차기끼리는 서로 0.5 데미지다.
  검과 발차기가 맞붙으면 검이 이기고 발차기 사용자가 1.0 데미지를 받는다.
- 방어 게이지 최대치는 3.0이며 방어/패링 중이 아닐 때 자동 회복한다.

### MediaPipe 연동 방향

MediaPipe가 전신 좌표를 Unity로 그대로 보내는 방식이 아니라, Python에서 동작을
분류한 뒤 UDP 9002로 의미 이벤트만 전송한다. `NetworkInputProvider`가 이를
`CombatInputHub` 이벤트로 변환하므로 키보드 테스트와 MediaPipe 입력은 동일한
게임 로직과 Animator를 사용한다.

캐릭터 애니메이션은 현실 동작을 과장해서 복제하지 않는다. 기본 전투 자세와
하체는 발 IK로 고정한다. 오른손 검은 선택된 휴머노이드 공격 클립의 손을
따라가며, 왼손 방패와 발차기만 별도 IK 타깃으로 제어한다. 가로베기와
세로베기는 손 궤적 감사 도구로 분류한 서로 다른 클립을 사용한다. 방패는
기본 상태에서도 몸 왼쪽 바깥에 유지되며, 패링은 방어 자세에서 시작해 왼팔과
방패가 함께 바깥으로 뻗었다가 돌아온다.
