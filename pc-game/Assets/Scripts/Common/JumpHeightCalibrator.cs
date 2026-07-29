// 점프해서 과일따기 전용 - 엉덩이 중점(HipMid) y좌표의 "가만히 서 있는" 기준선을 짧게 잡은
// 뒤, 그 대비 상승 높이를 몸통 길이로 정규화해서 리턴한다. 키/카메라 거리 차이에 영향받지
// 않게 절대 화면 높이가 아니라 항상 "이 사람 몸통 대비 얼마나 떴는지"로만 잰다.
//
// 발목(Ankle) 대신 엉덩이 중점을 쓰는 이유: 점프 정점에서 카메라 프레임 하단에 가까운 발이
// 잘리거나 포즈 추정이 흔들리기 쉬운 반면, 몸통 중심에 가까운 엉덩이는 상대적으로 프레임
// 안쪽에 머물고 추정이 더 안정적이다.
using UnityEngine;

public class JumpHeightCalibrator : MonoBehaviour
{
    public PlayerId player;
    public float calibrationSeconds = 1.5f;

    // 테스트용 임시 스위치: true면 평균 없이 트래킹되는 첫 프레임의 값을 그대로 기준선으로
    // 확정한다(1.5초 대기 스킵). 실제 플레이 전에는 false로 되돌릴 것.
    public bool skipCalibrationWait = false;

    public bool IsCalibrated { get; private set; }

    private float _baselineHipY;
    private float _calibTimer;

    private void Update()
    {
        PlayerPoseState state = PoseInputHub.Instance != null ? PoseInputHub.Instance.Get(player) : null;
        if (state == null || !state.IsTracked || IsCalibrated)
            return;

        // 공통 로딩 화면에서 이미 기준 자세를 수집했다면 게임 씬에서 다시 기다리지 않는다.
        if (state.IsCalibrated)
        {
            _baselineHipY = state.BaselineHipY;
            IsCalibrated = true;
            return;
        }

        if (skipCalibrationWait)
        {
            _baselineHipY = state.Joints.HipMid.y;
            IsCalibrated = true;
            return;
        }

        float hipY = state.Joints.HipMid.y;
        _baselineHipY = _calibTimer <= 0f ? hipY : Mathf.Lerp(_baselineHipY, hipY, 0.15f);
        _calibTimer += Time.deltaTime;
        if (_calibTimer >= calibrationSeconds)
            IsCalibrated = true;
    }

    public void Recalibrate()
    {
        IsCalibrated = false;
        _calibTimer = 0f;
    }

    // 0 이상, 몸통 길이 대비 상승 비율 (뛸수록 커짐). 캘리브레이션 전이거나 추적이 끊기면 0.
    public float GetJumpHeight()
    {
        if (!IsCalibrated) return 0f;
        PlayerPoseState state = PoseInputHub.Instance != null ? PoseInputHub.Instance.Get(player) : null;
        if (state == null || !state.IsTracked) return 0f;

        float torso = state.Joints.TorsoLength;
        if (torso <= 0f) return 0f;
        float hipY = state.Joints.HipMid.y;
        // 이미지 y는 아래로 증가하므로, 기준선보다 작아졌다는 건 위로 올라갔다는 뜻.
        return Mathf.Max(0f, (_baselineHipY - hipY) / torso);
    }
}
