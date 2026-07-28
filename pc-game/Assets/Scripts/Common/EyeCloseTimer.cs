// 눈빛싸움 전용까진 아니지만 사실상 그 게임을 위해 만든 유틸: EAR이 임계값 밑으로
// "연속으로 얼마나 오래" 유지됐는지 추적한다. 순간 EAR만 보면 자연스러운 깜빡임도 바로
// "감음"으로 잡히기 때문에, 실제 판정은 이 지속시간을 기준으로 해야 한다.
//
// ClosedDuration만 보면 눈을 감았다 뜨기를 반복해서 계속 리셋시키는 식으로 무한정 버틸 수
// 있다. 그래서 눈을 뜨더라도 리셋되지 않는 TotalClosedDuration(라운드 전체 누적 감은 시간)도
// 같이 추적한다 - 깜빡임을 아무리 반복해도 결국 이 누적치로 패배가 확정된다.
using UnityEngine;

public class EyeCloseTimer : MonoBehaviour
{
    public PlayerId player;
    public float earThreshold = 0.18f;

    public float ClosedDuration { get; private set; }
    public float TotalClosedDuration { get; private set; }

    public bool IsClosedContinuously(float seconds) => ClosedDuration >= seconds;

    public void ResetTimer()
    {
        ClosedDuration = 0f;
        TotalClosedDuration = 0f;
    }

    private void Update()
    {
        PlayerPoseState state = PoseInputHub.Instance != null ? PoseInputHub.Instance.Get(player) : null;
        if (state == null || !state.IsTracked)
        {
            ClosedDuration = 0f;
            return;
        }

        if (state.IsEyeClosedNow(earThreshold))
        {
            ClosedDuration += Time.deltaTime;
            TotalClosedDuration += Time.deltaTime;
        }
        else
        {
            ClosedDuration = 0f;
        }
    }
}
