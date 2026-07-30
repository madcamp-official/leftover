// 창을 자유롭게 리사이즈해도 게임 출력 화면은 항상 16:9로 고정한다(레터박스/필러박스로
// 남는 공간은 검은 여백) - 사용자 요구: "전체화면모드만 안되게하고, 창모드일때 창 크기
// 조절이 자유롭게 되게 해줘. 대신 게임 출력 화면이 16:9로 고정이어야지." ProjectSettings의
// resizableWindow=1(자유 리사이즈), fullscreenMode=Windowed + allowFullscreenSwitch=0
// (전체화면 진입 자체를 막음)과 짝을 이루는 런타임 쪽 절반이다.
//
// 구현 원리: 씬의 실제 렌더링 카메라(Camera.main, 씬마다 새로 생김) 하나에만 매 프레임
// Camera.rect(정규화된 뷰포트)를 16:9로 계산해서 매긴다. World Space Canvas +
// CameraCanvasFitter를 쓰는 화면(Hub StartScreenCanvas, 미니게임 HUD)은 카메라
// aspect/뷰포트만 맞으면 자동으로 따라온다 - CameraCanvasFitter가 이미 _camera.aspect를
// 그대로 쓰기 때문에 별도 손질이 필요 없다.
//
// 별도 카메라를 더 만드는 대신 Camera.main 하나만 쓴다 - 실측 확인: URP(2D Renderer)에서
// 서로 다른 depth를 가진 독립 Base 카메라 여러 개를 겹쳐 쓰는 예전 빌트인 파이프라인 방식은
// 제대로 합성이 안 되고 World Space 콘텐츠가 통째로 안 보이는 문제가 났다(제대로 하려면 URP
// 카메라 스택 API를 써야 하는데, 그럴 필요 없이 카메라 하나만 쓰는 쪽이 훨씬 단순하고
// 안전하다). 뷰포트 밖(레터박스 여백)은 카메라가 아예 안 그리는 영역이라 그대로 두면
// 검은색으로 남는다(실측 확인 - 별도로 채울 필요 없었음).
//
// Screen Space - Overlay 캔버스(UiBuilder.CreateOverlayCanvas, LoadingScreenController,
// 프리팹으로 저장된 화면들)는 카메라 뷰포트를 무시하고 항상 창 전체를 채우므로, 매 프레임
// 씬을 훑어서 전부 Screen Space - Camera로 바꾸고 이 Camera.main을 물려준다 - 생성 코드마다
// 하나하나 손대는 대신 한 곳에서 일괄 처리해서 프리팹이든 코드 생성이든 놓치는 곳이 없다.
using UnityEngine;

public sealed class LetterboxController : MonoBehaviour
{
    private const float TargetAspect = 16f / 9f;

    private static LetterboxController _instance;

    public static LetterboxController Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<LetterboxController>();
            return _instance;
        }
        private set => _instance = value;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void LateUpdate()
    {
        // Camera.main은 씬을 옮길 때마다 다른 인스턴스를 가리키므로 매 프레임 다시 찾는다 -
        // 비용은 태그 조회 한 번뿐이라 무시할 만하다.
        Camera main = Camera.main;
        if (main == null) return;

        Rect viewport = ComputeLetterboxRect();
        main.rect = viewport;
        RelinkOverlayCanvases(main);
    }

    private static void RelinkOverlayCanvases(Camera uiCamera)
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
        }
    }

    private static Rect ComputeLetterboxRect()
    {
        float windowAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : TargetAspect;
        if (windowAspect > TargetAspect)
        {
            // 창이 16:9보다 넓다 - 좌우를 필러박스로 남긴다.
            float normalizedWidth = TargetAspect / windowAspect;
            return new Rect((1f - normalizedWidth) / 2f, 0f, normalizedWidth, 1f);
        }
        // 창이 16:9보다 좁다(세로로 김) - 위아래를 레터박스로 남긴다.
        float normalizedHeight = windowAspect / TargetAspect;
        return new Rect(0f, (1f - normalizedHeight) / 2f, 1f, normalizedHeight);
    }
}
