using UnityEngine;

// 깃털날기 전용: jump_1~4(절벽에서 뛰어내리는 인트로)와 flap_1~3(양손에 깃털을 든 날갯짓
// 팔 위/중간/아래) 프레임을 재생한다. CoconutCharacterFrames와 같은 원칙 - 런타임에 새
// GameObject를 만들지 않고, 씬에 미리 배치된 SpriteRenderer 하나의 스프라이트만
// 갈아끼운다. Play를 누르지 않아도 씬을 열면 바로 캐릭터가 보인다.
//
// 위치는 "기준 자리(_basePosition)"만 갖고, 실제 표시 위치는 항상 기준 + 컷별 X 오프셋이다.
// 기준은 기본적으로 씬에 배치된 자리지만, FeatherFlightGame이 SetBase()로 매 프레임 바꿀 수
// 있다(예: 원래 Y로 되돌아오는 인트로 홉 동안 X만 RestPoint 쪽으로 옮기는 식). 인트로가
// 끝나 기준이 고정되면, 그 뒤로는 컷 오프셋(X)만 씬에서 직접 조정하면 된다 - Y는 항상
// 기준과 같다. (원래 스폰 Y는 FeatherFlightGame이 실제 게임플레이 시작 height로도 쓴다.)
[ExecuteAlways]
public sealed class FeatherFlightCharacterView : MonoBehaviour
{
    [SerializeField] private PlayerId player;
    [SerializeField] private SpriteRenderer frameRenderer;

    [Tooltip("Resources/Characters/p{1,2}_<prefix>_N.png 접두어. P1은 보통 _right, P2는 _left.")]
    [SerializeField] private string jumpPrefix = "feather_flight_jump_right";
    [SerializeField] private string flapPrefix = "feather_flight_flap_right";

    [Tooltip("jump 컷(0=1번째)마다 기준 X에서 추가로 더할 오프셋(월드 유닛). 배열 길이가 컷 수보다 짧으면 나머지 컷은 0으로 취급.")]
    [SerializeField] private float[] jumpFrameOffsetX;
    [Tooltip("flap 컷(0=1번째)마다 기준 X에서 추가로 더할 오프셋(월드 유닛).")]
    [SerializeField] private float[] flapFrameOffsetX;

    private Sprite[] _jumpFrames;
    private Sprite[] _flapFrames;
    private Vector3 _basePosition;
    private bool _baseInitialized;

    public int JumpFrameCount => _jumpFrames?.Length ?? 0;
    public int FlapFrameCount => _flapFrames?.Length ?? 0;

    private void OnEnable()
    {
        EnsureBase();
        EnsureFrames();
    }

    private void OnValidate() => EnsureFrames();

    // FeatherFlightGame이 "지금 이 순간의 기준 자리"를 알려줄 때 쓴다 - 예를 들어 절벽에서
    // 화면 중앙 정착 자리로 도약하는 인트로 동안 매 프레임 호출해서 기준을 옮긴다.
    public void SetBase(Vector3 basePosition)
    {
        _basePosition = basePosition;
        _baseInitialized = true;
    }

    private void EnsureBase()
    {
        if (_baseInitialized) return;
        _basePosition = transform.position;
        _baseInitialized = true;
    }

    private void EnsureFrames()
    {
        if (_jumpFrames == null || _jumpFrames.Length == 0)
            _jumpFrames = ArtAssets.LoadCharacterSequence(player, jumpPrefix);
        if (_flapFrames == null || _flapFrames.Length == 0)
            _flapFrames = ArtAssets.LoadCharacterSequence(player, flapPrefix);
        if (frameRenderer != null && frameRenderer.sprite == null)
            ShowJumpFrame(0);
    }

    public void ShowJumpFrame(int index)
    {
        if (_jumpFrames == null || _jumpFrames.Length == 0 || frameRenderer == null) return;
        index = Mathf.Clamp(index, 0, _jumpFrames.Length - 1);
        frameRenderer.sprite = _jumpFrames[index];
        ApplyFrameOffsetX(jumpFrameOffsetX, index);
    }

    public void ShowFlapFrame(int index)
    {
        if (_flapFrames == null || _flapFrames.Length == 0 || frameRenderer == null) return;
        index = Mathf.Clamp(index, 0, _flapFrames.Length - 1);
        frameRenderer.sprite = _flapFrames[index];
        ApplyFrameOffsetX(flapFrameOffsetX, index);
    }

    private void ApplyFrameOffsetX(float[] offsets, int index)
    {
        EnsureBase();
        float offsetX = (offsets != null && index >= 0 && index < offsets.Length) ? offsets[index] : 0f;
        transform.position = new Vector3(_basePosition.x + offsetX, _basePosition.y, _basePosition.z);
    }
}
