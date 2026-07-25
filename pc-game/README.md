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

`Assets/Scenes/SampleScene.unity`를 열고 Play를 누르면 런타임에 평범한 낮
하늘 아래 돌바닥 중세 던전 아레나와 1대1 기사 듀얼이 자동으로 구성된다.

### 실행 방법 (웹캠 모션 인식으로 플레이)

1. `vision-server`에서 파이썬 인식 서버를 켠다 ([vision-server/README.md](../vision-server/README.md) 참고):
   ```bash
   cd vision-server
   python3 -m venv .venv && source .venv/bin/activate  # 최초 1회
   pip install -r requirements.txt                      # 최초 1회
   python main.py --pc-ip 127.0.0.1
   ```
   카메라 창이 뜨면 화면 보고 똑바로 선 뒤 `s`로 캘리브레이션 한 번 해줘야
   앉기(`crouch`) 판정이 정상 동작한다. 나머지 동작은 캘리브레이션 없이 바로 인식된다.
2. Unity에서 `Assets/Scenes/SampleScene.unity`를 열고 Play를 누른다. `BossDuelPrototype`이
   Play 시작 시 `CombatInput` 게임오브젝트에 `KeyboardInputProvider`와
   `NetworkInputProvider`(UDP 9002 수신)를 자동으로 둘 다 붙이므로, 씬을 따로 만질 필요
   없이 그대로 웹캠 앞에서 몸을 움직이면 게임이 반응한다.
   - 가로/세로 베기: 오른손을 몸통 기준으로 짧게 크게 움직이기(수평/수직 우세 방향으로 판정)
   - 발차기: 무릎을 빠르게 펴기(좌우 다리 구분 없음)
   - 기본 방어: 왼손을 가슴 높이에서 잠깐 정지
   - 패링: 왼손을 몸통 중심에서 바깥쪽으로 뻗기
   - 앉기 / 좌우 회피: 무릎 굽히기 / 고개(코)를 좌우로 기울이기
3. vision-server가 꺼져 있거나 카메라가 없으면 `NetworkInputProvider`가 UDP 소켓만
   바인딩해둔 채 조용히 대기하고, 키보드(J/K/L=가로베기·세로베기·발차기, `Space`=방어,
   `F`=패링, `S`=앉기, `A`/`D`=좌우)로 그대로 테스트할 수 있다 — 둘을 동시에 켜둬도
   서로 입력을 덮어쓰지 않는다.

키보드로 전투 루프를 검증할 때는 아래 표를 참고한다.

| 키 | 동작 |
|---|---|
| `J` | 가로 베기 |
| `K` | 세로 베기 |
| `L` | 발차기 |
| `Space` 길게 누르기 | 기본 방어 (누르고 있는 동안 게이지 초당 소모, 라인하르트 방패처럼) |
| `F` | 패링 (현재 게이지의 절반을 즉시 소모, 짧은 판정 윈도우) |
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

**가로/세로 베기 준비 동작(0.36초)은 검날 색으로도 구분된다** — 가로 베기는
호박색, 세로 베기는 보라색으로 검날이 펄스 발광하므로, 모션이 비슷해 보이는
순간에도 어떤 공격이 오는지 색으로 즉시 읽을 수 있다. 방패는 절차적 애니메이션을
다시 짜서 기본 방어 시 정면(상대 방향)으로 방패 면이 똑바로 향하고, 패링은
그 자세에서 수직축으로 빠르게 바깥으로 쳐내는 스윙 후 복귀하도록 만들었다
(`AssetShieldFollower` 참고).

