"""验证独立运行版的性能门槛与失败传播，不启动 Unity。"""
import contextlib
import io
import json
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

from Tools import verify


def sample(**changes):
    data = dict(scenario="play", screenWidth=1280, screenHeight=720,
                frameCount=1200, p95Ms=16.5, p99Ms=20.0, low1Fps=55.0)
    data.update(changes)
    return data


class PlayerGateTests(unittest.TestCase):
    def stage(self, samples, build_ok=True):
        # 隔离进程启动和文档写入，保留实际门槛判断与汇总逻辑。
        with patch.object(verify, "build_player", return_value=(build_ok, "测试构建")), \
             patch.object(verify, "run_player", side_effect=samples), \
             patch.object(verify, "upsert_section"), \
             contextlib.redirect_stdout(io.StringIO()):
            return verify.run_player_stage(None, 20, ["play", "switch"], 120)[0]

    def test_boundary_values_pass(self):
        self.assertTrue(self.stage([(sample(), None), (sample(scenario="switch"), None)]))

    def test_missing_scenario_fails(self):
        self.assertFalse(self.stage([(sample(), None), (None, "没有输出")]))

    def test_build_failure_fails(self):
        self.assertFalse(self.stage([], build_ok=False))

    def test_slow_p99_fails(self):
        self.assertFalse(self.stage([(sample(p99Ms=20.01), None), (sample(scenario="switch"), None)]))

    def test_low_fps_fails(self):
        self.assertFalse(self.stage([(sample(), None), (sample(scenario="switch", low1Fps=54.99), None)]))

    def test_invalid_measurements_fail(self):
        for invalid in (float("nan"), float("inf"), 0, -1):
            with self.subTest(invalid=invalid):
                self.assertFalse(self.stage([(sample(p99Ms=invalid), None), (sample(scenario="switch"), None)]))

    def test_insufficient_frames_fail(self):
        self.assertFalse(self.stage([(sample(frameCount=0), None), (sample(scenario="switch"), None)]))

    def test_mismatched_scenario_fails(self):
        self.assertFalse(self.stage([(sample(), None), (sample(), None)]))

    def test_mismatched_window_size_fails(self):
        self.assertFalse(self.stage([(sample(screenWidth=2560, screenHeight=1494), None),
                                     (sample(scenario="switch"), None)]))

    def test_crashed_player_does_not_pass_with_json(self):
        process = Mock(returncode=1)
        with patch.object(Path, "exists", side_effect=[False, True]), \
             patch.object(Path, "read_text", return_value=json.dumps(sample())), \
             patch.object(verify.subprocess, "Popen", return_value=process), \
             contextlib.redirect_stdout(io.StringIO()):
            data, error = verify.run_player(Path("PianoTeacher3D.exe"), 20, "play", 120)
        self.assertIsNone(data)
        self.assertIn("exit=1", error)

    def test_failed_build_does_not_pass_with_executable(self):
        process = Mock(returncode=1)
        with patch.object(Path, "exists", side_effect=[False, True]), \
             patch.object(verify.subprocess, "Popen", return_value=process), \
             contextlib.redirect_stdout(io.StringIO()):
            ok, error = verify.build_player("Unity.exe", 120)
        self.assertFalse(ok)
        self.assertIn("exit=1", error)

    def test_player_failure_reaches_cli_exit_code(self):
        # 编译与测试均通过时，Player 失败仍必须使整条验证命令失败。
        passed = dict(total=1, passed=1, failed=0, skipped=0, failed_names=[], skipped_names=[])
        with patch("sys.argv", ["verify.py", "--player"]), \
             patch.object(verify, "find_unity", return_value="Unity.exe"), \
             patch.object(verify, "run_unity", return_value=("completed", None)), \
             patch.object(verify, "grep_compile_errors", return_value=[]), \
             patch.object(verify, "parse_results", return_value=passed), \
             patch.object(verify, "run_player_stage", return_value=(False, ["采样失败"])), \
             patch.object(verify, "print_artifacts"), \
             contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(verify.main(), 1)


if __name__ == "__main__":
    unittest.main()
