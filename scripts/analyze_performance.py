"""Summarize observed frame costs; combat frame samples are not a full FPS trace."""
import argparse
import gzip
import json
import statistics
from collections import defaultdict
from pathlib import Path


def distribution(values):
    if not values:
        return None
    ordered = sorted(values)
    return {"count": len(values), "mean": round(statistics.mean(values), 3),
            **{f"p{p}": round(ordered[min(len(ordered)-1, int((len(ordered)-1)*p/100))], 3)
               for p in (50, 95, 99)}, "max": round(ordered[-1], 3),
            "over16_67": sum(v > 1000/60 for v in values),
            "over33_33": sum(v > 1000/30 for v in values)}


def summarize(path):
    frames = []
    stages = defaultdict(list)
    header = result = None
    performance = []
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", encoding="utf-8-sig") as source:
        for line in source:
            row = json.loads(line)
            if row.get("kind") == "start":
                header = {k: row.get(k) for k in ("controllerVersion", "settings", "developmentSeed")}
            elif row.get("kind") == "result":
                result = row
            elif row.get("kind") == "performance":
                performance.append(row)
            elif row.get("kind") == "frame":
                frames.append(row)
                stages[int(row["attack"])].append(row)
    fields = ("controllerMs", "plannerMs", "forecastMs")
    return {"path": str(path), "start": header, "result": result,
            "sampled_frames": len(frames),
            "cost_ms": {f: distribution([r[f] for r in frames]) for f in fields},
            "states": {str(s): {"frames": len(rows),
                                **{f: distribution([r[f] for r in rows]) for f in fields}}
                       for s, rows in sorted(stages.items())},
            "performance": performance}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", type=Path, nargs="+")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    summaries = [summarize(p) for p in args.paths]
    rendered = json.dumps(summaries, ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(rendered, encoding="utf8")
    for report in summaries:
        print(json.dumps({k: report[k] for k in ("path", "result", "cost_ms")}, ensure_ascii=False))
        worst = sorted(report["states"].items(), key=lambda p:p[1]["controllerMs"]["p95"], reverse=True)[:8]
        print("slowest states:", json.dumps(worst, ensure_ascii=False))
