// 배포_아키텍처_설계.md 1장(방식 A) - vision-server(Python/MediaPipe)를 게임이 직접 켜고
// 끄는 백그라운드 서브프로세스로 감싼다. Hub 씬에서 역할(호스트/클라이언트/1인)이 정해지는
// 순간 EnsureRunning()을 호출해 로컬 vision-server 하나를 그 역할에 맞는 인자로 띄운다 -
// 멀티플레이_분산_아키텍처_설계.md 2장에 따라 P1/P2 vision-server 모두 "호스트 PC의 LAN
// IP"를 --pc-ip로 겨냥해야 하므로(포즈는 항상 호스트로만 모인다), 호스트 자신도 자기
// LocalAddressHint를, 클라이언트는 접속에 쓴 호스트 IP를 그대로 넘긴다. 씬을 넘나들어야
// 하므로 다른 싱글턴들과 동일하게 DontDestroyOnLoad로 유지한다.
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class VisionServerLauncher : MonoBehaviour
{
    private static VisionServerLauncher _instance;

    public static VisionServerLauncher Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<VisionServerLauncher>();
            return _instance;
        }
        private set => _instance = value;
    }

    private Process _process;
    private string _runningPlayerId;
    private string _runningPcIp;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 이미 같은 인자로 돌고 있으면 아무것도 안 한다(멱등) - Hub 화면을 왔다갔다 하거나
    // 호스트 패널을 다시 열어도 매번 새로 켜지 않는다. 인자가 다르면(예: 클라이언트가 잘못된
    // IP로 접속 시도했다가 다른 IP로 재시도) 기존 프로세스를 죽이고 새 인자로 다시 켠다.
    public void EnsureRunning(string playerId, string pcIp)
    {
        if (IsRunning && _runningPlayerId == playerId && _runningPcIp == pcIp) return;

        StopProcess();

        string exePath = ResolveBinaryPath();
        if (exePath == null)
        {
            Debug.LogError(
                "[VisionServerLauncher] vision-server 실행파일을 찾지 못했습니다 - "
                + "vision-server/vision-server.spec으로 PyInstaller 빌드를 먼저 만들어야 합니다 "
                + "(Editor Play 중이라면 리포 안 vision-server/dist/vision-server/를 찾습니다).");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            // --voice: 이 프로세스 하나가 매치 전체(7개 미니게임, 그 중 소리지르기 포함)를
            // 담당하므로 항상 켜둔다. --no-show: 백그라운드 자동 실행이라 디버그 카메라 창을
            // 띄우면 안 된다(main.py --no-show 참고, q키 종료도 같이 꺼지므로 Kill()로 직접
            // 종료해야 함).
            Arguments = $"--pc-ip {pcIp} --player-id {playerId} --voice --no-show",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[vision-server:{playerId}] {e.Data}"); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.LogWarning($"[vision-server:{playerId}] {e.Data}"); };
            _process.Exited += (_, __) => Debug.LogWarning($"[VisionServerLauncher] vision-server({playerId}) 프로세스가 종료됐습니다 (exit={_process.ExitCode}).");
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _runningPlayerId = playerId;
            _runningPcIp = pcIp;
            Debug.Log($"[VisionServerLauncher] 실행: {exePath} {psi.Arguments}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VisionServerLauncher] 실행 실패: {e.Message}\n{exePath}");
            _process = null;
        }
    }

    public bool IsRunning => _process != null && !_process.HasExited;

    // 카메라 장치 선택(SettingsScreen)은 아직 vision-server 실행 인자로 안 넘어간다 - Unity의
    // WebCamTexture.devices/Microphone.devices 이름과 vision-server가 쓰는 OpenCV
    // --camera-index/sounddevice --voice-device 정수 인덱스 사이에 확실한 대응 관계가 없어서
    // (서로 다른 라이브러리의 열거 순서), 잘못 매핑해서 엉뚱한 장치를 골라버리는 게 기본값(0/
    // 시스템 기본)을 쓰는 것보다 더 나쁘다고 판단해 일부러 비워뒀다 - 해결하려면 먼저 두
    // 라이브러리의 장치 이름을 실측으로 비교해 신뢰할 수 있는 매핑을 확인해야 한다.

    private static string ResolveBinaryPath()
    {
        string exeName = Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.WindowsEditor
            ? "vision-server.exe" : "vision-server";

        // 배포_아키텍처_설계.md 3장: 빌드 결과물 옆에 vision-server를 동봉할 예정이지만 그
        // 복사 후처리 스크립트는 아직 없다(TODO) - 일단은 실행파일 바로 옆 vision-server/
        // 폴더를 잠정 규칙으로 삼는다(macOS는 Application.dataPath의 부모가 .app/Contents/
        // Resources라 자연스럽게 번들 안쪽이 된다. Windows는 빌드 루트 옆).
        string buildRoot = Path.GetDirectoryName(Application.dataPath);
        string bundled = buildRoot == null ? null : Path.Combine(buildRoot, "vision-server", exeName);
        if (bundled != null && File.Exists(bundled)) return Path.GetFullPath(bundled);

        // 위 복사 단계가 아직 없으므로, 리포를 통째로 갖고 있는 개발 중(Editor Play 또는
        // 이 리포 옆에서 만든 Standalone 빌드 테스트)에는 PyInstaller 산출물을 상대 경로로
        // 바로 찾아 쓴다 - 배포용 실행파일만 따로 받은 사람에게는 이 경로가 없을 테니 안전하게
        // null로 빠진다.
        string devPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "vision-server", "dist", "vision-server", exeName));
        return File.Exists(devPath) ? devPath : null;
    }

    private void StopProcess()
    {
        if (_process == null) return;
        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VisionServerLauncher] 종료 중 예외(무시): {e.Message}");
        }
        _process.Dispose();
        _process = null;
        _runningPlayerId = null;
        _runningPcIp = null;
    }

    // 정상 종료 시 확실히 자식 프로세스를 같이 끈다. 강제 종료/크래시로 이 콜백 자체가 안
    // 불리는 경우(부모가 죽어도 자식이 남는 문제)는 배포_아키텍처_설계.md 5장에 TODO로 남아
    // 있는 별도 워치독 작업 - 지금 범위 밖.
    private void OnApplicationQuit() => StopProcess();
    private void OnDestroy() => StopProcess();
}
