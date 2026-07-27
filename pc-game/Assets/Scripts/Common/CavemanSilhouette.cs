// 원시인 캐릭터의 공용 표현. image/characters/의 실제 아트 파츠(머리/몸통/양팔/양다리)를
// Resources/Characters/ 에서 불러와 조립하고, 손 든 상태/머리 기울기 같은 공용 제스처를
// 그대로 반영해서 보여준다. 6개 미니게임이 전부 이 컴포넌트를 공유하므로, public API
// (player / bodyColor / ApplyPose / SetFace / ResetFace)는 함부로 바꾸지 말 것.
//
// 리깅 구조: 관절마다 빈 GameObject("피벗")를 두고 스프라이트를 그 자식으로 반쪽 길이만큼
// 내려 달았다. 피벗을 회전시키면 관절을 축으로 자연스럽게 돌아간다 - 스프라이트를 직접
// 회전시키면 이미지 중심을 축으로 돌아서 팔이 몸에서 떨어져 보인다.
//
//   Root(몸통 중심)
//     ├ Torso
//     ├ HeadPivot(목)        → Head             : 머리 기울기
//     ├ ArmPivot(어깨) x2    → UpperArm
//     │    └ ForeArmPivot(팔꿈치) → LowerArm+Hand : 손 들기
//     └ LegPivot(고관절) x2  → UpperLeg
//          └ ShinPivot(무릎)  → LowerLeg+Foot
using UnityEngine;

public class CavemanSilhouette : MonoBehaviour
{
    public PlayerId player;

    // 이제 캐릭터가 색 틴트가 아니라 그림 자체로 구분되므로(character1 / character2) 렌더링에는
    // 쓰지 않는다. 기존 미니게임 코드가 참조할 수 있어 필드만 남겨둔다.
    public Color bodyColor = Color.white;

    // 캐릭터 전체 키(월드 유닛). 파츠마다 폭을 따로 지정하지 않고 이 키 하나로 균일 배율을
    // 계산해서 모든 파츠에 똑같이 적용한다 - 원본 아트가 서로 맞물리게 그려져 있으므로
    // 균일 배율이라야 관절이 어긋나지 않는다. character1/character2가 서로 다른 비율로
    // 그려져 있어도(실측: 몸통 가로세로비 0.96 vs 0.63) 각자 제 비율을 유지한 채 같은 키가 된다.
    private const float BodyHeight = 3.1f;

    // 몸통 폭 대비 어깨/고관절이 붙는 가로 위치.
    private const float ShoulderXRatio = 0.40f;
    private const float HipXRatio = 0.22f;

    // 머리를 몸통 위로 얼마나 겹칠지(머리 높이 대비) - 그림의 목 길이만큼 파묻어야 목이
    // 끊겨 보이지 않는다. 실측: character1은 아래 10%가 목, character2는 목이 훨씬 길다.
    private float HeadOverlapRatio => player == PlayerId.P1 ? 0.10f : 0.24f;

    // 팔 각도(도). 0 = 아래로 늘어뜨린 상태. 왼팔은 부호를 뒤집어 대칭으로 적용한다.
    private const float ArmRestDeg = 8f;
    private const float ArmRaisedDeg = 150f;
    private const float ForeArmRestDeg = 5f;
    private const float ForeArmRaisedDeg = 20f;
    private const float ArmLerpSpeed = 14f;

    private SpriteRenderer _head;
    private Transform _headPivot;
    private Transform _leftArmPivot, _rightArmPivot;
    private Transform _leftForeArmPivot, _rightForeArmPivot;

    private Sprite _defaultHeadSprite;
    private float _leftArmDeg, _rightArmDeg;
    private float _leftForeArmDeg, _rightForeArmDeg;

    private void Start()
    {
        // Awake()가 아니라 Start()인 이유: 호출부가
        // `var s = go.AddComponent<CavemanSilhouette>(); s.player = id;` 식으로 쓰는데,
        // AddComponent는 Awake()를 그 자리에서 동기 실행하므로 Awake()에서 player를 읽으면
        // 호출부가 대입하기 전의 기본값(P1)만 보게 된다. Start()는 다음 프레임이라 안전하다.
        BuildRig();
    }

