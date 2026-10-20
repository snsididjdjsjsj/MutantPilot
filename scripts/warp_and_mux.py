"""Map a real-time loopback recording onto the game-time video timeline, then mux.

The game runs slower than real time whenever the search stalls, so the recorded audio drifts
against the video by the total stall time (tens of seconds over a fight). ``<video>.frames.jsonl``
now carries a wall-clock stamp per captured frame, which gives a monotone game-time -> real-time
map; resampling the audio through that map restores frame-accurate sound effects.

Usage:
  python scripts/warp_and_mux.py <video.mp4> <loopback.wav> [--out output.mp4] [--ffmpeg ffmpeg.exe]
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np


def load_frames(video: Path) -> tuple[np.ndarray, np.ndarray]:
    path = Path(str(video) + ".frames.jsonl")
    if not path.exists():
        raise SystemExit(f"missing frame index: {path}")
    game, wall = [], []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        if "wall" not in row:
            raise SystemExit("frame index has no wall stamps; re-record with a current build")
        game.append(float(row["frame"]) / 60.0)
        wall.append(float(row["wall"]) / 1000.0)
    return np.asarray(game), np.asarray(wall)


def read_wav(path: Path, ffmpeg: str) -> tuple[np.ndarray, int]:
    """Read any WAV ffmpeg understands. The loopback recorder writes IEEE-float WAV, which the
    standard library's wave module rejects, so normalise to 16-bit PCM first."""
    pcm = Path(str(path) + ".pcm.wav")
    convert = subprocess.run(
        [ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
         "-i", str(path), "-c:a", "pcm_s16le", str(pcm)],
        capture_output=True, text=True)
    if convert.returncode != 0:
        print(convert.stderr, file=sys.stderr)
        raise SystemExit(f"could not decode {path}")
    with wave.open(str(pcm), "rb") as handle:
        channels = handle.getnchannels()
        width = handle.getsampwidth()
        rate = handle.getframerate()
        raw = handle.readframes(handle.getnframes())
    pcm.unlink(missing_ok=True)
    if width != 2:
        raise SystemExit(f"unexpected sample width after conversion: {width}")
    data = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    return data.reshape(-1, channels), rate


def write_wav(path: Path, data: np.ndarray, rate: int) -> None:
    clipped = np.clip(data, -1.0, 1.0)
    pcm = (clipped * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(pcm.shape[1])
        handle.setsampwidth(2)
        handle.setframerate(rate)
        handle.writeframes(pcm.tobytes())


def warp(audio: np.ndarray, rate: int, game: np.ndarray, wall: np.ndarray) -> np.ndarray:
    """Play each game tick's sound at its recorded wall time, without changing pitch.

    Read-position resampling would also shift pitch by the slowdown factor, which is audible. This
    overlap-adds fixed-length source grains at a warped read position instead: the grain itself is
    never resampled, so pitch is preserved and only the timeline is remapped.
    """
    real = wall - wall[0]
    total = float(game[-1])
    win = max(64, int(rate * 0.02))
    hop = win // 2
    out_len = int(total * rate) + win
    frames = audio.shape[0]
    channels = audio.shape[1]
    out = np.zeros((out_len, channels), np.float32)
    weight = np.zeros((out_len, 1), np.float32)
    window = np.hanning(win).astype(np.float32)[:, None]
    starts = np.arange(0, out_len - win, hop)
    if starts.size == 0:
        return audio[: max(1, int(total * rate))]
    read = np.interp(starts / rate, game, real) * rate
    read = np.clip(read, win // 2, frames - win // 2 - 1)
    for index, position in enumerate(read):
        begin = int(position) - win // 2
        out[index:index + win] += audio[begin:begin + win] * window
        weight[index:index + win] += window
    return (out / np.maximum(weight, 1e-6))[: int(total * rate)]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("video", type=Path)
    parser.add_argument("wav", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg") or "ffmpeg")
    parser.add_argument("--keep-wav", action="store_true")
    parser.add_argument("--audio-offset", type=float, default=2.0)
    args = parser.parse_args()

    game, wall = load_frames(args.video)
    real_span = float(wall[-1] - wall[0])
    game_span = float(game[-1])
    tempo = max(0.5, min(2.0, real_span / max(game_span, 1e-9)))
    warped = Path(str(args.video) + ".gamesync.wav")
    trim = max(0.0, args.audio_offset)
    stretch = subprocess.run(
        [args.ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
         "-ss", "%.3f" % trim, "-i", str(args.wav),
         "-filter:a", "atempo=%.6f" % tempo, "-t", "%.3f" % game_span,
         "-c:a", "pcm_s16le", "-ac", "2", "-ar", "48000", str(warped)],
        capture_output=True, text=True)
    if stretch.returncode != 0:
        print(stretch.stderr, file=sys.stderr)
        raise SystemExit("atempo stretch failed")
    audio = np.zeros((1, 2), np.float32)
    rate = 48000

    out = args.out or args.video.with_name(args.video.stem + "_av.mp4")
    result = subprocess.run(
        [args.ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
         "-i", str(args.video), "-i", str(warped),
         "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
         "-shortest", str(out)],
        capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stderr, file=sys.stderr)
        raise SystemExit("ffmpeg mux failed")
    if not args.keep_wav:
        warped.unlink(missing_ok=True)

    real_seconds = float(wall[-1] - wall[0])
    print(json.dumps({
        "video": str(args.video),
        "audio": str(args.wav),
        "output": str(out),
        "video_seconds": round(float(game[-1]), 3),
        "real_seconds": round(real_seconds, 3),
        "slowdown": round(real_seconds / max(game[-1], 1e-9), 4),
        "atempo": round(tempo, 6),
        "sample_rate": rate,
        "channels": int(audio.shape[1]),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
