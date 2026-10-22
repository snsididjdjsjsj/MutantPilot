"""Control the local validation harness without keyboard focus or a Python ML runtime."""
import argparse
import json
import os
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("status", "prepare", "summon", "stop", "pilot", "savequit", "clean", "tune", "checkbeams", "seed", "anchor", "background", "inspect", "record", "equiptrial"))
    parser.add_argument("--weapon", choices=("FargowiltasSouls/DragonBreath2", "FargowiltasSouls/NukeFishron", "FargowiltasSouls/TheDestroyer"))
    parser.add_argument("--mode", choices=("arm", "preview", "stop"), help="Native logic-frame recording")
    parser.add_argument("--ffmpeg", type=Path, help="Absolute path to installed ffmpeg")
    parser.add_argument("--output-directory", type=Path, help="Directory for local video outputs")
    parser.add_argument("--frames",type=int,help="Preview length in game frames; default 180")
    parser.add_argument("--enabled", type=int, choices=(0, 1), help="Enable/disable native-speed background updates between fights")
    parser.add_argument("--settings", type=Path, help="JSON routing parameters for tune")
    parser.add_argument("--seed", type=int, help="RNG seed for repeatable fights (development only)")
    parser.add_argument("--x", type=float, help="world x for anchor (development only)")
    parser.add_argument("--y", type=float, help="world y for anchor (development only)")
    parser.add_argument("--directory", type=Path, default=Path.home() / "Documents" / "My Games" /
                        "Terraria" / "tModLoader" / "MutantPilot")
    parser.add_argument("--timeout", type=float, default=10)
    args = parser.parse_args()
    status_path = args.directory / "status.json"
    if args.operation == "status":
        if not status_path.exists():
            raise SystemExit("No live status. Build MutantPilot, enable it, and enter a single-player world first.")
        print(status_path.read_text(encoding="utf-8"))
        return
    try:
        before = json.loads(status_path.read_text(encoding="utf-8"))
        age = (datetime.now(timezone.utc) - datetime.fromisoformat(before["utc"].replace("Z", "+00:00"))).total_seconds()
    except (OSError, json.JSONDecodeError, KeyError, ValueError):
        raise SystemExit("No fresh world status; enter the world before sending a command.")
    if age > 3 or age < -1:
        raise SystemExit("World status is stale; no command was sent.")
    args.directory.mkdir(parents=True, exist_ok=True)
    command = {"id": uuid.uuid4().hex, "op": args.operation}
    if args.operation == "equiptrial":
        if not args.weapon:
            parser.error("equiptrial requires --weapon")
        command["weapon"] = args.weapon
    if args.operation == "record":
        if args.mode is None:
            parser.error("record requires --mode")
        command["mode"] = args.mode
        if args.mode == "preview" and args.frames is not None:
            if not 1 <= args.frames <= 36000:
                parser.error("preview frames must be between 1 and 36000")
            command["frames"]=args.frames
        if args.mode != "stop":
            if args.ffmpeg is None or args.output_directory is None:
                parser.error("record arm/preview requires --ffmpeg and --output-directory")
            command.update(ffmpeg=str(args.ffmpeg.resolve()), directory=str(args.output_directory.resolve()))
    if args.operation == "background":
        if args.enabled is None:
            parser.error("background requires --enabled 0 or 1")
        command["enabled"] = bool(args.enabled)
    if args.operation == "tune":
        if args.settings is None:
            parser.error("tune requires --settings with a JSON file")
        command["settings"] = json.loads(args.settings.read_text(encoding="utf-8-sig"))
    if args.operation == "seed":
        if args.seed is None:
            parser.error("seed requires --seed with an integer")
        command["seed"] = args.seed
    if args.operation == "anchor":
        if args.x is None:
            parser.error("anchor requires --x with a world x")
        command["x"] = args.x
        if args.y is not None:
            command["y"] = args.y
    temporary = args.directory / "command.json.tmp"
    temporary.write_text(json.dumps(command), encoding="utf-8")
    os.replace(temporary, args.directory / "command.json")
    deadline = time.monotonic() + args.timeout
    while time.monotonic() < deadline:
        try:
            status = json.loads(status_path.read_text(encoding="utf-8"))
            if status.get("commandId") == command["id"]:
                print(json.dumps(status, ensure_ascii=False))
                if status.get("commandError"):
                    raise SystemExit(1)
                return
        except (OSError, json.JSONDecodeError):
            pass
        time.sleep(.1)
    # Cancel the unacknowledged operation so it cannot unexpectedly run after a later reload.
    cancel = {"id": command["id"], "op": "status"}
    temporary.write_text(json.dumps(cancel), encoding="utf-8")
    os.replace(temporary, args.directory / "command.json")
    raise SystemExit("No acknowledgement: game paused, closed, or old mod loaded. Pending operation cancelled.")


if __name__ == "__main__":
    main()
