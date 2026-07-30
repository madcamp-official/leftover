// 온라인 2인 플레이에서 호스트 PC는 UDP 9100/9101(포즈/프리뷰 수신), TCP 9200(게임 이벤트)
// 인바운드를 받아야 한다. Windows Defender 방화벽이 기본값(켜짐)인 채로는 이 인바운드가
// 막혀서, 예전에는 사용자가 "Windows Defender 방화벽" 설정에 직접 들어가 인바운드 규칙을
// 손으로 추가해야 했다(멀티플레이_실행_명령어.md 7장) - "실행파일 하나 더블클릭하면 다
// 된다"는 목표(배포_아키텍처_설계.md)에 안 맞는 부분이라 자동화한다.
//
// macOS는 이게 필요 없다 - macOS 응용 프로그램 방화벽은 기본적으로 꺼져 있고, 사용자가 켜둔
// 경우에도 앱이 인바운드를 처음 받을 때 OS가 알아서 "수신 연결 허용?" 한 번 클릭 대화상자를
// 띄워준다(코드 개입 불필요, ad-hoc 서명이어도 동작 - 배포_아키텍처_설계.md 3장 참고).
//
// Windows는 방화벽 인바운드 규칙 추가 자체가 관리자 권한이 필요해서, 완전히 조용히 처리할
// 수는 없다 - "호스트로 시작"을 처음 누른 순간 Windows의 UAC(사용자 계정 컨트롤) 승인
// 대화상자가 한 번 뜨고, 승인하면 그 뒤로는 이 머신에서 다시 안 뜬다(PlayerPrefs로 "이미
// 시도했음"을 기억 - 사용자가 거부해도 매번 다시 묻지 않는다, 재시도하려면 설정을 지워야 함).
//
// **주의: 이 파일은 실제 Windows 머신에서 실측 검증된 적이 없다** - PyInstaller와 마찬가지로
// 이 기능을 만든 개발 환경(macOS)에서는 Windows 방화벽 API를 테스트할 방법이 없다. 팀원이
// Windows에서 처음 호스트로 시작해볼 때 UAC 대화상자가 실제로 뜨는지, 규칙이 제대로
// 추가되는지 확인이 필요하다 - 안 되면 vision-server/README.md나
// 멀티플레이_실행_명령어.md 7장의 수동 방화벽 설정 절차로 우회 가능하다.
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class WindowsFirewallHelper
{
    private const string PrefKey = "windows_firewall_rules_attempted_v1";
    private const string RuleNameUdp = "UgaUgaGame-UDP";
    private const string RuleNameTcp = "UgaUgaGame-TCP";

    // NetworkSession.StartHost()에서 호출한다 - 호스트만 인바운드가 필요하므로 클라이언트는
    // 건드리지 않는다. 방화벽 규칙 추가는 몇 초 걸릴 수 있고 실패해도 호스팅 자체를 막을
    // 이유가 없으므로 백그라운드에서 fire-and-forget으로 처리한다.
    public static void EnsureInboundRulesAsync()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer) return;
        if (PlayerPrefs.GetInt(PrefKey, 0) == 1) return;

        // 성공/실패/사용자 거부 여부와 무관하게 "한 번 시도했음"만 바로 기록한다 - 매번 실행할
        // 때마다 UAC를 다시 띄우는 게 훨씬 나쁜 경험이라, 재시도는 PlayerPrefs를 지우거나
        // 재설치한 경우로만 제한한다.
        PlayerPrefs.SetInt(PrefKey, 1);
        PlayerPrefs.Save();

        Task.Run(() =>
        {
            try
            {
                AddRules();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WindowsFirewallHelper] 방화벽 규칙 추가 실패(무시하고 계속 진행): {e.Message}");
            }
        });
    }

    private static void AddRules()
    {
        // add rule 자체가 관리자 권한 필요 - cmd.exe를 runas로 한 번만 띄워서 두 규칙을 같이
        // 추가한다(UAC 대화상자 1번만 뜨게 하려고 netsh를 두 번 따로 실행하지 않음).
        string script =
            $"netsh advfirewall firewall add rule name=\"{RuleNameUdp}\" dir=in action=allow " +
            "protocol=UDP localport=9100,9101 profile=any & " +
            $"netsh advfirewall firewall add rule name=\"{RuleNameTcp}\" dir=in action=allow " +
            "protocol=TCP localport=9200 profile=any";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {script}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using Process process = Process.Start(psi);
            process?.WaitForExit(15000);
            Debug.Log("[WindowsFirewallHelper] 방화벽 인바운드 규칙 추가 시도 완료 (UAC 승인 여부와 무관하게 여기 도달).");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 사용자가 UAC 승인을 거부하면(ERROR_CANCELLED) 여기로 들어온다 - 방화벽 규칙 없이
            // 계속 진행, 클라이언트가 접속을 못 하면 수동 설정 안내(README)로 우회해야 한다.
            Debug.LogWarning("[WindowsFirewallHelper] 관리자 권한 승인이 거부되어 방화벽 규칙을 추가하지 못했습니다. " +
                "온라인 2인 플레이에서 상대가 접속을 못 하면 Windows Defender 방화벽에서 " +
                "UDP 9100/9101, TCP 9200 인바운드를 수동으로 허용해야 합니다.");
        }
    }
}
