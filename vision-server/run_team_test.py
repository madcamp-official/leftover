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
    return parser.parse_args()


def ask_if_missing(value: str | None, prompt: str, allowed: set[str] | None = None) -> str:
    while not value:
        value = input(prompt).strip().lower()
        if allowed is not None and value not in allowed:
            print(f"다음 중 하나를 입력하세요: {', '.join(sorted(allowed))}")
            value = None
    return value


def check_environment() -> None:
    missing = [
        package
        for package in ("cv2", "mediapipe")
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

    check_environment()

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

    print()
    print(f"[시작] {player_id} 카메라 -> {pc_ip}:{args.port}")
    print("카메라 창에서 관절 점이 보이면 연결 준비 완료입니다. 종료는 q.")
    print()
    return subprocess.run(command, cwd=ROOT, check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
