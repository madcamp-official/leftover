// 원시인 캐릭터의 최소 공용 표현. "엉클그랜파" 류 카툰 실루엣(두꺼운 검은 외곽선 + 납작한
// 원색 + 과장되게 큰 머리) 룩으로, 몸통(캡슐) + 머리(원, 몸통보다 비율상 크게) + 눈 2개(장식용
// 점) + 양손(둥근 뭉툭한 원) 파츠로 구성된다. 손 든 상태/머리 기울기 같은 공용 제스처를 그대로
// 반영해서 보여준다. 미니게임마다 필요한 디테일(예: 눈빛싸움의 눈 표시, 돌던지기의 던지는 팔
// 각도)은 이 위에 각 미니게임 스크립트가 덧붙이면 된다 - 이 컴포넌트는 "아무 게임에서나 바로
// 쓸 수 있는 최소 골격"만 담당.
using UnityEngine;

public class CavemanSilhouette : MonoBehaviour
{
    public PlayerId player;
    public Color bodyColor = Color.white;

    private SpriteRenderer _body;
    private SpriteRenderer _head;
    private SpriteRenderer _leftHand;
    private SpriteRenderer _rightHand;

    private void Start()
    {
        // Deliberately Start(), not Awake(): callers do
        // `var s = go.AddComponent<CavemanSilhouette>(); s.player = id;` - AddComponent
        // runs Awake() synchronously before that second line ever executes, so reading
        // `player` in Awake() would always see the default (P1) regardless of what the
        // caller actually assigns. Start() runs on the next frame, well after the
        // caller's own field assignment, so `player` is reliably set by then.
        bodyColor = player == PlayerId.P1 ? new Color(0.2f, 0.6f, 1f) : new Color(1f, 0.3f, 0.25f);

        _body = CreatePart("Body", RuntimeSpriteFactory.CreateCapsule(64, 110, bodyColor), new Vector3(0, 0f, 0));
        _head = CreatePart("Head", RuntimeSpriteFactory.CreateCircle(96, bodyColor), new Vector3(0, 1f, 0));
        _leftHand = CreatePart("LeftHand", RuntimeSpriteFactory.CreateCircle(34, bodyColor), new Vector3(-0.48f, 0.2f, 0));
        _rightHand = CreatePart("RightHand", RuntimeSpriteFactory.CreateCircle(34, bodyColor), new Vector3(0.48f, 0.2f, 0));

        // 장식용 얼굴 - 검은 점 눈 2개. 포즈에 반응하지 않는 고정 파츠라 _head의 자식으로 붙여서
        // 머리가 기울면 눈도 같이 기울도록 한다.
        // 오프셋은 Head 로컬 좌표 기준(머리 중심 = 원점, 지름 0.96 유닛짜리 원 위에 눈 두 개).
        Sprite eyeSprite = RuntimeSpriteFactory.CreateCircle(14, RuntimeSpriteFactory.OutlineColor, outlinePx: 0f);
        CreateEye(eyeSprite, new Vector3(-0.16f, 0.05f, 0f));
        CreateEye(eyeSprite, new Vector3(0.16f, 0.05f, 0f));
    }

    private void CreateEye(Sprite sprite, Vector3 headLocalPos)
    {
        var go = new GameObject("Eye");
        go.transform.SetParent(_head.transform, false);
        go.transform.localPosition = headLocalPos;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;
    }

    private SpriteRenderer CreatePart(string name, Sprite sprite, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        return renderer;
    }

    // 매 프레임 미니게임 쪽에서 호출해서 공용 제스처를 시각적으로 반영한다.
    public void ApplyPose(PlayerPoseState state)
    {
        // Start()가 아직 실행되기 전(같은 프레임에서 생성 직후 호출되는 등 드문 타이밍)이면
        // 파츠가 아직 null일 수 있으니 방어적으로 무시한다 - 다음 프레임에 다시 호출된다.
        if (_leftHand == null || _rightHand == null || _head == null) return;
        if (state == null || !state.IsTracked) return;

        float raisedHeight = 0.9f;
        _leftHand.transform.localPosition = new Vector3(-0.48f,
            state.IsHandRaised(rightHand: false) ? raisedHeight : 0.2f, 0f);
        _rightHand.transform.localPosition = new Vector3(0.48f,
            state.IsHandRaised(rightHand: true) ? raisedHeight : 0.2f, 0f);

        float tiltDeg = Mathf.Clamp(state.HeadTiltRatio(), -1f, 1f) * -25f;
        _head.transform.localRotation = Quaternion.Euler(0, 0, tiltDeg);
    }
}
