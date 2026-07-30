"""ScreamDuel용 독립 마이크 게이지.

MediaPipe, 카메라, Unity 없이 sounddevice 입력만 0~100으로 표시한다.

실행:
    .venv/Scripts/python.exe test_scream_mic.py
    .venv/Scripts/python.exe test_scream_mic.py --list-devices
    .venv/Scripts/python.exe test_scream_mic.py --device 1

조작:
    Space  최고값 초기화
    Q/Esc  종료
"""

from __future__ import annotations

import argparse
import math
import queue
import tkinter as tk

import numpy as np
import sounddevice as sd


class MicrophoneMeter:
    def __init__(
        self,
        root: tk.Tk,
        device: int | None,
        channels: int | None,
        min_db: float,
        max_db: float,
    ) -> None:
        if max_db <= min_db:
            raise ValueError("--max-db는 --min-db보다 커야 합니다.")

        self.root = root
        self.device = sd.default.device[0] if device is None else device
        info = sd.query_devices(self.device, "input")
        self.device_name = info["name"]
        self.samplerate = float(info["default_samplerate"])
        max_channels = int(info["max_input_channels"])
        self.channels = channels or max_channels
        if self.channels < 1 or self.channels > max_channels:
            raise ValueError(f"--channels는 1~{max_channels} 사이여야 합니다.")

        self.min_db = min_db
        self.max_db = max_db
        self.current_db = min_db
        self.current_value = 0.0
        self.display_value = 0.0
        self.peak_value = 0.0
        self.events: queue.SimpleQueue[str] = queue.SimpleQueue()

        self.root.title("ScreamDuel Microphone Test")
        self.root.geometry("760x360")
        self.root.configure(bg="#17120e")
        self.root.resizable(False, False)
        self.root.bind("<space>", self.reset_peak)
        self.root.bind("<Escape>", self.close)
        self.root.bind("q", self.close)
        self.root.protocol("WM_DELETE_WINDOW", self.close)

        self.title = tk.Label(
            root,
            text="SCREAM DUEL  ·  MICROPHONE TEST",
            font=("Arial", 20, "bold"),
            fg="#ffd36b",
            bg="#17120e",
        )
        self.title.pack(pady=(24, 8))

        self.device_label = tk.Label(
            root,
            text=(
                f"#{self.device}  {self.device_name}  ·  "
                f"{self.samplerate:.0f} Hz  ·  {self.channels} ch"
            ),
            font=("Arial", 10),
            fg="#d7c6a8",
            bg="#17120e",
        )
        self.device_label.pack()

        self.canvas = tk.Canvas(
            root,
            width=660,
            height=82,
            bg="#2b2118",
            highlightthickness=3,
            highlightbackground="#75552f",
        )
        self.canvas.pack(pady=(26, 10))
        self.bar = self.canvas.create_rectangle(0, 0, 0, 82, fill="#3196ff", width=0)
        for percent in (25, 50, 75):
            x = 660 * percent / 100
            self.canvas.create_line(x, 0, x, 82, fill="#806c50", dash=(4, 4))

        self.value_label = tk.Label(
            root,
            text="0",
            font=("Arial", 38, "bold"),
            fg="white",
            bg="#17120e",
        )
        self.value_label.pack()

        self.detail_label = tk.Label(
            root,
            text="현재 -35.0 dB  ·  최고 0  ·  범위 -35~0 dB",
            font=("Arial", 12),
            fg="#d7c6a8",
            bg="#17120e",
        )
        self.detail_label.pack(pady=(2, 8))

        self.help_label = tk.Label(
            root,
            text="Space: 최고값 초기화     Q / Esc: 종료",
            font=("Arial", 10),
            fg="#8f806c",
            bg="#17120e",
        )
        self.help_label.pack()

        self.stream = sd.InputStream(
            device=self.device,
            samplerate=self.samplerate,
            channels=self.channels,
            dtype="float32",
            blocksize=0,
            callback=self.on_audio,
        )
        self.stream.start()
        self.root.after(16, self.refresh)

    def on_audio(self, data, frames, time_info, status) -> None:
        if status:
            self.events.put(str(status))
        rms = float(np.sqrt(np.mean(np.square(data))))
        db = 20.0 * math.log10(rms) if rms > 1e-9 else self.min_db
        value = (db - self.min_db) / (self.max_db - self.min_db)
        self.current_db = db
        self.current_value = max(0.0, min(1.0, value))

    def refresh(self) -> None:
        # 빠르게 올라가고 천천히 내려와 순간 음량도 눈으로 확인할 수 있게 한다.
        target = self.current_value
        if target >= self.display_value:
            self.display_value += (target - self.display_value) * 0.55
        else:
            self.display_value += (target - self.display_value) * 0.12

        self.peak_value = max(self.peak_value, self.current_value)
        percent = round(self.display_value * 100)
        peak = round(self.peak_value * 100)
        width = 660 * self.display_value

        if percent < 45:
            color = "#3196ff"
        elif percent < 75:
            color = "#ffba38"
        else:
            color = "#ff4b36"

        self.canvas.coords(self.bar, 0, 0, width, 82)
        self.canvas.itemconfigure(self.bar, fill=color)
        self.value_label.configure(text=str(percent), fg=color)
        self.detail_label.configure(
            text=(
                f"현재 {self.current_db:.1f} dB  ·  최고 {peak}  ·  "
                f"범위 {self.min_db:g}~{self.max_db:g} dB"
            )
        )

        if not self.events.empty():
            self.help_label.configure(text=f"오디오 경고: {self.events.get()}", fg="#ff6b5b")

        self.root.after(16, self.refresh)

    def reset_peak(self, event=None) -> None:
        self.peak_value = 0.0

    def close(self, event=None) -> None:
        try:
            self.stream.stop()
            self.stream.close()
        finally:
            self.root.destroy()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--device", type=int, default=None)
    parser.add_argument("--channels", type=int, default=None)
    parser.add_argument("--min-db", type=float, default=-35.0)
    parser.add_argument("--max-db", type=float, default=0.0)
    parser.add_argument("--list-devices", action="store_true")
    args = parser.parse_args()

    if args.list_devices:
        print(sd.query_devices())
        return

    root = tk.Tk()
    try:
        MicrophoneMeter(
            root,
            device=args.device,
            channels=args.channels,
            min_db=args.min_db,
            max_db=args.max_db,
        )
    except Exception as error:
        root.destroy()
        raise SystemExit(f"마이크를 열 수 없습니다: {error}") from error
    root.mainloop()


if __name__ == "__main__":
    main()
