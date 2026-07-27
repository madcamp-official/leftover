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
}
