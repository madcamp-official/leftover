// 2대 노트북 실측 테스트에서는 각자의 Unity 콘솔을 서로 실시간으로 볼 수 없다 - 그래서
// 모든 Debug.Log/Warning/Error를 로컬 텍스트 파일에도 그대로 남긴다. 테스트 후 이 파일
// 내용만 복사해서 공유하면 된다(양쪽 다 보내야 호스트/클라이언트 타임라인을 나란히 비교할
// 수 있다 - 파일 첫 줄에 역할/커맨드라인 인자를 같이 남기는 이유).
//
// Application.persistentDataPath는 macOS/Windows 어디서든 항상 쓰기 가능하고, 부팅 시점에
// 정확한 경로를 콘솔에 출력하므로 찾기 쉽다. 새 프레임 로그를 추가할 때마다 즉시 flush하는
// File.AppendAllText를 쓴다 - 게임이 중간에 강제 종료돼도 그때까지 로그는 남아있어야 한다.
using System;
using System.IO;
using UnityEngine;

public static class DebugFileLogger
{
    private const string FileName = "uga_uga_debug_log.txt";
    private static string _path;
    private static bool _initialized;

    public static string FilePath => _path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_initialized) return;
        _initialized = true;

        // Application.persistentDataPath는 macOS(~/Library/Application Support/...)와
        // Windows(AppData\LocalLow\...) 둘 다 기본적으로 숨김/시스템 폴더 취급이라 Finder/
        // 탐색기로 찾기 어렵다는 실측 피드백이 있었다 - 바탕화면처럼 항상 바로 보이는
        // 위치에 우선 써보고, 실패하면(권한 등) persistentDataPath로 대체한다.
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _path = Path.Combine(string.IsNullOrEmpty(desktop) ? Application.persistentDataPath : desktop, FileName);
        try
        {
            WriteHeader();
        }
        catch (Exception)
        {
            _path = Path.Combine(Application.persistentDataPath, FileName);
            try
            {
                WriteHeader();
            }
            catch (Exception e2)
            {
                Debug.LogWarning($"[DebugFileLogger] 로그 파일을 만들지 못했습니다: {e2.Message}");
                _path = null;
                return;
            }
        }

        Application.logMessageReceived += OnUnityLog;
        Debug.Log($"[DebugFileLogger] 로그 파일 위치: {_path}");
    }

    private static void WriteHeader()
    {
        File.WriteAllText(_path,
            $"=== 우가우가게임 디버그 로그 시작 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n" +
            $"커맨드라인 인자: {string.Join(" ", Environment.GetCommandLineArgs())}\n\n");
    }

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (_path == null) return;
        try
        {
            File.AppendAllText(_path, $"[{Time.realtimeSinceStartup:F2}s][{type}] {condition}\n");
        }
        catch
        {
            // 로그 파일 쓰기 실패로 게임이 죽으면 안 되므로 조용히 무시한다.
        }
    }
}
