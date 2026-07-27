// 눈빛싸움 전용까진 아니지만 사실상 그 게임을 위해 만든 유틸: EAR이 임계값 밑으로
// "연속으로 얼마나 오래" 유지됐는지 추적한다. 순간 EAR만 보면 자연스러운 깜빡임도 바로
// "감음"으로 잡히기 때문에, 실제 판정은 이 지속시간을 기준으로 해야 한다.
using UnityEngine;

public class EyeCloseTimer : MonoBehaviour
{
    public PlayerId player;
    public float earThreshold = 0.18f;

    public float ClosedDuration { get; private set; }

    public bool IsClosedContinuously(float seconds) => ClosedDuration >= seconds;

    public void ResetTimer() => ClosedDuration = 0f;

    private void Update()
    {
        PlayerPoseState state = PoseInputHub.Instance != null ? PoseInputHub.Instance.Get(player) : null;
        if (state == null || !state.IsTracked)
        {
            ClosedDuration = 0f;
            return;
        }

        ClosedDuration = state.IsEyeClosedNow(earThreshold) ? ClosedDuration + Time.deltaTime : 0f;
    }
}
