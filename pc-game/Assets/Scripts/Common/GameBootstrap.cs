// 각 미니게임 씬이 Hub를 거치지 않고 단독으로 열려도(Unity 에디터에서 그 씬만 바로 Play)
// 동작하도록, 필요한 싱글턴들이 없으면 만들어준다. Hub 씬에서 이미 만들어놨으면 각 싱글턴의
// Awake()에서 중복 인스턴스를 스스로 파괴하므로 여기서 중복 생성을 걱정할 필요 없다.
using UnityEngine;

public static class GameBootstrap
{
    public static void EnsureInputSystems()
    {
        if (PoseInputHub.Instance == null)
        {
            var go = new GameObject("PoseInput");
            go.AddComponent<PoseInputHub>();
            go.AddComponent<PoseStreamReceiver>();
            go.AddComponent<CameraPreviewReceiver>();
        }
        else if (CameraPreviewReceiver.Instance == null)
        {
            var go = new GameObject("CameraPreviewReceiver");
            go.AddComponent<CameraPreviewReceiver>();
        }
    }

    public static void EnsureMatchController()
    {
        if (MatchController.Instance == null)
        {
            var go = new GameObject("MatchController");
            go.AddComponent<MatchController>();
        }
    }

    public static void EnsureNetwork()
    {
        if (NetworkSession.Instance == null)
        {
            var go = new GameObject("NetworkSession");
            go.AddComponent<NetworkSession>();
        }
    }

    // 미니게임 씬 단독 실행 시엔 일부러 안 만든다 - 실제 카메라를 켜는 무거운 서브프로세스라,
    // DebugPoseController/PoseSimulator로 카메라 없이 빠르게 반복 테스트하는 기존 개발
    // 워크플로를 방해하면 안 된다(Assets/Scripts/Common/README.md 참고). Hub에서 역할이
    // 실제로 정해질 때만(HubController) 명시적으로 호출한다.
    public static void EnsureVisionServerLauncher()
    {
        if (VisionServerLauncher.Instance == null)
        {
            var go = new GameObject("VisionServerLauncher");
            go.AddComponent<VisionServerLauncher>();
        }
    }
}
