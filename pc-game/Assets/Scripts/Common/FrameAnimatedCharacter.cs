// 관절 리깅(CavemanSilhouette의 팔 회전) 대신, 미리 그려진 "프레임 시퀀스"를 통째로 재생해서
// 캐릭터를 표현하는 컴포넌트. frames[0]은 항상 기본(대기) 프레임이다.
//
// 씬 편집 없이 코드에서 완전히 조립되도록 만들었다 - Attach()가 캐릭터 루트 밑의 기존 리깅
// SpriteRenderer를 전부 숨기고, 프레임 재생 전용 SpriteRenderer 하나를 새로 붙인다.
//
// 위치 규칙: 캐릭터 루트(부모)의 위치를 "발이 서 있는 자리(바닥)"로 본다. 스프라이트
// 피벗은 보통 중앙(0.5, 0.5)이라 그대로 얹으면 캐릭터가 반쯤 땅에 파묻히거나 공중에
// 떠 보이므로, 매 프레임 스프라이트의 실제 높이(스케일 적용 후)를 계산해서 그 절반만큼
// 위로 올려 붙인다 - 그래야 스프라이트 하단이 항상 루트 위치(바닥)에 맞는다.
//
// 크기는 width(월드 유닛)를 그대로 쓴다 - 다른 게임들의 fruitWidth/coconutWidth/stoneWidth
// 처럼 호출하는 게임 스크립트의 public 필드로 노출해서 에디터에서 눈으로 보고 조정하게
// 만들 것(자동으로 "적당히" 재는 방식은 캐릭터마다 리깅 크기가 달라서 신뢰할 수 없었다).
using System.Collections;
using UnityEngine;

public class FrameAnimatedCharacter : MonoBehaviour
{
    private SpriteRenderer _renderer;
    private Sprite[] _frames;
    private float _width;
    private float _yOffset;
    private Vector2[] _handAnchors;
    private Vector2 _currentHandAnchor;
    private bool _hideAtIdle;
    private bool _useWorldSpaceLayout;
    private Coroutine _playRoutine;

    public bool HasFrames => _frames != null && _frames.Length > 0;
    public bool IsPlaying => _playRoutine != null;

    // "이 그림에서 손이 있는 지점"을 스프라이트 로컬 좌표(피벗=중앙 기준, 오른쪽/위가 +)로
    // 프레임 번호별로 지정한 것 - 실제 관절 트래킹과는 무관하다. 부드럽게 보간하지 않고,
    // 그 순간 화면에 보이는 프레임(ShowFrame)이 바뀌는 그 타이밍에 정확히 그 프레임의
    // 앵커 값으로 스냅한다 - 코코넛처럼 손에 들려있는 소품이 그림의 각 자세와 항상 정확히
    // 맞아떨어지게 하려는 것.
    public Vector3 HandAnchorWorld => _renderer != null ? _renderer.transform.TransformPoint(_currentHandAnchor) : transform.position;

    // 현재 표시 중인 프레임과 관계없이 지정한 프레임이 표시됐을 때의 손 앵커 월드 좌표를 계산한다.
    // 조각 효과처럼 특정 프레임의 기준 위치에서 항상 시작해야 하는 연출에 사용한다.
    public Vector3 GetHandAnchorWorld(int frameIndex)
    {
        if (!HasFrames || _renderer == null) return transform.position;

        int index = Mathf.Clamp(frameIndex, 0, _frames.Length - 1);
        Sprite sprite = _frames[index];
        Transform parent = _renderer.transform.parent;
        if (sprite == null || parent == null) return transform.position;

        float localScale;
        Vector3 localPosition;
        if (_useWorldSpaceLayout)
        {
            float parentScaleX = Mathf.Max(0.0001f, Mathf.Abs(parent.lossyScale.x));
            float parentScaleY = Mathf.Max(0.0001f, Mathf.Abs(parent.lossyScale.y));
            localScale = _width / (sprite.bounds.size.x * parentScaleX);
            float bottomToPivot = -sprite.bounds.min.y * localScale;
            localPosition = new Vector3(0f, bottomToPivot + _yOffset / parentScaleY, 0f);
        }
        else
        {
            localScale = _width / sprite.bounds.size.x;
            float height = sprite.bounds.size.y * localScale;
            localPosition = new Vector3(0f, height * 0.5f + _yOffset, 0f);
        }

        Vector2 anchor = AnchorFor(index);
        Vector3 anchorInParent = localPosition + new Vector3(anchor.x * localScale, anchor.y * localScale, 0f);
        return parent.TransformPoint(anchorInParent);
    }

