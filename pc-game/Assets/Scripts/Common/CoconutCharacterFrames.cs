using UnityEngine;

// CoconutCrack 전용: coconut_1~N 프레임 시퀀스를 재생하되, FrameAnimatedCharacter처럼
// 런타임에 새 GameObject를 만들지 않는다 - 씬에 미리 배치해 둔 SpriteRenderer 하나의
// 스프라이트만 갈아끼운다. 그래서 Play를 누르지 않아도 씬을 열면 바로 캐릭터가 보인다.
//
// 코코넛이 각 컷(프레임)에서 손에 들려 있어야 할 자리는 좌표 계산이 아니라 coconutAnchors에
// 지정한 빈 Transform들로 정한다 - Scene 뷰에서 직접 드래그해서 눈으로 맞추는 방식.
[ExecuteAlways]
public sealed class CoconutCharacterFrames : MonoBehaviour
{
    [SerializeField] private PlayerId player;
    [SerializeField] private SpriteRenderer frameRenderer;

    [Tooltip("coconut_1부터 순서대로 - 이 컷에서 코코넛이 손에 들려 있어야 할 자리. Scene 뷰에서 직접 옮겨서 맞춘다.")]
    [SerializeField] private Transform[] coconutAnchors;

    private Sprite[] _frames;
    private int _currentIndex;

    public int FrameCount => _frames?.Length ?? 0;
    public int CurrentIndex => _currentIndex;

    private void OnEnable() => EnsureFrames();
    private void OnValidate() => EnsureFrames();

    private void EnsureFrames()
    {
        if (_frames == null || _frames.Length == 0)
            _frames = ArtAssets.LoadCharacterSequence(player, "coconut");
        ShowFrame(_currentIndex);
    }

    public void ShowFrame(int index)
    {
        if (_frames == null || _frames.Length == 0 || frameRenderer == null) return;
        _currentIndex = Mathf.Clamp(index, 0, _frames.Length - 1);
        frameRenderer.sprite = _frames[_currentIndex];
    }

    public Vector3 CoconutAnchorWorld(int index)
    {
        if (coconutAnchors == null || coconutAnchors.Length == 0) return transform.position;
        Transform t = coconutAnchors[Mathf.Clamp(index, 0, coconutAnchors.Length - 1)];
        return t != null ? t.position : transform.position;
    }
}
