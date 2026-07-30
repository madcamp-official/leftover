// 배포_아키텍처_설계.md 4장 1단계("Standalone 빌드 자체가 되는지부터 확인") 검증용 -
// 지금까지는 Editor Play로만 실행해봤고 실제 Standalone 빌드는 한 번도 시도된 적이
// 없었다. 이 메뉴는 리소스 누락/싱글턴 초기화 순서 문제처럼 Editor Play에서는 안 드러나는
// 빌드 전용 문제를 잡기 위한 개발용 빠른 빌드다 - 서명/공증/아이콘 같은 배포용 다듬기는
// 전혀 하지 않는다.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DevBuildTools
{
    // 리포 밖(프로젝트 상위 폴더)에 만든다 - Assets 밑에 빌드 산출물이 들어가면 Unity가
    // 그걸 또 에셋으로 임포트하려 들 수 있고, 용량도 커서 실수로 커밋될 위험이 있다.
    // 리포 루트의 .gitignore가 이미 Build/를 무시하므로 그 이름을 그대로 쓴다.
    private const string OutputRoot = "../Build";

    [MenuItem("Tools/UGAUGA/Build Dev Player (macOS)")]
    public static void BuildMac()
    {
        string path = Path.Combine(OutputRoot, "macOS", "pc-game.app");
        Build(BuildTarget.StandaloneOSX, path);
    }

    [MenuItem("Tools/UGAUGA/Build Dev Player (Windows)")]
    public static void BuildWindows()
    {
        string path = Path.Combine(OutputRoot, "Windows", "pc-game.exe");
        Build(BuildTarget.StandaloneWindows64, path);
    }

    private static void Build(BuildTarget target, string locationPath)
    {
        string[] scenes = System.Array.ConvertAll(
            System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled),
            s => s.path);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPath,
            target = target,
            options = BuildOptions.Development,
        };

        Debug.Log($"[DevBuildTools] 빌드 시작: target={target}, scenes={scenes.Length}개, 출력={locationPath}");
        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[DevBuildTools] 빌드 결과: {report.summary.result} " +
            $"(오류 {report.summary.totalErrors}개, 경고 {report.summary.totalWarnings}개, " +
            $"소요 {report.summary.totalTime.TotalSeconds:F1}초, 크기 {report.summary.totalSize / 1024 / 1024}MB)");
    }
}
