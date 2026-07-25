// Phase 1 임시 입력 소스 — 카메라 없이 게임 로직/밸런스를 테스트할 때 쓴다.
// 씬에 CombatInputHub와 함께 붙여서 사용하며, NetworkInputProvider(MediaPipe)와
// 동시에 켜둬도 된다: 유지형 상태(방어/앉기/좌우)는 이 컴포넌트가 "자기가 마지막으로
// 보낸 값"만 기준으로 변화가 있을 때만 Hub를 건드리므로, 키보드를 아예 안 만지는 동안은
// NetworkInputProvider가 세팅한 상태를 매 프레임 false로 되돌리지 않는다.
//
// 키맵:
//   J = 가로 베기   K = 세로 베기   L = 발차기
//   Space(누르고 있기) = 기본 방어   F = 패링
//   S(누르고 있기) = 앉기   A/D(누르고 있기) = 좌/우 이동

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class KeyboardInputProvider : MonoBehaviour
{
    [Header("검 (일회성 트리거)")]
    public KeyCode swingHorizontalKey = KeyCode.J;
    public KeyCode swingVerticalKey = KeyCode.K;
    public KeyCode kickKey = KeyCode.L;

    [Header("방패 (패링은 일회성, 방어는 유지형)")]
    public KeyCode guardKey = KeyCode.Space;
    public KeyCode parryKey = KeyCode.F;

    [Header("회피 (유지형)")]
    public KeyCode crouchKey = KeyCode.S;
    public KeyCode leftKey = KeyCode.A;
    public KeyCode rightKey = KeyCode.D;

    private CombatInputHub _hub;

    // 이 프로바이더가 마지막으로 Hub에 보낸 값. Hub의 현재 값(다른 프로바이더가 바꿨을
    // 수도 있음)이 아니라 이 값과 비교해야 다른 입력 소스와 공존할 수 있다.
    private bool _lastGuarding;
    private bool _lastCrouching;
    private LateralPosition _lastLateral = LateralPosition.Center;

    private void Start() => _hub = CombatInputHub.Instance;

    private void Update()
    {
        if (_hub == null) return;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.jKey.wasPressedThisFrame) _hub.RaiseSwingHorizontal();
        if (keyboard.kKey.wasPressedThisFrame) _hub.RaiseSwingVertical();
        if (keyboard.lKey.wasPressedThisFrame) _hub.RaiseKick();
        if (keyboard.fKey.wasPressedThisFrame) _hub.RaiseParry();

        bool guarding = keyboard.spaceKey.isPressed;
        bool crouching = keyboard.sKey.isPressed;
        bool left = keyboard.aKey.isPressed;
        bool right = keyboard.dKey.isPressed;
#else
        if (Input.GetKeyDown(swingHorizontalKey)) _hub.RaiseSwingHorizontal();
        if (Input.GetKeyDown(swingVerticalKey)) _hub.RaiseSwingVertical();
        if (Input.GetKeyDown(kickKey)) _hub.RaiseKick();
        if (Input.GetKeyDown(parryKey)) _hub.RaiseParry();

        bool guarding = Input.GetKey(guardKey);
        bool crouching = Input.GetKey(crouchKey);
        bool left = Input.GetKey(leftKey);
        bool right = Input.GetKey(rightKey);
#endif
        if (guarding != _lastGuarding)
        {
            _lastGuarding = guarding;
            _hub.SetGuarding(guarding);
        }

        if (crouching != _lastCrouching)
        {
            _lastCrouching = crouching;
            _hub.SetCrouching(crouching);
        }

        LateralPosition pos = left && !right ? LateralPosition.Left
                             : right && !left ? LateralPosition.Right
                             : LateralPosition.Center;
        if (pos != _lastLateral)
        {
            _lastLateral = pos;
            _hub.SetLateralPosition(pos);
        }
    }
}
