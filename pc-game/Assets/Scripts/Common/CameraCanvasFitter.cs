using UnityEngine;

// 편집 가능한 World Space Canvas의 사각형을 현재 카메라 사각형과 일치시킨다.
[ExecuteAlways]
[RequireComponent(typeof(Canvas))]
public sealed class CameraCanvasFitter : MonoBehaviour
{
    private const float ReferenceHeight = 1152f;
    private Camera _camera;
    private RectTransform _rect;

    private void OnEnable() => Fit();
    private void OnValidate() => Fit();
    private void LateUpdate() => Fit();

    public void Fit()
    {
        if (_camera == null) _camera = Camera.main;
        if (_rect == null) _rect = GetComponent<RectTransform>();
        if (_camera == null || _rect == null || !_camera.orthographic) return;

        float referenceWidth = ReferenceHeight * _camera.aspect;
        float worldScale = _camera.orthographicSize * 2f / ReferenceHeight;
        _rect.sizeDelta = new Vector2(referenceWidth, ReferenceHeight);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.position = new Vector3(_camera.transform.position.x, _camera.transform.position.y, -1f);
        _rect.localScale = Vector3.one * worldScale;
        _rect.localRotation = Quaternion.identity;
    }
}
