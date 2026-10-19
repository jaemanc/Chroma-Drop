"""실제 Unity 대신 임시 실행 파일로 dev.sh의 실패 전파를 검증한다."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class DevScriptTests(unittest.TestCase):
    def run_dev(self, compile_status=0, unity_status=0, result="Passed", passed=2, editor_check=1):
        with tempfile.TemporaryDirectory(prefix="chromadrop-dev-test-") as tmp:
            root = Path(tmp)
            (root / "Tools").mkdir()
            shutil.copyfile(Path(__file__).resolve().parents[1] / "Tools/dev.sh", root / "Tools/dev.sh")
            (root / "ProjectSettings").mkdir()
            (root / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: test\n")

            def executable(path, body):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("#!/usr/bin/env bash\n" + body + "\n")
                path.chmod(0o755)

            executable(root / "Tools/sync-stages.sh", "exit 0")
            executable(root / "bin/pgrep", f"exit {editor_check}")
            executable(root / "bin/pkill", "exit 1")
            mono = root / "Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin"
            executable(mono / "mcs", f"exit {compile_status}")
            executable(mono / "mono", "exit 0")
            unity = root / "Unity.app/Contents/MacOS/Unity"
            # 실패 종료여도 통과 XML이 남은 경우를 포함한다.
            executable(unity, '''while [ "$#" -gt 0 ]; do
  if [ "$1" = "-testResults" ]; then shift; output="$1"; fi
  shift
done
mkdir -p Logs
touch Logs/dev-test.log
''' + f'''echo '<test-run result="{result}" passed="{passed}" failed="0" skipped="0" />' > "$output"
exit {unity_status}''')
            env = dict(os.environ, UNITY_PATH=str(unity), PATH=str(root / "bin") + os.pathsep + os.environ["PATH"])
            return subprocess.run(["bash", "Tools/dev.sh", "--test"], cwd=root,
                                  env=env, capture_output=True, text=True)

    def test_compile_failure_stops_before_unity(self):
        run = self.run_dev(compile_status=1)
        self.assertNotEqual(run.returncode, 0)
        self.assertNotIn("▶ PlayMode", run.stdout)

    def test_editor_check_failure_stops_before_unity(self):
        run = self.run_dev(editor_check=2)
        self.assertNotEqual(run.returncode, 0)
        self.assertNotIn("▶ PlayMode", run.stdout)
        self.assertIn("실행 상태를 확인할 수 없다", run.stderr)

    def test_unity_failure_rejects_passing_xml(self):
        self.assertNotEqual(self.run_dev(unity_status=2).returncode, 0)

    def test_empty_test_run_fails(self):
        self.assertNotEqual(self.run_dev(passed=0).returncode, 0)

    def test_suite_failure_fails(self):
        self.assertNotEqual(self.run_dev(result="Failed").returncode, 0)

    def test_passing_run_succeeds(self):
        run = self.run_dev()
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)


if __name__ == "__main__":
    unittest.main()
