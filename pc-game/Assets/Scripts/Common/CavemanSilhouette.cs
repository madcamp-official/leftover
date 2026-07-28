// 원시인 캐릭터 표현. image/characters/character{1,2}/front/의 실제 리깅 아트(머리, 몸통,
// 팔은 상완+하완(손 포함) 2단 관절, 다리는 정적 장식)로 조립한다. P1=character1,
// P2=character2. 팔은 PoseInputHub의 실제 어깨/팔꿈치/손목 각도로 매 프레임 회전시켜서
// 관절이 굽혀지는 것처럼 보이게 한다 - 길이는 아트의 고정 비율을 쓰고, 방향(각도)만
// 트래킹값을 따라간다. 손 든 상태/머리 기울기 같은 공용 제스처를 여기서 처리하고,
// 미니게임마다 필요한 디테일(예: 눈빛싸움의 눈 감김 표정)은 이 위에 각 미니게임 스크립트가
// 덧붙이면 된다.
using UnityEngine;

public class CavemanSilhouette : MonoBehaviour
{
    public PlayerId player;

    [Header("크기 (튜닝 가능, 월드 유닛)")]
    public float torsoHeight = 0.75f;

    private SpriteRenderer _head;
    private Sprite _defaultHeadSprite;
    private float _partScale;
    private Transform _leftShoulderPivot, _rightShoulderPivot;
    private Transform _leftElbowPivot, _rightElbowPivot;
    private float _upperArmLength, _lowerArmLength;

    public Transform HeadTransform => _head != null ? _head.transform : null;

    private void Start()
    {
        // Deliberately Start(), not Awake(): 호출자가 AddComponent 직후 player를 대입하므로
        // Awake()에서 읽으면 항상 기본값(P1)만 보인다 - CavemanSilhouette 초창기부터의 패턴.
        Sprite headSprite = LoadPart("head");
        Sprite torsoSprite = LoadPart("torso");
        Sprite leftUpperArm = LoadPart("left_upper_arm");
        Sprite leftLowerArm = LoadPart("left_lower_arm_hand");
        Sprite rightUpperArm = LoadPart("right_upper_arm");
        Sprite rightLowerArm = LoadPart("right_lower_arm_hand");
        Sprite leftUpperLeg = LoadPart("left_upper_leg");
        Sprite leftLowerLeg = LoadPart("left_lower_leg_foot");
        Sprite rightUpperLeg = LoadPart("right_upper_leg");
        Sprite rightLowerLeg = LoadPart("right_lower_leg_foot");
        _defaultHeadSprite = headSprite;

        // sprite.bounds.size는 각 스프라이트가 임포트된 PPU까지 반영된 "네이티브 월드 크기"다
        // (rect는 그냥 픽셀 수라 PPU가 100이 아니면 실제 렌더 크기와 안 맞음 - 여기서 이 실수로
        // 캐릭터가 카메라를 꽉 채울 만큼 거대하게 렌더링되는 버그가 한 번 났었다). torso의
        // 네이티브 크기 대비 원하는 torsoHeight 비율을 모든 파츠에 동일하게 적용해서 하나의
        // 일관된 캐릭터로 스케일한다.
        _partScale = torsoHeight / torsoSprite.bounds.size.y;

        float torsoWorldWidth = torsoSprite.bounds.size.x * _partScale;
        float headWorldHeight = headSprite.bounds.size.y * _partScale;
        _upperArmLength = leftUpperArm.bounds.size.y * _partScale;
        _lowerArmLength = leftLowerArm.bounds.size.y * _partScale;
        float upperLegLength = leftUpperLeg.bounds.size.y * _partScale;
        float lowerLegLength = leftLowerLeg.bounds.size.y * _partScale;

        // 발이 원점(0,0)에 오도록 다리→몸통→머리 순서로 아래에서 위로 쌓는다.
        float legOverlap = upperLegLength * 0.25f; // 통 옷자락 밑으로 다리가 살짝 들어가 보이게
        float legTopY = lowerLegLength + upperLegLength;
        float torsoBottomY = legTopY - legOverlap;
        float torsoCenterY = torsoBottomY + torsoHeight * 0.5f;
        float torsoTopY = torsoBottomY + torsoHeight;

        CreateStaticLeg(leftUpperLeg, leftLowerLeg, -torsoWorldWidth * 0.18f, lowerLegLength, upperLegLength, -1);
        CreateStaticLeg(rightUpperLeg, rightLowerLeg, torsoWorldWidth * 0.18f, lowerLegLength, upperLegLength, -1);

        CreatePart("Torso", torsoSprite, new Vector3(0, torsoCenterY, 0), 0);

        _head = CreatePart("Head", headSprite, new Vector3(0, torsoTopY + headWorldHeight * 0.42f, 0), 2);

        float shoulderY = torsoBottomY + torsoHeight * 0.83f;
        float shoulderX = torsoWorldWidth * 0.36f;

        _leftShoulderPivot = CreateArm("Left", leftUpperArm, leftLowerArm, new Vector3(-shoulderX, shoulderY, 0),
            out _leftElbowPivot);
        _rightShoulderPivot = CreateArm("Right", rightUpperArm, rightLowerArm, new Vector3(shoulderX, shoulderY, 0),
            out _rightElbowPivot);
    }

