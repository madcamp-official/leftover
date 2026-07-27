// 원시인 캐릭터의 최소 공용 표현. 몸통(캡슐) + 머리(원) + 양손(작은 원) 4개 파츠로만
// 구성된 실루엣이고, 손 든 상태/머리 기울기 같은 공용 제스처를 그대로 반영해서 보여준다.
// 미니게임마다 필요한 디테일(예: 눈빛싸움의 눈 표시, 돌던지기의 던지는 팔 각도)은 이 위에
// 각 미니게임 스크립트가 덧붙이면 된다 - 이 컴포넌트는 "아무 게임에서나 바로 쓸 수 있는
// 최소 골격"만 담당.
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
        bodyColor = player == PlayerId.P1 ? new Color(0.25f, 0.55f, 0.95f) : new Color(0.95f, 0.35f, 0.25f);

        _body = CreatePart("Body", RuntimeSpriteFactory.CreateCapsule(60, 120, bodyColor), new Vector3(0, 0f, 0));
        _head = CreatePart("Head", RuntimeSpriteFactory.CreateCircle(70, bodyColor), new Vector3(0, 0.95f, 0));
        _leftHand = CreatePart("LeftHand", RuntimeSpriteFactory.CreateCircle(26, bodyColor), new Vector3(-0.45f, 0.2f, 0));
        _rightHand = CreatePart("RightHand", RuntimeSpriteFactory.CreateCircle(26, bodyColor), new Vector3(0.45f, 0.2f, 0));
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
        if (state == null || !state.IsTracked) return;

        float raisedHeight = 0.9f;
        _leftHand.transform.localPosition = new Vector3(-0.45f,
            state.IsHandRaised(rightHand: false) ? raisedHeight : 0.2f, 0f);
        _rightHand.transform.localPosition = new Vector3(0.45f,
            state.IsHandRaised(rightHand: true) ? raisedHeight : 0.2f, 0f);

        float tiltDeg = Mathf.Clamp(state.HeadTiltRatio(), -1f, 1f) * -25f;
        _head.transform.localRotation = Quaternion.Euler(0, 0, tiltDeg);
    }
}
