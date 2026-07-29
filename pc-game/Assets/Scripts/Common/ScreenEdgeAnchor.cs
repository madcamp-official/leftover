using UnityEngine;

// RectTransform의 anchor/anchoredPosition 개념을 월드 스페이스 오브젝트에 그대로 적용한다.
// anchor(0~1, 0~1)로 카메라 뷰포트 안의 기준점을 고르고(예: x=0은 화면 왼쪽 끝, x=1은
// 오른쪽 끝), anchoredPosition(월드 유닛)만큼 그 기준점에서 추가로 옮긴 자리에 이
// 오브젝트를 둔다. 화면 비율(aspect)이 바뀌어도 anchor가 가리키는 카메라 뷰 가장자리에는
// 항상 붙어 있는다 - CameraBackgroundFitter와 같은 원리를 배경 전체가 아니라 임의의
// 기준점 하나에 적용한 것.
//
// 이 오브젝트 자체가 화면 가장자리에 붙는 "기준점"이고, 실제로 보이는 스프라이트(테이블
// 등)는 자식으로 두고 스프라이트의 피벗을 그 가장자리 쪽(예: 왼쪽 절반은 왼쪽 피벗)으로
// 맞추면 "스프라이트의 그 가장자리가 화면 가장자리에 붙어있다"는 결과가 된다.
[ExecuteAlways]
public sealed class ScreenEdgeAnchor : MonoBehaviour
{
    [Tooltip("0~1 카메라 뷰포트 기준점. x: 0=왼쪽 끝, 1=오른쪽 끝, 0.5=가운데. y: 0=아래쪽 끝, 1=위쪽 끝, 0.5=가운데.")]
    public Vector2 anchor = new Vector2(0f, 0.5f);

    [Tooltip("기준점에서 월드 유닛으로 추가 이동 - RectTransform의 anchoredPosition과 같은 역할. 화면 가장자리에 딱 붙이려면 0, 안쪽으로 여백을 두려면 양수/음수로 조절.")]
    public Vector2 anchoredPosition = Vector2.zero;

    private Camera _camera;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();
    private void LateUpdate() => Apply();

    public void Apply()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null || !_camera.orthographic) return;

        float halfHeight = _camera.orthographicSize;
        float halfWidth = halfHeight * _camera.aspect;
        Vector3 camPos = _camera.transform.position;

        float x = camPos.x + Mathf.Lerp(-halfWidth, halfWidth, anchor.x) + anchoredPosition.x;
        float y = camPos.y + Mathf.Lerp(-halfHeight, halfHeight, anchor.y) + anchoredPosition.y;

        Vector3 p = transform.position;
        transform.position = new Vector3(x, y, p.z);
    }
}
