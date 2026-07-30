"""ScreamDuel 마이크 입력만 빠르게 확인하는 진단 도구."""

import argparse
import time

from main import VoiceLevelMeter


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--device", type=int, default=None)
    parser.add_argument("--channels", type=int, default=None)
    parser.add_argument("--seconds", type=float, default=8.0)
    args = parser.parse_args()

    meter = VoiceLevelMeter(device=args.device, channels=args.channels)
    print(
        f"device=#{meter.device} {meter.device_name} "
        f"rate={meter.samplerate:.0f}Hz channels={meter.channels}",
        flush=True,
    )

    values = []
    started = time.time()
    last_second = -1
    meter.start()
    try:
        while time.time() - started < args.seconds:
            time.sleep(0.05)
            values.append(meter.level)
            second = int(time.time() - started)
            if second != last_second:
                print(f"second={second} level={meter.level:.3f}", flush=True)
                last_second = second
    finally:
        meter.stop()

    maximum = max(values, default=0.0)
    average = sum(values) / len(values) if values else 0.0
    print(f"max={maximum:.3f} avg={average:.3f}", flush=True)


if __name__ == "__main__":
    main()
