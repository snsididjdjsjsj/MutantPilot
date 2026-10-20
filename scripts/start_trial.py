"""Prepare through normal flight, verify a fresh start, then summon once."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import shutil
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--settings", type=Path)
    parser.add_argument("--life", type=int, default=900)
    parser.add_argument("--record",action="store_true",help="Capture one native game render per logic tick to a 60 FPS video")
    parser.add_argument("--ffmpeg",type=Path,default=shutil.which("ffmpeg"))
    parser.add_argument("--record-lead",type=float,default=4.0,help="Seconds to record before summoning, so the video opens on the arena instead of the first attack")

    parser.add_argument("--record-directory",type=Path,default=Path(__file__).resolve().parents[1]/"recordings")
    parser.add_argument("--stop-on-hit", action="store_true",
                        help="Watch the trial and normally save/quit after its first recorded Hurt.")
    args = parser.parse_args()
    directory = Path.home() / "Documents/My Games/Terraria/tModLoader/MutantPilot"

    def read_status():
        # Atomic replacement can briefly deny a Windows reader access. Retry
        # the read only; never resend a command after an uncertain outcome.
        read_deadline = time.monotonic() + 1
        while True:
            try:
                result = json.loads((directory / "status.json").read_text(encoding="utf8"))
                break
            except (OSError, json.JSONDecodeError):
                if time.monotonic() >= read_deadline:
                    raise SystemExit("Live status could not be read; no further command was sent.")
                time.sleep(.025)
        age = (datetime.now(timezone.utc) - datetime.fromisoformat(result["utc"].replace("Z", "+00:00"))).total_seconds()
        if age > 3 or age < -1:
            raise SystemExit("Live status is stale; no further command was sent.")
        if result["version"] != args.version or not result["masochist"] or result["assistance"]:
            raise SystemExit("Version, mode or assistance does not match the trial requirements.")
        return result

    def status():
        result = read_status()
        if result["boss"] is not None or result["player"]["dead"]:
            raise SystemExit("A boss is already alive or the player is dead; no new trial started.")
        return result

    def command(operation, *extra):
        # Both ends use UTF-8, independently of the Windows console code page.
        result = subprocess.run([sys.executable, "-X", "utf8", str(Path(__file__).with_name("live.py")),
                                 operation, *extra], capture_output=True, text=True, encoding="utf8")
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr)
        return json.loads(result.stdout)

    status()
    if args.settings:
        command("tune", "--settings", str(args.settings.resolve()))
    command("prepare")
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        current = status()
        player = current["player"]
        if (player["prepareTicks"] > 0 and player["life"] >= args.life
                and player.get("prepareReached", player["y"] <= player["prepareY"] + 50)
                and abs(player["vy"]) < .01):
            if args.record:
                if not args.ffmpeg:
                    raise SystemExit("Recording requires an installed ffmpeg; summon was not sent.")
                recording=command("record","--mode","arm","--ffmpeg",str(Path(args.ffmpeg).resolve()),
                                  "--output-directory",str(args.record_directory.resolve()))
                if not recording.get("recording",{}).get("armed"):
                    raise SystemExit("Recording did not arm; summon was not sent.")
                # A few seconds of lead-in before the boss appears. The video otherwise opens on the
                # first attack, which reads badly; this shows the arena and the summon instead.
                if args.record_lead > 0:
                    time.sleep(args.record_lead)
            started = command("summon")
            print(json.dumps({"utc": started["utc"], "version": started["version"],
                              "life": started["player"]["life"], "y": started["player"]["y"],
                              "settings": started["settings"], "boss": started["boss"],
                              "recording":started.get("recording")}, ensure_ascii=False), flush=True)
            if args.stop_on_hit:
                next_progress = time.monotonic() + 30
                while True:
                    current = read_status()
                    observed = current["player"]
                    if observed["dead"] or current["boss"] is None:
                        print(json.dumps({"closed": observed["lastResult"]}), flush=True)
                        return
                    if observed["hits"] > 0:
                        # The encounter has already failed the zero-Hurt objective.
                        # Preserve its record and world with the normal save flow.
                        command("savequit")
                        print(json.dumps({"stopped_after_first_hit": True, "tick": observed["tick"],
                                          "hits": observed["hits"]}), flush=True)
                        return
                    if time.monotonic() >= next_progress:
                        print(json.dumps({"tick": observed["tick"], "hits": observed["hits"],
                                          "damage": observed["actualDamage"]}), flush=True)
                        next_progress = time.monotonic() + 30
                    time.sleep(.25)
            return
        time.sleep(.25)
    raise SystemExit("Preparation did not reach a valid start; summon was not sent.")


if __name__ == "__main__":
    main()
