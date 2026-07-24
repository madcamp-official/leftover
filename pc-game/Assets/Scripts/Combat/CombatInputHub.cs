// 게임 로직이 실제로 참조하는 단일 창구.
// KeyboardInputProvider / NetworkInputProvider는 여기로만 이벤트를 쏘고,
// CombatController 같은 게임 로직은 어느 쪽이 활성인지 몰라도 된다.
// 입력 소스를 갈아끼워도(키보드 -> MediaPipe) 게임 로직은 건드릴 필요 없음.

using System;
using UnityEngine;

public enum LateralPosition { Center, Left, Right }

public class CombatInputHub : MonoBehaviour
{
    public static CombatInputHub Instance { get; private set; }

    // 검/방패 — 순간적으로 한 번 발생하는 동작
    public event Action OnSwingHorizontal;
    public event Action OnSwingVertical;
    public event Action OnKick;
    public event Action OnParry;

    // 방어/회피 — 유지되는 상태
    public bool IsGuarding { get; private set; }
    public bool IsCrouching { get; private set; }
    public LateralPosition LateralPosition { get; private set; } = LateralPosition.Center;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RaiseSwingHorizontal() { Debug.Log("[CombatInput] SwingHorizontal"); OnSwingHorizontal?.Invoke(); }
    public void RaiseSwingVertical() { Debug.Log("[CombatInput] SwingVertical"); OnSwingVertical?.Invoke(); }
    public void RaiseKick() { Debug.Log("[CombatInput] Kick"); OnKick?.Invoke(); }
    public void RaiseParry() { Debug.Log("[CombatInput] Parry"); OnParry?.Invoke(); }

    public void SetGuarding(bool active)
    {
        if (IsGuarding == active) return;
        IsGuarding = active;
        Debug.Log($"[CombatInput] Guarding={active}");
    }

    public void SetCrouching(bool active)
    {
        if (IsCrouching == active) return;
        IsCrouching = active;
        Debug.Log($"[CombatInput] Crouching={active}");
    }

    public void SetLateralPosition(LateralPosition position)
    {
        if (LateralPosition == position) return;
        LateralPosition = position;
        Debug.Log($"[CombatInput] LateralPosition={position}");
    }
}