    private float _scale; // 원본 px -> 월드 유닛 균일 배율 (모든 파츠에 동일 적용)

    private void BuildRig()
    {
        Sprite torsoSprite = ArtAssets.LoadCharacter(player, "torso");
        Sprite headSprite = ArtAssets.LoadCharacter(player, "head");
        Sprite upperLegSprite = ArtAssets.LoadCharacter(player, "left_upper_leg");
        Sprite lowerLegSprite = ArtAssets.LoadCharacter(player, "left_lower_leg_foot");

        // 머리+몸통+다리 원본 높이의 합이 BodyHeight가 되도록 배율을 한 번만 정하고, 이후
        // 모든 파츠에 그대로 쓴다. 파츠별로 폭을 따로 맞추면 원본에서 서로 맞물리게 그려진
        // 관절 크기가 제각각이 되어 팔다리가 끊어져 보인다.
        float nativeHeight = NativeSize(headSprite).y + NativeSize(torsoSprite).y
            + NativeSize(upperLegSprite).y + NativeSize(lowerLegSprite).y;
        _scale = nativeHeight > 0f ? BodyHeight / nativeHeight : 1f;

        Vector2 torso = NativeSize(torsoSprite) * _scale;
        CreatePart("Torso", torsoSprite, transform, Vector3.zero, sortingOrder: 0);

        float torsoTop = torso.y * 0.5f;
        float torsoBottom = -torso.y * 0.5f;

        // 머리 - 목(몸통 위쪽)을 축으로 기울어진다. 목이 끊겨 보이지 않게 몸통 위로 살짝 겹친다.
        _defaultHeadSprite = headSprite;
        Vector2 head = NativeSize(headSprite) * _scale;
        _headPivot = CreatePivot("HeadPivot", transform, new Vector3(0f, torsoTop - head.y * HeadOverlapRatio, 0f));
        _head = CreatePart("Head", headSprite, _headPivot, new Vector3(0f, head.y * 0.5f, 0f), sortingOrder: 3);

        float shoulderY = torsoTop - torso.y * 0.12f;
        BuildArm(screenRight: false, shoulder: new Vector3(-torso.x * ShoulderXRatio, shoulderY, 0f),
            out _leftArmPivot, out _leftForeArmPivot);
        BuildArm(screenRight: true, shoulder: new Vector3(torso.x * ShoulderXRatio, shoulderY, 0f),
            out _rightArmPivot, out _rightForeArmPivot);

        float hipY = torsoBottom + torso.y * 0.06f;
        BuildLeg(screenRight: false, hip: new Vector3(-torso.x * HipXRatio, hipY, 0f));
        BuildLeg(screenRight: true, hip: new Vector3(torso.x * HipXRatio, hipY, 0f));

        _leftArmDeg = -ArmRestDeg;
        _rightArmDeg = ArmRestDeg;
        _leftForeArmDeg = -ForeArmRestDeg;
        _rightForeArmDeg = ForeArmRestDeg;
        ApplyArmAngles();
    }

    // 팔다리 파츠는 끝에 둥근 "볼 조인트"가 그려져 있어서, 스프라이트를 전체 높이 기준으로
    // 이어 붙이면 볼 하나만큼 어긋나 관절이 끊어져 보인다. 아래 값은 각 PNG의 알파 픽셀을
    // 직접 측정해서 얻은 볼 중심 위치(파츠 높이 대비 비율, 위에서부터) - 볼 중심은 끝에서
    // 반지름만큼 안쪽이다. character1/character2가 서로 다른 비율로 그려져 있어 따로 둔다.
    // (종아리/팔뚝의 Bottom은 발·손이라 관절로 쓰지 않지만 대칭을 위해 같이 적어둔다.)
    // 볼 조인트 중심의 위치(파츠 내용 크기 대비 비율, 왼쪽 위 모서리 기준). 세로뿐 아니라
    // 가로도 필요하다 - 종아리처럼 발이 옆으로 뻗은 파츠는 무릎 볼이 이미지 가로 중앙에서
    // 크게 벗어나 있어서(실측 0.28/0.24), 스프라이트 중심만 맞추면 무릎이 어긋나고 다리가
    // 벌어진다.
    private readonly struct Joint
    {
        public readonly float TopX, TopY, BottomX, BottomY;
        public Joint(float topX, float topY, float bottomX, float bottomY)
        { TopX = topX; TopY = topY; BottomX = bottomX; BottomY = bottomY; }
    }

