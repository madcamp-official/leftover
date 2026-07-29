using System.Collections;
using UnityEngine;

public enum StoneThrowSide { Left, Right }
public enum StoneThrowHand { Left, Right }

// 한 플레이어의 정면 또는 후면 통짜 프레임을 표시한다.
// 위치/손/목표 앵커는 모두 씬 자식 오브젝트라 Scene 창에서 직접 옮길 수 있다.
public class StoneThrowCharacterView : MonoBehaviour
{
    [Header("표시 대상")]
    [SerializeField] private PlayerId player;
    [SerializeField] private bool backView;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer characterRenderer;
    [SerializeField] private SpriteRenderer hitFaceRenderer;

    [Header("좌우 회피 위치 (Slot 기준)")]
    [SerializeField] private Transform leftDodgeAnchor;
    [SerializeField] private Transform rightDodgeAnchor;

    [Header("돌 시작점 (VisualRoot 기준)")]
    [SerializeField] private Transform leftHandReleaseAnchor;
    [SerializeField] private Transform rightHandReleaseAnchor;

    [Header("돌 목표점 (Slot 기준)")]
    [SerializeField] private Transform leftTargetAnchor;
    [SerializeField] private Transform rightTargetAnchor;

    [Header("6컷 애니메이션")]
    [SerializeField] private Sprite[] leftHandFrames;
    [SerializeField] private Sprite[] rightHandFrames;

    private Coroutine _hitFaceRoutine;

    public PlayerId Player => player;
    public bool IsBackView => backView;

    private void Awake()
    {
        ShowIdle();
        if (hitFaceRenderer != null) hitFaceRenderer.enabled = false;
    }

    public void SetSide(StoneThrowSide side)
    {
        Transform anchor = side == StoneThrowSide.Left ? leftDodgeAnchor : rightDodgeAnchor;
        if (visualRoot != null && anchor != null)
            visualRoot.localPosition = anchor.localPosition;
    }

    public void ShowThrowFrame(StoneThrowHand hand, int frameIndex)
    {
        Sprite[] frames = Frames(hand);
        if (characterRenderer == null || frames == null || frames.Length == 0) return;
        characterRenderer.sprite = frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
    }

    public void ShowIdle()
    {
        Sprite[] frames = rightHandFrames != null && rightHandFrames.Length > 0
            ? rightHandFrames : leftHandFrames;
        if (characterRenderer != null && frames != null && frames.Length > 0)
            characterRenderer.sprite = frames[0];
    }

    public int FrameCount(StoneThrowHand hand) => Frames(hand)?.Length ?? 0;

    public Vector3 ReleasePosition(StoneThrowHand hand)
    {
        Transform anchor = hand == StoneThrowHand.Left ? leftHandReleaseAnchor : rightHandReleaseAnchor;
        return anchor != null ? anchor.position : transform.position;
    }

    public Vector3 TargetPosition(StoneThrowSide side)
    {
        Transform anchor = side == StoneThrowSide.Left ? leftTargetAnchor : rightTargetAnchor;
        return anchor != null ? anchor.position : transform.position;
    }

    public void ShowHitFace(float seconds)
    {
        if (backView || hitFaceRenderer == null) return;
        if (_hitFaceRoutine != null) StopCoroutine(_hitFaceRoutine);
        _hitFaceRoutine = StartCoroutine(HitFaceRoutine(seconds));
    }

    private IEnumerator HitFaceRoutine(float seconds)
    {
        hitFaceRenderer.enabled = true;
        yield return new WaitForSeconds(seconds);
        hitFaceRenderer.enabled = false;
        _hitFaceRoutine = null;
    }

    private Sprite[] Frames(StoneThrowHand hand)
        => hand == StoneThrowHand.Left ? leftHandFrames : rightHandFrames;
}
