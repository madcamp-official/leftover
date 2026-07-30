// 배포_아키텍처_설계.md 4장 1단계("Standalone 빌드 자체가 되는지부터 확인") 검증용으로
// 시작했다가, 3장(vision-server 동봉) + "dmg 실행하면 다 된다" 요구까지 이어서 담당하는
// 개발용 빌드 메뉴. 서명/공증/아이콘 같은 배포용 다듬기는 여전히 안 한다 - 몰입캠프 데모
// 규모에서는 "고급 정보 → 실행"으로 우회 가능하다는 문서 판단 그대로.
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class DevBuildTools
{
    // 리포 밖(프로젝트 상위 폴더)에 만든다 - Assets 밑에 빌드 산출물이 들어가면 Unity가
    // 그걸 또 에셋으로 임포트하려 들 수 있고, 용량도 커서 실수로 커밋될 위험이 있다.
    // 리포 루트의 .gitignore가 이미 Build/를 무시하므로 그 이름을 그대로 쓴다.
    private const string OutputRoot = "../Build";

    // vision-server/vision-server.spec으로 PyInstaller가 만드는 산출물 - onedir 빌드라
    // 실행파일 하나가 아니라 폴더 통째로(실행파일 + _internal/) 옮겨야 한다. 이 리포를
    // 통째로 갖고 있는 개발 머신 기준 상대 경로 - VisionServerLauncher.ResolveBinaryPath()의
    // 개발용 폴백 경로와 반드시 같은 곳을 가리켜야 한다.
    private const string VisionServerDistRoot = "../vision-server/dist/vision-server";

    [MenuItem("Tools/UGAUGA/Build Dev Player (macOS)")]
    public static void BuildMac()
    {
        string path = Path.Combine(OutputRoot, "macOS", "pc-game.app");
        if (Build(BuildTarget.StandaloneOSX, path))
            CopyVisionServerIntoMacApp(path);
    }

    [MenuItem("Tools/UGAUGA/Build Dev Player (Windows)")]
    public static void BuildWindows()
    {
        string path = Path.Combine(OutputRoot, "Windows", "pc-game.exe");
        if (Build(BuildTarget.StandaloneWindows64, path))
            CopyVisionServerBesideWindowsExe(path);
    }

    // "dmg 실행하면 다 된다"의 실제 배포 형태 - Build Dev Player (macOS)까지 그대로 실행한
    // 뒤, 나온 .app을 하나의 dmg로 포장한다. macOS 전용(hdiutil)이라 다른 플랫폼 에디터에서는
    // 메뉴 자체가 아예 안 보인다.
#if UNITY_EDITOR_OSX
    [MenuItem("Tools/UGAUGA/Build Dev Player + DMG (macOS)")]
    public static void BuildMacDmg()
    {
        BuildMac();

        string appPath = Path.GetFullPath(Path.Combine(OutputRoot, "macOS", "pc-game.app"));
        if (!Directory.Exists(appPath))
        {
            Debug.LogError("[DevBuildTools] .app 빌드가 실패해서 dmg를 만들지 않습니다.");
            return;
        }

        string dmgPath = Path.GetFullPath(Path.Combine(OutputRoot, "macOS", "UgaUgaGame.dmg"));
        if (File.Exists(dmgPath)) File.Delete(dmgPath);

        // hdiutil create -volname <표시될 볼륨 이름> -srcfolder <.app 담긴 폴더> -ov -format
        // UDZO(압축) <출력.dmg> - 더블클릭하면 마운트되고 안에 .app 하나만 있는 가장 단순한
        // 형태(별도 Applications 폴더 심볼릭 링크 같은 드래그 설치 UX는 생략 - 필요해지면
        // create-dmg 같은 도구로 나중에 다듬으면 됨).
        string srcFolder = Path.GetDirectoryName(appPath);
        int exitCode = RunAndWait("hdiutil",
            $"create -volname \"UgaUgaGame\" -srcfolder \"{srcFolder}\" -ov -format UDZO \"{dmgPath}\"",
            out string output);

        if (exitCode == 0)
            Debug.Log($"[DevBuildTools] dmg 생성 완료: {dmgPath}\n{output}");
        else
            Debug.LogError($"[DevBuildTools] hdiutil 실패(exit={exitCode}):\n{output}");
    }
#endif

    private static bool Build(BuildTarget target, string locationPath)
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

        return report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
    }

    // macOS: VisionServerLauncher.ResolveBinaryPath()가 Application.dataPath의 부모
    // (.app/Contents/Resources) 밑 vision-server/를 찾으므로 그 자리에 그대로 복사한다.
    private static void CopyVisionServerIntoMacApp(string appPath)
    {
        string dest = Path.Combine(appPath, "Contents", "Resources", "vision-server");
        CopyVisionServerDist(dest);
#if UNITY_EDITOR_OSX
        // Unity가 빌드 직후 .app에 ad-hoc 서명을 걸어두는데(codesign -dv로 실측 확인,
        // flags=0x2(adhoc)), 서명 이후에 파일(vision-server/)을 더 끼워넣으면 서명이 담고
        // 있던 내용물 목록과 실제 내용물이 달라져서 깨진다 - spctl --assess로 실측 확인한
        // 증상은 "a sealed resource is missing or invalid"이고, 실제 배포(다운로드로 받아
        // Gatekeeper가 quarantine 검사를 하는 경우)에서는 이게 문서가 예상한 "확인되지 않은
        // 개발자 - 우회 가능" 수준이 아니라 "손상되어 열 수 없음"으로 훨씬 나쁘게 뜬다.
        // --deep으로 새로 끼워넣은 vision-server까지 포함해서 다시 서명해야 한다.
        int codesignExit = RunAndWait("/usr/bin/codesign",
            $"--force --deep --sign - \"{appPath}\"", out string codesignOutput);
        if (codesignExit == 0)
            Debug.Log("[DevBuildTools] vision-server 포함해서 .app 재서명(ad-hoc) 완료");
        else
            Debug.LogError($"[DevBuildTools] 재서명 실패(exit={codesignExit}):\n{codesignOutput}");
#endif
    }

    // Windows: ResolveBinaryPath()가 pc-game.exe와 같은 폴더 밑 vision-server/를 찾는다.
    private static void CopyVisionServerBesideWindowsExe(string exePath)
    {
        string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
        string dest = Path.Combine(exeDir, "vision-server");
        CopyVisionServerDist(dest);
    }

    private static void CopyVisionServerDist(string dest)
    {
        string src = Path.GetFullPath(VisionServerDistRoot);
        if (!Directory.Exists(src))
        {
            Debug.LogWarning(
                "[DevBuildTools] vision-server PyInstaller 산출물을 못 찾아 동봉을 건너뜁니다: "
                + $"{src}\n먼저 vision-server 폴더에서 pyinstaller vision-server.spec을 돌려야 합니다 "
                + "(README.md 참고). Unity 빌드 자체는 정상이며, 이 상태로 실행하면 카메라/포즈 "
                + "인식 없이 UI만 동작합니다.");
            return;
        }

        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        CopyDirectoryRecursive(src, dest);

        // PyInstaller onedir 산출물은 실행 권한 비트가 이미 있지만, 이 리포 안에서 다시
        // 복사하는 과정(Directory copy)에서 마스크가 유지 안 되는 경우가 있어 macOS에서는
        // 확실히 한 번 더 걸어준다. Windows는 실행 권한 개념이 달라서 해당 없음.
#if UNITY_EDITOR_OSX
        string exePath = Path.Combine(dest, "vision-server");
        if (File.Exists(exePath)) RunAndWait("/bin/chmod", $"+x \"{exePath}\"", out _);
#endif

        Debug.Log($"[DevBuildTools] vision-server 동봉 완료: {src} -> {dest}");
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (string subDir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
    }

#if UNITY_EDITOR_OSX
    private static int RunAndWait(string fileName, string arguments, out string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        output = stdout + stderr;
        return process.ExitCode;
    }
#endif
}
