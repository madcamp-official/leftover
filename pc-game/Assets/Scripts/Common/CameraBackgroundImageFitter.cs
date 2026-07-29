using UnityEngine;
using UnityEngine.UI;

// Canvas 안의 배경 Image를 원본 비율 그대로 유지하면서 부모(카메라에 맞춰진 Canvas) 전체를
// 덮는다. 화면비가 다르면 남는 부분만 프레임 밖으로 잘리며 좌우/상하 빈 공간은 생기지 않는다.
[ExecuteAlways]
[RequireComponent(typeof(Image))]
public sealed class CameraBackgroundImageFitter : MonoBehaviour
{
    private Image _image;
    private RectTransform _rect;
    private RectTransform _parentRect;

    private void OnEnable() => Fit();
    private void OnValidate() => Fit();
    private void LateUpdate() => Fit();

    public void Fit()
    {
        if (_image == null) _image = GetComponent<Image>();
        if (_rect == null) _rect = GetComponent<RectTransform>();
        if (_parentRect == null) _parentRect = transform.parent as RectTransform;
        if (_image == null || _image.sprite == null || _rect == null || _parentRect == null) return;

        Vector2 native = _image.sprite.rect.size;
        Vector2 area = _parentRect.rect.size;
        if (native.x <= 0f || native.y <= 0f || area.x <= 0f || area.y <= 0f) return;

        float scale = Mathf.Max(area.x / native.x, area.y / native.y);
        _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.anchoredPosition = Vector2.zero;
        _rect.sizeDelta = native * scale;
        _rect.localScale = Vector3.one;
        _image.preserveAspect = true;
    }
}
