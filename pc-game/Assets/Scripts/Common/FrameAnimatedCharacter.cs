// 관절 리깅(CavemanSilhouette의 팔 회전) 대신, 미리 그려진 "프레임 시퀀스"를 통째로 재생해서
// 캐릭터를 표현하는 컴포넌트. frames[0]은 항상 기본(대기) 프레임이다.
//
// 씬 편집 없이 코드에서 완전히 조립되도록 만들었다 - Attach()가 캐릭터 루트 밑의 기존 리깅
// SpriteRenderer를 전부 숨기고, 프레임 재생 전용 SpriteRenderer 하나를 새로 붙인다. 새
// 렌더러의 표시 폭은 숨기기 직전 리깅 파츠들의 전체 시각적 폭을 그대로 재는 방식으로 정해서,
// 에디터에서 그 캐릭터 인스턴스를 어떤 크기로 배치해뒀든 크기가 튀지 않게 맞춘다. 나중에
// 에디터에서 이 "FrameAnimation" 자식 오브젝트의 위치/크기를 손으로 더 다듬어도 된다 - 코드는
// 그 변경을 건드리지 않는다.
using System.Collections;
using UnityEngine;

public class FrameAnimatedCharacter : MonoBehaviour
{
    private SpriteRenderer _renderer;
    private Sprite[] _frames;
    private float _width;
    private Coroutine _playRoutine;

    public bool HasFrames => _frames != null && _frames.Length > 0;
    public bool IsPlaying => _playRoutine != null;

    // frames가 비어 있으면(아직 이 캐릭터/게임용 프레임이 준비 안 됨) 아무것도 하지 않고
    // null을 반환한다 - 기존 리깅이 그대로 보이는 폴백이 유지된다.
    public static FrameAnimatedCharacter Attach(GameObject characterRoot, Sprite[] frames, int sortingOrder = 1)
    {
        if (frames == null || frames.Length == 0) return null;

        SpriteRenderer[] rigParts = characterRoot.GetComponentsInChildren<SpriteRenderer>();
        float width = MeasureCombinedWidth(rigParts);
        foreach (SpriteRenderer sr in rigParts) sr.enabled = false;

        var go = new GameObject("FrameAnimation");
        go.transform.SetParent(characterRoot.transform, false);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = sortingOrder;

        FrameAnimatedCharacter anim = go.AddComponent<FrameAnimatedCharacter>();
        anim._renderer = renderer;
        anim._frames = frames;
        anim._width = width;
        anim.ShowFrame(0);
        return anim;
    }

    private static float MeasureCombinedWidth(SpriteRenderer[] renderers)
    {
        if (renderers.Length == 0) return 1.5f;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return Mathf.Max(0.1f, bounds.size.x);
    }

    // 연속값(0~1)에 비례해 프레임을 고른다 - 예: 점프 높이 비율. PlayOnce 재생 중에는 무시한다.
    public void SetProgress(float t)
    {
        if (!HasFrames || IsPlaying) return;
        int index = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(t) * (_frames.Length - 1)), 0, _frames.Length - 1);
        ShowFrame(index);
    }

    // duration 동안 frames[1..]을 순서대로 재생한 뒤 frames[0](기본 프레임)으로 되돌아온다.
    public void PlayOnce(float duration)
    {
        if (!HasFrames || _frames.Length <= 1) return;
        if (_playRoutine != null) StopCoroutine(_playRoutine);
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
        _renderer.sprite = _frames[index];
        ArtAssets.FitWidth(_renderer, _width);
    }
}
