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
    [SerializeField] private Sprite hitFullBodySprite;

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

    private Coroutine _hitReactionRoutine;
    private Sprite _requestedSprite;

    public PlayerId Player => player;
    public bool IsBackView => backView;

    private void Awake()
    {
        if (!backView && hitFullBodySprite == null)
        {
            int playerNumber = player == PlayerId.P1 ? 1 : 2;
            hitFullBodySprite = Resources.Load<Sprite>($"Characters/p{playerNumber}_stone_throw_hit_fullbody");
        }
        ShowIdle();
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
        SetRequestedSprite(frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)]);
    }

    public void ShowIdle()
    {
        Sprite[] frames = rightHandFrames != null && rightHandFrames.Length > 0
            ? rightHandFrames : leftHandFrames;
        if (characterRenderer != null && frames != null && frames.Length > 0)
            SetRequestedSprite(frames[0]);
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

    public void ShowHitFullBody(float seconds)
    {
        if (backView || characterRenderer == null || hitFullBodySprite == null) return;
        if (_hitReactionRoutine != null) StopCoroutine(_hitReactionRoutine);
        _hitReactionRoutine = StartCoroutine(HitFullBodyRoutine(seconds));
    }

    private IEnumerator HitFullBodyRoutine(float seconds)
    {
        characterRenderer.sprite = hitFullBodySprite;
        yield return new WaitForSeconds(seconds);
        _hitReactionRoutine = null;
        if (_requestedSprite != null) characterRenderer.sprite = _requestedSprite;
    }

    private void SetRequestedSprite(Sprite sprite)
    {
        _requestedSprite = sprite;
        if (_hitReactionRoutine == null && characterRenderer != null)
            characterRenderer.sprite = sprite;
    }

    private Sprite[] Frames(StoneThrowHand hand)
        => hand == StoneThrowHand.Left ? leftHandFrames : rightHandFrames;
}