캐릭터는 [Mixamo](https://www.mixamo.com)의 "Knight D Pelegrini" 캐릭터
하나를 몸/애니메이션 전부의 유일한 소스로 쓴다. 대기/방어/패링/가로·세로
베기/발차기/피격/사망 10개 상태를 전부 **이 캐릭터 위에서** 다운로드한
"Sword And Shield" 모션캡처 클립으로 재생하므로 리타기깅 오차가 전혀 없다
— 이전에는 별도 모델(CyberSoldier 등)에 다른 소스(EEJANAI/Kevin
Iglesias, 나중엔 Mixamo도) 애니메이션을 리타기팅해서 썼는데, 몸통 비례가
서로 안 맞아 모션이 어색했던 문제가 있어서 "캐릭터 메시와 애니메이션을
같은 Mixamo 소스로 통일"하는 방식으로 바꿨다. Mixamo 클립이 없는 개발
환경에서는 `BossDuelAssetLibraryBuilder`가 자동으로 예전 EEJANAI/Kevin
Iglesias 클립으로 폴백한다(콘솔에 경고 표시, 이 경우 CyberSoldier가 아니라
Knight 메시 위에 리타기팅됨).

검·방패는 전용 메시 대신 절차적 지오메트리에 발광 재질(호박/청록/보라
에너지 코어)을 입혀 에너지 무기처럼 보이게 했다 —
`BossDuelAssetLibrary.kevinSwordPrefab`/`kevinShieldPrefab`은 의도적으로
비워둬서 이 폴백 경로를 강제한다. 검/방패 자체의 움직임은 몸통 애니메이션과
무관하게 `AssetSwordFollower`/`AssetShieldFollower`가 순수 좌표 계산으로
따로 제어한다(가로/세로 베기 궤적, 방어/패링 자세).

Asset Store/Mixamo 원본은 라이선스상 저장소에 포함하지 않으므로 각
개발자가 직접 설치해야 한다. [mixamo.com](https://www.mixamo.com)에 Adobe
계정으로 로그인 → **Characters** 탭에서 "Knight"로 검색해 "Knight D
Pelegrini" 선택 → **Animations** 탭에서 "Sword And Shield"로 검색, 아래
10개 클립을 그 캐릭터가 선택된 채로 하나씩 다운로드(포맷 FBX Binary,
스킨은 첫 번째 것만 With Skin/나머지는 Without Skin이면 충분 — 애니메이션만
쓰므로 무관)해서 정확히 이 파일명으로 넣는다:
- `pc-game/Assets/Mixamo/Character/Knight D Pelegrini - Sword And Shield Idle (FighterMesh+Idle).fbx`
  (Idle, **With Skin** — 실제로 화면에 보이는 메시가 여기서 나옴)
- `pc-game/Assets/Mixamo/Animations/`에 나머지 9개(Without Skin이면 충분):
  `Knight - Sword And Shield Slash - Cross Slash (Horizontal).fbx`,
  `Knight - Sword And Shield Slash - Downward Slash (Vertical).fbx`,
  `Knight - Sword And Shield Kick - Sparta Kick (Kick).fbx`,
  `Knight - Sword And Shield Block Idle (Guard).fbx`,
  `Knight - Sword And Shield Block - Idle To Block (Parry).fbx`,
  `Knight - Sword And Shield Crouch Block Idle (DodgeCrouch).fbx`,
  `Knight - Sword And Shield Strafe - Left Walk (DodgeLeft).fbx`,
  `Knight - Sword And Shield Impact - Unblocked (Hit-Stagger).fbx`,
  `Knight - Sword And Shield Death - Falling Back (Dead).fbx`.

`Tools > Boss Duel > Audit Sword Clip Trajectories`에서 각 클립의 손 궤적을
다시 비교할 수 있다. `Tools > Boss Duel > Rebuild Asset Library`를
실행하면 Resources용 에셋 라이브러리와 전용 Animator Controller가 재생성된다.
원본 Toon 재질은 실행 시 URP/Lit으로 변환되어 현재 렌더 파이프라인에서도
정상 표시된다.

캐릭터의 발은 `GroundedFighterRig`가 IK로 지면에 고정한다. 예전에는 이
고정 위치를 게임 시작 시 딱 한 번만 스냅샷으로 찍어서 평생 고정했는데,
평상시(앉기/좌우 회피가 아닐 때)에는 매 프레임 현재 애니메이션의 발
위치를 다시 추적하도록 바꿨다 — 한 번 찍은 스냅샷이 계속 반복 재생되는
대기 애니메이션의 자연스러운 다리 움직임과 계속 어긋나면서(다리는 대기
클립대로 움직이는데 발만 옛날 위치에 고정) 다리가 붕 뜬 것처럼 보이는
문제가 있었다. 앉기/좌우 회피 중에는 여전히 그 순간의 발 위치를 고정해서
엉덩이가 그 자리에서 위아래·좌우로 움직이는 것처럼 보이게 한다. 발
**회전**은 IK로 덮어쓰지 않고 애니메이션이 그대로 재생하게 둔다 — 매 프레임
읽은 실시간 회전값을 그 자리에서 다시 IK에 먹이는 피드백 루프가 발목이
계속 도는 버그로 이어졌던 걸 고치면서, 위치만 고정하고 회전은 손대지 않는
쪽으로 단순화했다.

하늘은 씬의 기본 프로시저럴 스카이박스(평범한 파란 하늘)를 그대로 쓴다 —
한때 `SpaceSkies Free`의 성운 스카이박스로 바꿨다가 다시 원래대로 되돌렸다.
아레나는 `KenneyModularDungeon`(무료, CC0)의 돌 아치문 게이트와 절차적
스톤 바닥판/기둥/뒷벽으로 구성된 중세 던전이다 — 한때 SF 모듈러 킷으로
전부 바꿨다가, 캐릭터 모션을 먼저 완성하자는 우선순위에 맞춰 원래의
중세 구성(바닥판 포함)으로 되돌렸다. 타격 슬래시 이펙트는 `Stylized Slash
VFX`(HungNguyenVFX)의 색상별 파티클 프리팹을 쓰고, 가드/패링/임팩트
버스트는 기존 Kenney 스프라이트 시스템을 그대로 재사용한다. 전투 사운드는
`TII_SoundLibrary_3Steps`(SCI-FI, 무료)의 빔소드 생성·스위시·레이저
히트·에너지 실드 업/다운/임팩트 클립과 `Free Laser Weapons`(Daniel
SoundsGood)의 블래스트음을 최대 3개 레이어로 조합한다(사운드는 SF 톤을
그대로 유지 — 이번 피드백은 맵/캐릭터에 한정). 원본 라이선스는 각 패키지
폴더에 보존돼 있다.

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
- 방어 게이지 최대치는 3.0이다. 오버워치 라인하르트 방패처럼, 기본 방어를 들고 있는
  동안은 초당 일정량 계속 깎이고 방패를 내려야(방어/패링 중이 아닐 때만) 회복한다.
  게이지가 0이 되면 방어가 강제로 풀린다. 패링은 더 이상 고정 비용이 아니라 시전 순간의
  게이지를 절반으로 태우므로, 아껴뒀다가 쓸수록 한 번에 크게 소모된다. 다만 그 패링이
  실제로 공격을 막아내면 소모한 만큼 그대로 환불된다 — 실패(헛스윙)하면 환불 없이 그대로
  소모된 채 남는다.

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
