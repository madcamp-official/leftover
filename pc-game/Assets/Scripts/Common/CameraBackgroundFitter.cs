using UnityEngine;

// 배경의 원본 비율을 유지하면서 카메라 뷰 전체를 덮는다. 화면비가 달라지면 남는 부분은
// 카메라 바깥으로 잘리므로 Free Aspect나 울트라와이드에서도 빈 공간이 생기지 않는다.
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class CameraBackgroundFitter : MonoBehaviour
{
    private SpriteRenderer _renderer;
    private Camera _camera;

    private void OnEnable() => Fit();
    private void OnValidate() => Fit();
    private void LateUpdate() => Fit();

    public void Fit()
    {
        if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
        if (_camera == null) _camera = Camera.main;
        if (_renderer == null || _renderer.sprite == null || _camera == null || !_camera.orthographic) return;

        Vector2 native = _renderer.sprite.bounds.size;
        if (native.x <= 0f || native.y <= 0f) return;

        float viewHeight = _camera.orthographicSize * 2f;
        float viewWidth = viewHeight * _camera.aspect;
        float coverScale = Mathf.Max(viewWidth / native.x, viewHeight / native.y);
        transform.localScale = new Vector3(coverScale, coverScale, 1f);
    }
}
