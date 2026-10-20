import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("combat_report", Path(__file__).parents[1] / "scripts/analyze_combat.py")
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class ReportChecks(unittest.TestCase):
    def evaluate(self, rows):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "run.jsonl"
            path.write_text("\n".join(json.dumps(r) for r in rows), encoding="utf8")
            return report.summarize(path)

    def records(self):
        return [dict(kind="start", controller="live-mpc", masochist=True),
                dict(kind="damage", tick=20, amount=1000),
                dict(kind="result", ticks=60, killed=True, noHitVictory=True,
                     hits=0, assisted=False, masochist=True)]

    def test_authoritative_kill(self):
        self.assertTrue(self.evaluate(self.records())["verified_no_hit_victory"])

    def test_no_gold_certificate_requires_explicit_complete_observation(self):
        rows = self.records()
        self.assertFalse(self.evaluate(rows)["verified_no_gold_no_hit_victory"])
        rows[0]["goldenStasisAllowed"] = False
        rows[-1]["goldShellTicks"] = 0
        self.assertTrue(self.evaluate(rows)["verified_no_gold_no_hit_victory"])
        rows[-1]["goldShellTicks"] = 1
        self.assertFalse(self.evaluate(rows)["verified_no_gold_no_hit_victory"])

    def test_gold_observed_in_frame_rejects_inconsistent_result(self):
        rows = self.records()
        rows[0]["goldenStasisAllowed"] = False
        rows[-1]["goldShellTicks"] = 0
        rows.insert(1, dict(kind="frame", tick=1,
                           navigation=dict(nativeEquipment=dict(goldShellActive=True))))
        self.assertFalse(self.evaluate(rows)["verified_no_gold_no_hit_victory"])

    def test_rejected_record_disqualifies_claimed_victory(self):
        rows = self.records()
        rows[-1]["rejectedRecords"] = 1
        self.assertFalse(self.evaluate(rows)["verified_no_hit_victory"])

    def test_seeded_development_run_cannot_certify_natural_victory(self):
        rows = self.records()
        rows[0]["developmentSeed"] = 12345
        self.assertFalse(self.evaluate(rows)["verified_no_hit_victory"])

    def test_ceiling_exposure_weights_sampling_time_and_excludes_large_gaps(self):
        value = report.ceiling_metrics([dict(tick=0, playerY=700, worldMinY=677),
            dict(tick=6, playerY=2000, worldMinY=677), dict(tick=12, playerY=700, worldMinY=677),
            dict(tick=30, playerY=700, worldMinY=677)])
        self.assertEqual(value["sampled_min_clearance_px"], 23)
        self.assertEqual(value["covered_game_ticks"], 12)
        self.assertEqual(value["excluded_gap_ticks"], 18)
        self.assertEqual(value["estimated_seconds_within"]["600"], .1)

    def test_no_kill_event(self):
        self.assertFalse(self.evaluate(self.records()[:-1])["verified_no_hit_victory"])

    def test_collision_index_disagreement_disqualifies_claimed_victory(self):
        rows = self.records()
        rows.insert(1, dict(kind="frame", tick=1, spatialAuditChecks=20, spatialAuditMismatches=1))
        self.assertFalse(self.evaluate(rows)["verified_no_hit_victory"])

    def test_hit_cannot_be_hidden_by_summary(self):
        rows = self.records()
        rows.insert(1, dict(kind="hit", tick=10, damage=100))
        self.assertFalse(self.evaluate(rows)["verified_no_hit_victory"])

    def test_wrong_difficulty(self):
        rows = self.records()
        rows[0]["masochist"] = False
        self.assertFalse(self.evaluate(rows)["verified_no_hit_victory"])

    def test_no_real_damage(self):
        self.assertFalse(self.evaluate(self.records()[::2])["verified_no_hit_victory"])

    def test_motion_uses_next_tick_and_excludes_hit_and_sampling_gaps(self):
        frames = [dict(tick=1, expectedX=3, expectedY=4),
                  dict(tick=2, playerX=0, playerY=0, expectedX=100, expectedY=100),
                  dict(tick=4, playerX=0, playerY=0, expectedX=100, expectedY=100),
                  dict(tick=5, playerX=0, playerY=0)]
        self.assertEqual(report.motion_before_hit(frames, [dict(tick=5)]),
                         dict(pairs=1, mean_px=5, median_px=5, p95_px=5, max_px=5))

    def test_collision_warning_does_not_cross_a_sampling_gap(self):
        rows = self.records()
        rows[1:1] = [dict(kind="frame", tick=7, predictedFirstHit=1, rescueDepth=6),
                     dict(kind="frame", tick=8, predictedFirstHit=1, rescueDepth=24),
                     dict(kind="hit", tick=8), dict(kind="hit", tick=10)]
        result = self.evaluate(rows)
        self.assertEqual(result["hits_with_immediate_collision_warning"], 1)
        self.assertEqual(result["hits_without_same_tick_snapshot"], 1)
        self.assertEqual(result["sampled_rescue_depth_max"], 24)

    def test_collision_warning_aligns_actual_game_tick_when_npc_hit_precedes_controls(self):
        rows = self.records()
        rows[1:1] = [dict(kind="frame", tick=7, gameTick=100, predictedFirstHit=1),
                     dict(kind="frame", tick=8, gameTick=101, predictedFirstHit=0),
                     dict(kind="hit", tick=7, gameTick=101), dict(kind="hit", tick=7, gameTick=102)]
        result = self.evaluate(rows)
        self.assertEqual(result["hits_with_immediate_collision_warning"], 0)
        self.assertEqual(result["hits_without_same_tick_snapshot"], 1)

    def test_post_hit_decision_cannot_hide_the_preceding_warning(self):
        rows = self.records()
        rows[1:1] = [dict(kind="frame", tick=7, gameTick=100, attack=20, predictedFirstHit=1),
                     dict(kind="hit", tick=7, gameTick=101),
                     dict(kind="frame", tick=8, gameTick=101, attack=52, predictedFirstHit=0)]
        result = self.evaluate(rows)
        self.assertEqual(result["hits_with_immediate_collision_warning"], 1)
        self.assertEqual(result["hit_diagnostics"][0]["state"], 20)
        self.assertEqual(result["hit_diagnostics"][0]["preceding_frame_lag"], 1)

    def test_post_hit_warning_cannot_be_counted_as_advance_warning(self):
        rows = self.records()
        rows[1:1] = [dict(kind="frame", tick=7, gameTick=100, predictedFirstHit=0),
                     dict(kind="hit", tick=7, gameTick=101),
                     dict(kind="frame", tick=8, gameTick=101, predictedFirstHit=1)]
        self.assertEqual(self.evaluate(rows)["hits_with_immediate_collision_warning"], 0)

    def test_old_sample_does_not_prove_a_causal_warning(self):
        rows = self.records()
        rows[1:1] = [dict(kind="frame", tick=7, gameTick=100, predictedFirstHit=1),
                     dict(kind="hit", tick=12, gameTick=106)]
        result = self.evaluate(rows)
        self.assertEqual(result["hits_with_immediate_collision_warning"], 0)
        self.assertEqual(result["hits_without_causal_snapshot"], 1)

    def test_forecast_error_uses_actual_source_instead_of_cached_start(self):
        rows = self.records()
        rows[1:1] = [dict(kind="frame", tick=7, gameTick=100, predictedFirstHit=1),
                     dict(kind="hit", tick=7, gameTick=101, sourceIndex=3,
                          forecast=dict(observedX=0, observedY=0, predictedX=120, predictedY=0),
                          nearby=[dict(id=3, x=120, y=0)])]
        self.assertEqual(self.evaluate(rows)["hit_diagnostics"][0]["source_position_error_px"], 0)
        del rows[2]["nearby"]
        self.assertIsNone(self.evaluate(rows)["hit_diagnostics"][0]["source_position_error_px"])


if __name__ == "__main__":
    unittest.main()
