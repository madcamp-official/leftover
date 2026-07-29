// 원시인 캐릭터 표현. image/characters/character{1,2}/joints/front/의 실제 리깅 아트(머리, 몸통,
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

    [Tooltip("뒷모습처럼 카메라 반대편에서 본 캐릭터는 포즈의 좌우를 반전해 같은 동작으로 보이게 합니다.")]
    [SerializeField] private bool mirrorTrackedPoseHorizontally;

    [Header("씬/프리팹에 고정 배치된 파츠")]
    [SerializeField] private SpriteRenderer head;
    [SerializeField] private Transform leftShoulderPivot;
    [SerializeField] private Transform rightShoulderPivot;
    [SerializeField] private Transform leftElbowPivot;
    [SerializeField] private Transform rightElbowPivot;
    [SerializeField] private Transform leftUpperArm;
    [SerializeField] private Transform rightUpperArm;
    [SerializeField] private Transform leftLowerArm;
    [SerializeField] private Transform rightLowerArm;

    private Sprite _defaultHeadSprite;
    private float _leftUpperArmLength, _rightUpperArmLength;

    public Transform HeadTransform => head != null ? head.transform : null;

    private void Awake()
    {
        _defaultHeadSprite = head != null ? head.sprite : null;
        _leftUpperArmLength = DistanceFromPivot(leftShoulderPivot, leftUpperArm);
        _rightUpperArmLength = DistanceFromPivot(rightShoulderPivot, rightUpperArm);
    }

    private static float DistanceFromPivot(Transform pivot, Transform part)
        => pivot != null && part != null ? Mathf.Max(0.01f, Mathf.Abs(part.localPosition.y) * 2f) : 0.5f;

    // 상황별 표정으로 잠깐 바꾸고 싶을 때(예: 돌에 맞음, 바나나를 먹음) 사용. part는
    // "face_grimacing", "face_stone_hit_one_tooth_broken" 같은 ArtAssets.LoadCharacter 규칙.
    public void SetFace(string facePart)
    {
        if (head == null) return;
        Sprite sprite = ArtAssets.LoadCharacter(player, facePart);
        if (sprite != null) head.sprite = sprite;
    }

    public void ResetFace()
    {
        if (head != null) head.sprite = _defaultHeadSprite;
    }

    // 매 프레임 미니게임 쪽에서 호출해서 공용 제스처를 시각적으로 반영한다.
    public void ApplyPose(PlayerPoseState state)
    {
        if (leftShoulderPivot == null || state == null || !state.IsTracked) return;

        ApplyArm(leftShoulderPivot, leftElbowPivot, _leftUpperArmLength, state.Joints.leftShoulder,
            state.Joints.leftElbow, state.Joints.leftWrist, mirrorTrackedPoseHorizontally);
        ApplyArm(rightShoulderPivot, rightElbowPivot, _rightUpperArmLength, state.Joints.rightShoulder,
            state.Joints.rightElbow, state.Joints.rightWrist, mirrorTrackedPoseHorizontally);

        float tilt = state.HeadTiltRatio() * (mirrorTrackedPoseHorizontally ? -1f : 1f);
        float tiltDeg = Mathf.Clamp(tilt, -1f, 1f) * -25f;
        if (head != null) head.transform.localRotation = Quaternion.Euler(0, 0, tiltDeg);
    }

    private static void ApplyArm(Transform shoulderPivot, Transform elbowPivot, float upperArmLength,
        Vector2 shoulder, Vector2 elbow, Vector2 wrist, bool mirrorHorizontally)
    {
        Vector2 upperDir = ImageDirection(shoulder, elbow, mirrorHorizontally);
        shoulderPivot.rotation = Quaternion.Euler(0, 0, RotationZFromDirection(upperDir));
        // StoneThrow의 전경/원거리 캐릭터처럼 루트 Scale이 1이 아닐 때도 팔꿈치가 상완 끝에
        // 붙어 있도록 로컬 파츠 길이를 현재 월드 스케일로 변환한다.
        float worldUpperArmLength = upperArmLength * Mathf.Abs(shoulderPivot.lossyScale.y);
        elbowPivot.position = shoulderPivot.position + (Vector3)(upperDir * worldUpperArmLength);

        Vector2 lowerDir = ImageDirection(elbow, wrist, mirrorHorizontally);
        elbowPivot.rotation = Quaternion.Euler(0, 0, RotationZFromDirection(lowerDir));
    }

    // MediaPipe 이미지 좌표(y 아래로 증가)를 "위가 양수"인 방향 벡터로 변환.
    private static Vector2 ImageDirection(Vector2 from, Vector2 to, bool mirrorHorizontally)
    {
        Vector2 d = new Vector2(to.x - from.x, from.y - to.y);
        if (mirrorHorizontally) d.x = -d.x;
        return d.sqrMagnitude < 1e-6f ? Vector2.down : d.normalized;
    }

    // 파츠 스프라이트는 회전 0일 때 아래쪽(로컬 -Y)을 가리키도록 배치돼 있으므로, 그 기본
    // 방향을 목표 방향으로 맞추는 각도를 구한다.
    private static float RotationZFromDirection(Vector2 dir) =>
        Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;
}
