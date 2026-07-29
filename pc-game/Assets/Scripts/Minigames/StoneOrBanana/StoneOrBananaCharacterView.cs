using UnityEngine;

// 한 플레이어를 정면 또는 후면에서 보여 주는 통짜 스프라이트 뷰다.
// 손/입 앵커와 수풀은 모두 씬 자식 오브젝트이므로 Play 모드 밖에서 직접 조절할 수 있다.
public class StoneOrBananaCharacterView : MonoBehaviour
{
    [Header("표시 대상")]
    [SerializeField] private PlayerId player;
    [SerializeField] private bool backView;
    [SerializeField] private SpriteRenderer characterRenderer;
    [SerializeField] private SpriteRenderer bushRenderer;
    [SerializeField, Min(.1f)] private float displayWidth = 3f;

    [Header("씬에서 조절하는 투사체 기준점")]
    [SerializeField] private Transform leftHandReleaseAnchor;
    [SerializeField] private Transform rightHandReleaseAnchor;
    [SerializeField] private Transform receiveAnchor;

    [Header("StoneThrow 6컷")]
    [SerializeField] private Sprite[] leftHandFrames;
    [SerializeField] private Sprite[] rightHandFrames;
    [SerializeField, Min(0)] private int heldPoseFrameIndex = 2;

    [Header("받는 역할 정면 전신")]
    [SerializeField] private Sprite mouthClosedSprite;
    [SerializeField] private Sprite mouthOpenSprite;
    [SerializeField] private Sprite bananaChewingSprite;
    [SerializeField] private Sprite stoneHitOneToothSprite;
    [SerializeField] private Sprite stoneHitTwoTeethSprite;

    public PlayerId Player => player;
    public bool IsBackView => backView;
    public SpriteRenderer BushRenderer => bushRenderer;

    private void Awake()
    {
        ShowReceiver(false);
    }

    public void ShowHeldHand(StoneThrowHand hand)
    {
        Sprite[] frames = Frames(hand);
        if (frames == null || frames.Length == 0) return;
        SetSprite(frames[Mathf.Clamp(heldPoseFrameIndex, 0, frames.Length - 1)]);
    }

    public void ShowThrowFrame(StoneThrowHand hand, int frameIndex)
    {
        Sprite[] frames = Frames(hand);
        if (frames == null || frames.Length == 0) return;
        SetSprite(frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)]);
    }

    public int FrameCount(StoneThrowHand hand) => Frames(hand)?.Length ?? 0;

    public void ShowReceiver(bool mouthOpen)
    {
        if (backView)
        {
            Sprite[] idle = rightHandFrames != null && rightHandFrames.Length > 0
                ? rightHandFrames : leftHandFrames;
            if (idle != null && idle.Length > 0) SetSprite(idle[0]);
            return;
        }
        SetSprite(mouthOpen ? mouthOpenSprite : mouthClosedSprite);
    }

    public void ShowBananaChewing()
    {
        if (!backView) SetSprite(bananaChewingSprite);
    }

    public void ShowStoneHit(int lostTeeth)
    {
        if (!backView)
            SetSprite(lostTeeth >= 2 ? stoneHitTwoTeethSprite : stoneHitOneToothSprite);
    }

    public Vector3 ReleasePosition(StoneThrowHand hand)
    {
        Transform anchor = hand == StoneThrowHand.Left ? leftHandReleaseAnchor : rightHandReleaseAnchor;
        return anchor != null ? anchor.position : transform.position;
    }

    public Vector3 ReceivePosition()
        => receiveAnchor != null ? receiveAnchor.position : transform.position;

    private Sprite[] Frames(StoneThrowHand hand)
        => hand == StoneThrowHand.Left ? leftHandFrames : rightHandFrames;

    private void SetSprite(Sprite sprite)
    {
        if (characterRenderer == null || sprite == null) return;
        characterRenderer.sprite = sprite;
        float nativeWidth = sprite.bounds.size.x;
        if (nativeWidth > 0f)
            characterRenderer.transform.localScale = Vector3.one * (displayWidth / nativeWidth);
    }
}
