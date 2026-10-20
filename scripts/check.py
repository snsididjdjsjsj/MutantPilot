"""Run the checks that do not need Terraria, a save, or a combat log."""

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

RUN = (
    "tests/CombatPlannerChecks/CombatPlannerChecks.csproj",
    "tests/ForecastIndexChecks/ForecastIndexChecks.csproj",
    "tests/WormResponseChecks/WormResponseChecks.csproj",
    "tests/LogicVideoChecks/LogicVideoChecks.csproj",
    "tests/ShortRayChecks/ShortRayChecks.csproj",
)

# Prints a large JSON sample. Build it, run it, and keep the log short.
QUIET = ("tests/SwordRoutingStudy/SwordRoutingStudy.csproj",)

# These compile against the mod sources. Running them needs a local fight log or world.
BUILD_ONLY = (
    "tests/EmptyWorldChecks/EmptyWorldChecks.csproj",
    "tests/FinalPhaseReplay/FinalPhaseReplay.csproj",
    "tests/LockedTossChecks/LockedTossChecks.csproj",
    "tests/MarkSpawnChecks/MarkSpawnChecks.csproj",
    "tests/MechFanChecks/MechFanChecks.csproj",
    "tests/NoGoldSphereChecks/NoGoldSphereChecks.csproj",
    "tests/P1TransitionReplay/P1TransitionReplay.csproj",
    "tests/SphereSpawnChecks/SphereSpawnChecks.csproj",
    "tests/TossResponseChecks/TossResponseChecks.csproj",
    "tests/VoidExitReplay/VoidExitReplay.csproj",
    "tests/VoidPhaseReplay/VoidPhaseReplay.csproj",
)


def run(command, quiet=False):
    print("+", " ".join(command), flush=True)
    completed = subprocess.run(
        command,
        cwd=str(ROOT),
        stdout=subprocess.PIPE if quiet else None,
        stderr=subprocess.STDOUT if quiet else None,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if completed.returncode:
        if quiet and completed.stdout:
            sys.stdout.write(completed.stdout)
        raise SystemExit(completed.returncode)
    if quiet and completed.stdout:
        print("passed")


def build(project):
    run(["dotnet", "build", project, "-c", "Release", "-v", "q", "--nologo", "-clp:ErrorsOnly;NoSummary"])


def main():
    for project in BUILD_ONLY:
        build(project)
    for project in RUN + QUIET:
        build(project)
        run(["dotnet", "run", "--project", project, "-c", "Release", "--no-build"], quiet=project in QUIET)
    run([sys.executable, "-m", "unittest", "discover", "-s", "tests", "-p", "test_combat_report.py"])
    print("all standalone checks passed")


if __name__ == "__main__":
    main()
