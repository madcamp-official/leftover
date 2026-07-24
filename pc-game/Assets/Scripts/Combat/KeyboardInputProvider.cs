// Phase 1 임시 입력 소스 — MediaPipe 연동 전까지 이걸로 게임 로직/밸런스를 테스트한다.
// 씬에 CombatInputHub와 함께 붙여서 사용. NetworkInputProvider와 동시에 켜두지 말 것
// (둘 다 같은 Hub를 건드리므로 입력이 꼬일 수 있음).
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

        _hub.SetGuarding(keyboard.spaceKey.isPressed);
        _hub.SetCrouching(keyboard.sKey.isPressed);

        bool left = keyboard.aKey.isPressed;
        bool right = keyboard.dKey.isPressed;
#else
        if (Input.GetKeyDown(swingHorizontalKey)) _hub.RaiseSwingHorizontal();
        if (Input.GetKeyDown(swingVerticalKey)) _hub.RaiseSwingVertical();
        if (Input.GetKeyDown(kickKey)) _hub.RaiseKick();
        if (Input.GetKeyDown(parryKey)) _hub.RaiseParry();

        _hub.SetGuarding(Input.GetKey(guardKey));
        _hub.SetCrouching(Input.GetKey(crouchKey));

        bool left = Input.GetKey(leftKey);
        bool right = Input.GetKey(rightKey);
#endif
        LateralPosition pos = left && !right ? LateralPosition.Left
                             : right && !left ? LateralPosition.Right
                             : LateralPosition.Center;
        _hub.SetLateralPosition(pos);
    }
}