    // 각 PNG의 알파 픽셀을 직접 측정해서 얻은 값 ("left" 아트 기준).
    private Joint LeftArtJoint(string part)
    {
        if (player == PlayerId.P1)
        {
            if (part.EndsWith("upper_arm")) return new Joint(0.499f, 0.175f, 0.501f, 0.874f);
            if (part.EndsWith("lower_arm_hand")) return new Joint(0.601f, 0.119f, 0.628f, 0.886f);
            if (part.EndsWith("upper_leg")) return new Joint(0.499f, 0.182f, 0.505f, 0.865f);
            return new Joint(0.284f, 0.136f, 0.529f, 0.812f); // lower_leg_foot
        }
        if (part.EndsWith("upper_arm")) return new Joint(0.499f, 0.103f, 0.490f, 0.917f);
        if (part.EndsWith("lower_arm_hand")) return new Joint(0.582f, 0.098f, 0.553f, 0.895f);
        if (part.EndsWith("upper_leg")) return new Joint(0.499f, 0.101f, 0.512f, 0.932f);
        return new Joint(0.238f, 0.100f, 0.583f, 0.848f); // lower_leg_foot
    }

    // "right_" 아트는 "left_"를 좌우로 뒤집어 그린 것이라 X만 대칭시키면 된다(실측 확인).
    private Joint JointOf(string artPart)
    {
        Joint j = LeftArtJoint(artPart);
        return artPart.StartsWith("right_")
            ? new Joint(1f - j.TopX, j.TopY, 1f - j.BottomX, j.BottomY)
            : j;
    }

    // 스프라이트 중심을 원점으로 봤을 때의 볼 조인트 위치.
    private static Vector2 BallOffset(Vector2 size, float xRatio, float yRatio)
        => new Vector2((xRatio - 0.5f) * size.x, (0.5f - yRatio) * size.y);

    // 위쪽 파츠의 "아래 볼"과 아래쪽 파츠의 "위 볼"이 정확히 같은 점에 오도록 잇는다.
    private void BuildLimb(string namePrefix, string upperPart, string lowerPart, Transform root,
        Vector3 jointPos, int sortingOrder, out Transform upperPivot, out Transform lowerPivot)
    {
        Sprite upper = ArtAssets.LoadCharacter(player, upperPart);
        Sprite lower = ArtAssets.LoadCharacter(player, lowerPart);
        Joint uj = JointOf(upperPart);
        Joint lj = JointOf(lowerPart);

        Vector2 u = NativeSize(upper) * _scale;
        Vector2 uTop = BallOffset(u, uj.TopX, uj.TopY);
        Vector2 uBottom = BallOffset(u, uj.BottomX, uj.BottomY);
        upperPivot = CreatePivot($"{namePrefix}UpperPivot", root, jointPos);
        CreatePart($"{namePrefix}Upper", upper, upperPivot, -uTop, sortingOrder);

        Vector2 l = NativeSize(lower) * _scale;
        Vector2 lTop = BallOffset(l, lj.TopX, lj.TopY);
        lowerPivot = CreatePivot($"{namePrefix}LowerPivot", upperPivot, uBottom - uTop);
        CreatePart($"{namePrefix}Lower", lower, lowerPivot, -lTop, sortingOrder);
    }

    // 캐릭터가 화면을 마주보고 서 있으므로 캐릭터의 왼팔/왼다리가 화면 오른쪽에 온다(거울상).
    // 그래서 화면 오른쪽에는 "left_" 아트를, 화면 왼쪽에는 "right_" 아트를 붙여야 손발이
    // 바깥쪽을 향한다. 반대로 붙이면 발끝이 서로 안쪽을 향해 다리가 U자로 모인다.
    private static string ArtSide(bool screenRight) => screenRight ? "left" : "right";

    private void BuildArm(bool screenRight, Vector3 shoulder, out Transform armPivot, out Transform foreArmPivot)
    {
        string art = ArtSide(screenRight);
        BuildLimb(screenRight ? "rightArm" : "leftArm", $"{art}_upper_arm", $"{art}_lower_arm_hand",
            transform, shoulder, sortingOrder: 2, // 몸통보다 앞, 머리보다 뒤.
            out armPivot, out foreArmPivot);
    }

