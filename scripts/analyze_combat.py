"""Summarize real combat events. Never infer a win from survival or a boss disappearing."""
import argparse
import collections
import gzip
import json
import math
from pathlib import Path


def ceiling_metrics(frames):
    samples = [f for f in frames if "playerY" in f and "worldMinY" in f]
    if not samples:
        return None
    covered = excluded = 0
    near = {100: 0, 600: 0, 1200: 0}
    for a, b in zip(samples, samples[1:]):
        key = "gameTick" if "gameTick" in a and "gameTick" in b else "tick"
        elapsed = b[key] - a[key]
        if not 1 <= elapsed <= 6:
            excluded += max(0, elapsed)
            continue
        covered += elapsed
        for distance in near:
            if a["playerY"] - a["worldMinY"] < distance:
                near[distance] += elapsed
    return {"sampled_min_clearance_px": round(min(f["playerY"] - f["worldMinY"] for f in samples), 3),
            "covered_game_ticks": covered, "excluded_gap_ticks": excluded,
            "estimated_seconds_within": {str(d): round(t / 60, 3) for d, t in near.items()},
            "note": "Time-weighted preceding samples; gaps over six ticks are excluded."}


def motion_before_hit(frames, hits):
    first_hit = min((h["tick"] for h in hits), default=float("inf"))
    errors = []
    for predicted, actual in zip(frames, frames[1:]):
        if actual["tick"] >= first_hit or actual["tick"] != predicted["tick"] + 1:
            continue
        if not all(key in predicted for key in ("expectedX", "expectedY")):
            continue
        if not all(key in actual for key in ("playerX", "playerY")):
            continue
        errors.append(math.hypot(predicted["expectedX"] - actual["playerX"],
                                 predicted["expectedY"] - actual["playerY"]))
    errors.sort()
    if not errors:
        return None
    return {"pairs": len(errors), "mean_px": round(sum(errors) / len(errors), 4),
            "median_px": round(errors[len(errors) // 2], 4),
            "p95_px": round(errors[min(len(errors) - 1, int(len(errors) * .95))], 4),
            "max_px": round(errors[-1], 4)}


def summarize(path):
    opener = gzip.open if str(path).endswith(".gz") else open
    frames, damages, hits, result, header, forecast_audit = [], [], [], None, {}, {}
    preceding_hit_frames = []
    last_frame = None
    malformed = 0
    with opener(path, "rt", encoding="utf-8-sig") as stream:
        for line in stream:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                malformed += 1
                continue
            kind = row.get("kind", row.get("k"))
            if kind in ("start", "header"):
                header = row
            elif kind == "frame":
                frames.append(row)
                last_frame = row
            elif kind == "damage":
                damages.append(row)
            elif kind == "hit":
                hits.append(row)
                # NPC damage may precede SetControls within a game tick. A frame
                # written after this Hurt is a later decision, even when gameTick
                # matches. Preserve file order instead of joining to that decision.
                preceding_hit_frames.append(last_frame)
            elif kind == "result":
                result = row
            elif kind == "forecast_audit":
                forecast_audit = row
            elif kind == "t" and "P" in row:
                frames.append(row)
    native = header.get("controller") == "live-mpc"
    if native:
        ticks = (result or {}).get("ticks", max((f["tick"] for f in frames), default=0))
        actual_damage = sum(max(0, d.get("amount", 0)) for d in damages)
        assisted = bool((result or {}).get("assisted")) or any(f.get("assisted") for f in frames)
        verified = bool(result and result.get("killed") and result.get("noHitVictory")
                        and header.get("masochist") and result.get("masochist")
                        and not assisted and not hits and result.get("hits") == 0
                        and header.get("developmentSeed") is None and result.get("developmentSeed") is None
                        and actual_damage > 0 and malformed == 0
                        and result.get("rejectedRecords", 0) == 0
                        and not any(f.get("spatialAuditMismatches", 0) for f in frames))
        no_gold_verified = bool(verified and header.get("goldenStasisAllowed") is False
                                and result.get("goldShellTicks") == 0
                                and not any((f.get("navigation", {}).get("nativeEquipment", {})
                                             or {}).get("goldShellActive", False) for f in frames))
        ms = sorted(f.get("plannerMs", 0) for f in frames)
        by_tick = {f["tick"]: f for f in frames}
        by_game_tick = {f["gameTick"]: f for f in frames if "gameTick" in f}
        def hit_frame(hit):
            return by_game_tick.get(hit["gameTick"]) if "gameTick" in hit else by_tick.get(hit["tick"])
        hit_diagnostics = []
        for hit, previous in zip(hits, preceding_hit_frames):
            key = "gameTick" if "gameTick" in hit else "tick"
            lag = hit[key] - previous[key] if previous and key in previous else None
            # A one-tick-old plan predicts the impending update. A larger gap
            # cannot establish what the controller decided before this hit.
            causal = previous if lag is not None and 0 <= lag <= 1 else None
            forecast = hit.get("forecast") or {}
            actual_source = next((p for p in hit.get("nearby", [])
                                  if p.get("id") == hit.get("sourceIndex")), None)
            # TraceThreat.observedX/Y is the cached starting point of the old
            # forecast, not the source position at Hurt. Compare against the
            # actual source snapshot in Hurt.nearby; motion is not model error.
            position_error = (math.hypot(actual_source["x"] - forecast["predictedX"],
                                         actual_source["y"] - forecast["predictedY"])
                              if actual_source and all(k in forecast for k in ("predictedX", "predictedY"))
                              else None)
            hit_diagnostics.append({"tick": hit.get("tick"), "game_tick": hit.get("gameTick"),
                "source": hit.get("sourceName") or hit.get("source"), "damage": hit.get("damage"),
                "preceding_frame_lag": lag, "causal_snapshot_available": causal is not None,
                "decision_tick": causal.get("tick") if causal else None,
                "state": causal.get("attack") if causal else None,
                "predicted_first_hit": causal.get("predictedFirstHit") if causal else None,
                "immediate_collision_warning": bool(causal and causal.get("predictedFirstHit") == 1),
                "source_unseen": forecast.get("unseenThisTick", False) if "forecast" in hit else None,
                "source_position_error_px": round(position_error, 4) if position_error is not None else None})
        rescue_frames = [f for f in frames if f.get("rescueDepth", 0) > 0]
        queried = sum(f.get("dangerQueries", 0) for f in frames)
        cached = sum(f.get("cachedQueries", 0) for f in frames)
        threat_candidates = sum(f.get("threatCandidates", 0) for f in frames)
        threat_full_scan = sum(f.get("threatFullScanCandidates", 0) for f in frames)
        def timing_p95(key):
            values = sorted(f[key] for f in frames if key in f)
            return round(values[min(len(values) - 1, int(len(values) * .95))], 3) if values else None
        return {"file": str(path), "source": "native", "seconds": round(ticks / 60, 2),
                "actual_damage": actual_damage, "damage_events": len(damages),
                "hits": len(hits), "first_hit_tick": hits[0]["tick"] if hits else None,
                "first_hit": {k: v for k, v in hits[0].items() if k != "nearby"} if hits else None,
                "attack_input_fraction": round(sum(bool(f.get("attacking")) for f in frames) / max(1, len(frames)), 3),
                "mean_range": round(sum(f.get("range", 0) for f in frames) / max(1, len(frames)), 1),
                "planner_ms_p95": round(ms[min(len(ms) - 1, int(len(ms) * .95))], 3) if ms else None,
                "controller_ms_p95": timing_p95("controllerMs"),
                "forecast_ms_p95": timing_p95("forecastMs"),
                "sampled_rescue_frames": len(rescue_frames),
                "sampled_rescue_depth_max": max((f["rescueDepth"] for f in rescue_frames), default=0),
                "sampled_collision_queries": queried if any("dangerQueries" in f for f in frames) else None,
                "sampled_collision_cache_fraction": round(cached / max(1, queried + cached), 3)
                    if any("cachedQueries" in f for f in frames) else None,
                "sampled_threat_candidate_fraction": round(threat_candidates / max(1, threat_full_scan), 4)
                    if any("threatCandidates" in f for f in frames) else None,
                "spatial_audit_checks": max((f.get("spatialAuditChecks", 0) for f in frames), default=0),
                "spatial_audit_mismatches": max((f.get("spatialAuditMismatches", 0) for f in frames), default=0),
                "spatial_indexed_us_mean": frames[-1].get("spatialIndexedMeanUs") if frames else None,
                "spatial_full_scan_us_mean": frames[-1].get("spatialFullScanMeanUs") if frames else None,
                "hits_with_immediate_collision_warning": sum(h["immediate_collision_warning"] for h in hit_diagnostics),
                "hits_without_same_tick_snapshot": sum(hit_frame(h) is None for h in hits),
                "hits_without_causal_snapshot": sum(not h["causal_snapshot_available"] for h in hit_diagnostics),
                "hit_diagnostics": hit_diagnostics,
                "mean_prediction_error": round(sum(f.get("predictionError", 0) for f in frames) / max(1, len(frames)), 3),
                "motion_before_first_hit": motion_before_hit(frames, hits),
                "ceiling": ceiling_metrics(frames),
                "projectile_forecast_measurements": forecast_audit.get("measurements", []),
                "states": dict(collections.Counter(int(f.get("attack", 0)) for f in frames)),
                "weapons": dict(collections.Counter(f.get("weapon", "") for f in frames)),
                "result": result, "development_seed": header.get("developmentSeed"), "world": header.get("world"),
                "verified_no_hit_victory": verified,
                "verified_no_gold_no_hit_victory": no_gold_verified,
                "gold_shell_ticks": (result or {}).get("goldShellTicks"),
                "malformed_records": malformed}
    ticks = max((f.get("t", 0) for f in frames), default=-1) + 1
    return {"file": str(path), "source": "legacy-recorder", "seconds": round(ticks / 60, 2),
            "masochist": bool(header.get("masochist")), "initial_weapon": header.get("held"),
            "attack_input_fraction": round(sum(bool(f.get("I", [0])[0] & 64) for f in frames) / max(1, len(frames)), 3),
            "states": dict(collections.Counter(int(n[8]) for f in frames for n in f.get("N", []))),
            "verified_no_hit_victory": False,
            "note": "Legacy recorder lacks authoritative dealt-damage and kill events; no victory certification.",
            "malformed_records": malformed}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="+", type=Path)
    args = parser.parse_args()
    for path in args.paths:
        print(json.dumps(summarize(path), ensure_ascii=False))


if __name__ == "__main__":
    main()
