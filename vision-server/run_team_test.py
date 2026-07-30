"""팀 테스트용 MediaPipe 실행 런처.

각 플레이어 PC에서 이 파일을 실행하고 Unity가 실행 중인 PC의 LAN IP와
자신의 플레이어 번호(p1 또는 p2)를 입력하면 vision-server/main.py를 실행한다.
"""

from __future__ import annotations

import argparse
import importlib.util
import pathlib
import subprocess
import sys


ROOT = pathlib.Path(__file__).resolve().parent
MAIN_PATH = ROOT / "main.py"
POSE_MODEL_PATH = ROOT / "models" / "pose_landmarker_lite.task"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="우가우가게임 팀 테스트용 MediaPipe 런처")
    parser.add_argument("--pc-ip", help="Unity가 실행 중인 PC의 LAN IP")
    parser.add_argument("--player-id", choices=("p1", "p2"), help="이 카메라가 담당할 플레이어")
    parser.add_argument("--camera-index", type=int, default=0, help="사용할 카메라 번호 (기본: 0)")
    parser.add_argument("--port", type=int, default=9100, help="Unity UDP 수신 포트 (기본: 9100)")
    parser.add_argument(
        "--no-voice",
        action="store_true",
        help="마이크 캡처를 끈다. 전체 매치에서는 ScreamDuel 때문에 기본적으로 켜진다.",
    )
    parser.add_argument(
        "--voice-device",
        type=int,
        help="사용할 sounddevice 입력 장치 번호. 생략하면 시스템 기본 마이크.",
    )
    parser.add_argument(
        "--voice-channels",
        type=int,
        help="읽을 마이크 배열 채널 수. 생략하면 장치의 모든 입력 채널.",
    )
    return parser.parse_args()


def ask_if_missing(value: str | None, prompt: str, allowed: set[str] | None = None) -> str:
    while not value:
        value = input(prompt).strip().lower()
        if allowed is not None and value not in allowed:
            print(f"다음 중 하나를 입력하세요: {', '.join(sorted(allowed))}")
            value = None
    return value


def check_environment(include_voice: bool = True) -> None:
    packages = ["cv2", "mediapipe"]
    if include_voice:
        packages.extend(["sounddevice", "numpy"])
    missing = [
        package
        for package in packages
        if importlib.util.find_spec(package) is None
    ]
    if missing:
        print("[오류] 필요한 Python 패키지가 설치되지 않았습니다.")
        print(f"  {sys.executable} -m pip install -r \"{ROOT / 'requirements.txt'}\"")
        raise SystemExit(1)

    if not POSE_MODEL_PATH.exists():
        print(f"[오류] 포즈 모델 파일이 없습니다: {POSE_MODEL_PATH}")
        raise SystemExit(1)


def main() -> int:
    args = parse_args()
    pc_ip = ask_if_missing(args.pc_ip, "Unity PC의 LAN IP: ")
    player_id = ask_if_missing(
        args.player_id,
        "이 PC의 플레이어 번호 (p1/p2): ",
        {"p1", "p2"},
    )

    check_environment(include_voice=not args.no_voice)

    command = [
        sys.executable,
        str(MAIN_PATH),
        "--pc-ip",
        pc_ip,
        "--port",
        str(args.port),
        "--camera-index",
        str(args.camera_index),
        "--player-id",
        player_id,
    ]
    if not args.no_voice:
        command.append("--voice")
        if args.voice_device is not None:
            command.extend(["--voice-device", str(args.voice_device)])
        if args.voice_channels is not None:
            command.extend(["--voice-channels", str(args.voice_channels)])

    print()
    print(f"[시작] {player_id} 카메라 -> {pc_ip}:{args.port}")
    print("마이크 음량 전송: " + ("꺼짐" if args.no_voice else "켜짐 (--voice)"))
    print("카메라 창에서 관절 점이 보이면 연결 준비 완료입니다. 종료는 q.")
    print()
    return subprocess.run(command, cwd=ROOT, check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