    // frames가 비어 있으면(아직 이 캐릭터/게임용 프레임이 준비 안 됨) 아무것도 하지 않고
    // null을 반환한다 - 기존 리깅이 그대로 보이는 폴백이 유지된다.
    // yOffset: 바닥 기준 위치에서 추가로 위(+)/아래(-)로 미세 조정할 값(월드 유닛).
    // handAnchors: 프레임 개수와 같은 길이의 배열(짧으면 마지막 값을 반복 사용). 위
    // HandAnchorWorld 설명 참고.
    // sortingOrder: 다른 소품(테이블 등)보다 앞/뒤에 그리고 싶을 때 조정.
    // keepVisible: 캐릭터 자식 중에 리깅 파츠가 아니라 별도 소품(코코넛 등)의 SpriteRenderer가
    // 있으면 여기 넣어서 숨기지 않게 한다 - 그냥 "캐릭터 밑의 SpriteRenderer 전부 숨기기"로는
    // 이런 소품까지 같이 사라져버린다.
    // hideAtIdle: true면 frames[0](대기 프레임)일 때 아예 렌더러를 꺼서 화면에 안 보이게
    // 한다 - PlayOnce가 재생되는 동안(frames[1..])만 보이고, 끝나서 다시 대기 상태로
    // 돌아오면 다시 안 보인다.
    // useWorldSpaceLayout: true면 width/yOffset을 부모 스케일과 무관한 월드 단위로 적용하고
    // 스프라이트의 실제 bounds.min을 사용해 바닥을 맞춘다. 기존 게임의 배치를 바꾸지 않도록
    // 기본값은 false이며, Coconut처럼 월드 단위 배치가 필요한 게임만 켠다.
    public static FrameAnimatedCharacter Attach(GameObject characterRoot, Sprite[] frames, float width, float yOffset = 0f,
        Vector2[] handAnchors = null, int sortingOrder = 1, bool hideAtIdle = false,
        bool useWorldSpaceLayout = false, params SpriteRenderer[] keepVisible)
    {
        if (frames == null || frames.Length == 0) return null;

        foreach (SpriteRenderer sr in characterRoot.GetComponentsInChildren<SpriteRenderer>())
        {
            if (keepVisible != null && System.Array.IndexOf(keepVisible, sr) >= 0) continue;
            sr.enabled = false;
        }

        var go = new GameObject("FrameAnimation");
        go.transform.SetParent(characterRoot.transform, false);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = sortingOrder;

        FrameAnimatedCharacter anim = go.AddComponent<FrameAnimatedCharacter>();
        anim._renderer = renderer;
        anim._frames = frames;
        anim._width = width;
        anim._yOffset = yOffset;
        anim._handAnchors = handAnchors;
        anim._hideAtIdle = hideAtIdle;
        anim._useWorldSpaceLayout = useWorldSpaceLayout;
        anim.ShowFrame(0);
        return anim;
    }

    // 연속값(0~1)에 비례해 프레임을 고른다 - 예: 점프 높이 비율. PlayOnce 재생 중에는 무시한다.
    public void SetProgress(float t)
    {
        if (!HasFrames || IsPlaying) return;
        int index = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(t) * (_frames.Length - 1)), 0, _frames.Length - 1);
        ShowFrame(index);
    }

    // duration 동안 frames[1..]을 순서대로 재생한 뒤 frames[0](기본 프레임)으로 되돌아온다.
    // 손 앵커는 이 프레임 전환과 정확히 같은 타이밍에 스냅된다(보간 없음). 이미 재생 중이면
    // 새로 끼어들지 않고 무시한다 - 끼어들어서 처음부터 다시 시작하면(예전 동작) 반복 타격이
    // 빠른 게임에서는 스윙이 끝까지 재생될 틈이 없어 항상 중간 프레임(예: 팔 든 자세)에
    // 멈춰 있는 것처럼 보였다. 지금은 한 번 시작한 스윙은 끝까지 재생돼서 반드시
    // frames[0](대기 자세)으로 돌아온 뒤에야 다음 재생을 받아들인다.
    public void PlayOnce(float duration)
    {
        if (!HasFrames || _frames.Length <= 1 || IsPlaying) return;
        _playRoutine = StartCoroutine(PlayRoutine(duration));
    }

    private IEnumerator PlayRoutine(float duration)
    {
        int steps = _frames.Length - 1;
        float perFrame = duration / steps;
        for (int i = 1; i < _frames.Length; i++)
        {
            ShowFrame(i);
            yield return new WaitForSeconds(perFrame);
        }
        ShowFrame(0);
        _playRoutine = null;
    }

    private void ShowFrame(int index)
    {
        _renderer.enabled = !(_hideAtIdle && index == 0);

        _renderer.sprite = _frames[index];
        if (_useWorldSpaceLayout)
        {
            FitWorldWidth(_renderer, _width);

            // 부모가 기존 리깅 프리팹이라 1이 아닌 스케일을 가지고 있어도 yOffset은 월드 단위로
            // 동작하게 보정한다. bounds.min.y를 사용하므로 스프라이트 피벗이 중앙이 아니거나
            // 프레임마다 크롭 영역이 달라도 하단이 항상 캐릭터 루트(바닥)에 맞는다.
            float localScale = _renderer.transform.localScale.y;
            float parentScaleY = Mathf.Max(0.0001f, Mathf.Abs(_renderer.transform.parent.lossyScale.y));
            float bottomToPivot = -_renderer.sprite.bounds.min.y * localScale;
            _renderer.transform.localPosition = new Vector3(0f, bottomToPivot + _yOffset / parentScaleY, 0f);
        }
        else
        {
            ArtAssets.FitWidth(_renderer, _width);
            float scale = _renderer.transform.localScale.y;
            float height = _renderer.sprite.bounds.size.y * scale;
            _renderer.transform.localPosition = new Vector3(0f, height * 0.5f + _yOffset, 0f);
        }

        _currentHandAnchor = AnchorFor(index);
    }

    private static void FitWorldWidth(SpriteRenderer renderer, float targetWorldWidth)
    {
        float nativeWidth = renderer.sprite.bounds.size.x;
        if (nativeWidth <= 0f) return;

        Transform parent = renderer.transform.parent;
        float parentScaleX = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
        parentScaleX = Mathf.Max(0.0001f, parentScaleX);
        renderer.transform.localScale = Vector3.one * (targetWorldWidth / (nativeWidth * parentScaleX));
    }

    private Vector2 AnchorFor(int index)
    {
        if (_handAnchors == null || _handAnchors.Length == 0) return Vector2.zero;
        int clamped = Mathf.Clamp(index, 0, _handAnchors.Length - 1);
        return _handAnchors[clamped];
    }
}