    private Sprite LoadPart(string partName) => ArtAssets.LoadCharacter(player, partName);

    // 상황별 표정으로 잠깐 바꾸고 싶을 때(예: 돌에 맞음, 바나나를 먹음) 사용. part는
    // "face_grimacing", "face_stone_hit_one_tooth_broken" 같은 ArtAssets.LoadCharacter 규칙.
    public void SetFace(string facePart)
    {
        if (_head == null) return;
        Sprite sprite = ArtAssets.LoadCharacter(player, facePart);
        if (sprite != null) _head.sprite = sprite;
    }

    public void ResetFace()
    {
        if (_head != null) _head.sprite = _defaultHeadSprite;
    }

    private void CreateStaticLeg(Sprite upperLeg, Sprite lowerLeg, float x,
        float lowerLegLength, float upperLegLength, int sortingOrder)
    {
        CreatePart("UpperLeg", upperLeg, new Vector3(x, lowerLegLength + upperLegLength * 0.5f, 0), sortingOrder);
        CreatePart("LowerLegFoot", lowerLeg, new Vector3(x, lowerLegLength * 0.5f, 0), sortingOrder);
    }

    // 어깨 피벗(상완의 회전 중심, 몸통 자식)과 팔꿈치 피벗(하완의 회전 중심, 매 프레임 위치
    // 갱신)을 만들고 각 파츠를 그 피벗의 자식으로 붙인다. 파츠는 로컬 (0,-길이/2,0)에 둬서
    // 피벗을 회전시키면 "위쪽 끝이 피벗에 고정된 채" 자연스럽게 꺾이게 한다.
    private Transform CreateArm(string label, Sprite upper, Sprite lower, Vector3 shoulderLocalPos, out Transform elbowPivot)
    {
        var shoulderPivot = new GameObject($"{label}ShoulderPivot").transform;
        shoulderPivot.SetParent(transform, false);
        shoulderPivot.localPosition = shoulderLocalPos;

        var upperGo = new GameObject($"{label}UpperArm");
        upperGo.transform.SetParent(shoulderPivot, false);
        upperGo.transform.localPosition = new Vector3(0, -_upperArmLength * 0.5f, 0);
        upperGo.transform.localScale = Vector3.one * _partScale;
        var upperRenderer = upperGo.AddComponent<SpriteRenderer>();
        upperRenderer.sprite = upper;
        upperRenderer.sortingOrder = 1;

        elbowPivot = new GameObject($"{label}ElbowPivot").transform;
        elbowPivot.SetParent(transform, false);
        elbowPivot.position = shoulderPivot.position + Vector3.down * _upperArmLength;

        var lowerGo = new GameObject($"{label}LowerArmHand");
        lowerGo.transform.SetParent(elbowPivot, false);
        lowerGo.transform.localPosition = new Vector3(0, -_lowerArmLength * 0.5f, 0);
        lowerGo.transform.localScale = Vector3.one * _partScale;
        var lowerRenderer = lowerGo.AddComponent<SpriteRenderer>();
        lowerRenderer.sprite = lower;
        lowerRenderer.sortingOrder = 1;

        return shoulderPivot;
    }

    private SpriteRenderer CreatePart(string name, Sprite sprite, Vector3 localPos, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * _partScale;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    // 매 프레임 미니게임 쪽에서 호출해서 공용 제스처를 시각적으로 반영한다.
    public void ApplyPose(PlayerPoseState state)
    {
        if (_leftShoulderPivot == null || state == null || !state.IsTracked) return;

        ApplyArm(_leftShoulderPivot, _leftElbowPivot, state.Joints.leftShoulder, state.Joints.leftElbow, state.Joints.leftWrist);
        ApplyArm(_rightShoulderPivot, _rightElbowPivot, state.Joints.rightShoulder, state.Joints.rightElbow, state.Joints.rightWrist);

        float tiltDeg = Mathf.Clamp(state.HeadTiltRatio(), -1f, 1f) * -25f;
        _head.transform.localRotation = Quaternion.Euler(0, 0, tiltDeg);
    }

    private void ApplyArm(Transform shoulderPivot, Transform elbowPivot, Vector2 shoulder, Vector2 elbow, Vector2 wrist)
    {
        Vector2 upperDir = ImageDirection(shoulder, elbow);
        shoulderPivot.rotation = Quaternion.Euler(0, 0, RotationZFromDirection(upperDir));
        elbowPivot.position = shoulderPivot.position + (Vector3)(upperDir * _upperArmLength);

        Vector2 lowerDir = ImageDirection(elbow, wrist);
        elbowPivot.rotation = Quaternion.Euler(0, 0, RotationZFromDirection(lowerDir));
    }

    // MediaPipe 이미지 좌표(y 아래로 증가)를 "위가 양수"인 방향 벡터로 변환.
    private static Vector2 ImageDirection(Vector2 from, Vector2 to)
    {
        Vector2 d = new Vector2(to.x - from.x, from.y - to.y);
        return d.sqrMagnitude < 1e-6f ? Vector2.down : d.normalized;
    }

    // 파츠 스프라이트는 회전 0일 때 아래쪽(로컬 -Y)을 가리키도록 배치돼 있으므로, 그 기본
    // 방향을 목표 방향으로 맞추는 각도를 구한다.
    private static float RotationZFromDirection(Vector2 dir) =>
        Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;
}