    private void BuildLeg(bool screenRight, Vector3 hip)
    {
        string art = ArtSide(screenRight);
        BuildLimb(screenRight ? "rightLeg" : "leftLeg", $"{art}_upper_leg", $"{art}_lower_leg_foot",
            transform, hip, sortingOrder: -1, // 몸통 뒤로 보내 골반이 자연스럽게 가려지도록.
            out _, out _);
    }

    private static Transform CreatePivot(string name, Transform parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go.transform;
    }

    // 모든 파츠에 같은 _scale을 적용한다 - 원본 아트의 상대 비율(관절 크기 포함)이 그대로 유지된다.
    private SpriteRenderer CreatePart(string name, Sprite sprite, Transform parent,
        Vector3 localPos, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * _scale;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    // 스케일 적용 전 스프라이트 크기(월드 유닛). 못 불러왔으면 0을 리턴해서 배율 계산에서 빠진다.
    private static Vector2 NativeSize(Sprite sprite)
        => sprite == null ? Vector2.zero : (Vector2)sprite.bounds.size;

    // 매 프레임 미니게임 쪽에서 호출해서 공용 제스처를 시각적으로 반영한다.
    public void ApplyPose(PlayerPoseState state)
    {
        // Start()가 아직 안 돈 드문 타이밍(생성 직후 같은 프레임 호출 등)이면 조용히 무시한다.
        if (_headPivot == null || _leftArmPivot == null || _rightArmPivot == null) return;
        if (state == null || !state.IsTracked) return;

        bool leftRaised = state.IsHandRaised(rightHand: false);
        bool rightRaised = state.IsHandRaised(rightHand: true);

        // 거울 모드라 사용자의 오른손 = 화면 오른쪽. 왼팔은 부호를 뒤집어 대칭으로 올린다.
        // 뚝뚝 끊기지 않게 목표 각도로 부드럽게 수렴시킨다(프레임레이트 독립 지수 감쇠).
        float t = 1f - Mathf.Exp(-ArmLerpSpeed * Time.deltaTime);
        _leftArmDeg = Mathf.Lerp(_leftArmDeg, leftRaised ? -ArmRaisedDeg : -ArmRestDeg, t);
        _rightArmDeg = Mathf.Lerp(_rightArmDeg, rightRaised ? ArmRaisedDeg : ArmRestDeg, t);
        _leftForeArmDeg = Mathf.Lerp(_leftForeArmDeg, leftRaised ? -ForeArmRaisedDeg : -ForeArmRestDeg, t);
        _rightForeArmDeg = Mathf.Lerp(_rightForeArmDeg, rightRaised ? ForeArmRaisedDeg : ForeArmRestDeg, t);
        ApplyArmAngles();

        float tiltDeg = Mathf.Clamp(state.HeadTiltRatio(), -1f, 1f) * -25f;
        _headPivot.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
    }

    private void ApplyArmAngles()
    {
        _leftArmPivot.localRotation = Quaternion.Euler(0f, 0f, _leftArmDeg);
        _rightArmPivot.localRotation = Quaternion.Euler(0f, 0f, _rightArmDeg);
        _leftForeArmPivot.localRotation = Quaternion.Euler(0f, 0f, _leftForeArmDeg);
        _rightForeArmPivot.localRotation = Quaternion.Euler(0f, 0f, _rightForeArmDeg);
    }

    // 표정 교체. 표정 에셋이 얼굴만이 아니라 "머리 전체" 그림이라 머리 스프라이트를 통째로
    // 갈아끼운다. faceName은 Resources/Characters/의 접미사 그대로:
    // "face_grimacing", "face_stone_hit_one_tooth_broken", "face_stone_hit_two_teeth_broken" 등.
    public void SetFace(string faceName)
    {
        if (_head == null) return;
        Sprite sprite = ArtAssets.LoadCharacter(player, faceName);
        if (sprite != null) _head.sprite = sprite;
    }

    public void ResetFace()
    {
        if (_head == null || _defaultHeadSprite == null) return;
        _head.sprite = _defaultHeadSprite;
    }
}
