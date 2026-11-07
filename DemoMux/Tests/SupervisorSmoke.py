"""Regression checks for abnormal-exit cleanup; no third-party packages."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time


def live(pid):
    result = subprocess.run(["ps", "-p", str(pid), "-o", "stat="], capture_output=True, text=True)
    return result.returncode == 0 and not result.stdout.strip().startswith("Z")


def main():
    helper = Path(sys.argv[1]).resolve()
    assert subprocess.run([str(helper), "/bin/sh", "-c", "exit 23"]).returncode == 23
    assert subprocess.run([str(helper), "/usr/bin/true"]).returncode == 0
    with tempfile.TemporaryDirectory(prefix="dmx-supervisor-test-") as temporary:
        record = Path(temporary) / "pids.json"
        parent_code = """
import json, subprocess, sys, time
from pathlib import Path
child = subprocess.Popen([sys.argv[1], "/bin/sh", "-c", "sleep 60 & wait"])
for _ in range(100):
    result = subprocess.run(["pgrep", "-P", str(child.pid)], capture_output=True, text=True)
    if result.stdout.strip():
        component = int(result.stdout.split()[0])
        descendants = subprocess.run(["pgrep", "-P", str(component)], capture_output=True, text=True)
        if descendants.stdout.strip():
            Path(sys.argv[2]).write_text(json.dumps([child.pid, component, int(descendants.stdout.split()[0])]))
            break
    time.sleep(.02)
time.sleep(60)
"""
        for parent_signal in [signal.SIGKILL, signal.SIGTERM]:
            if record.exists():
                record.unlink()
            parent = subprocess.Popen([sys.executable, "-c", parent_code, str(helper), str(record)])
            members = []
            try:
                deadline = time.monotonic() + 5
                while not record.exists() and time.monotonic() < deadline:
                    time.sleep(.02)
                members = json.loads(record.read_text())
                assert all(live(pid) for pid in members)
                parent.send_signal(parent_signal)
                parent.wait(timeout=3)
                deadline = time.monotonic() + 3
                while any(live(pid) for pid in members) and time.monotonic() < deadline:
                    time.sleep(.03)
                assert not any(live(pid) for pid in members), ("orphan", members)
                print(f"Passed: parent {parent_signal.name}, component and grandchild exited")
            finally:
                if parent.poll() is None:
                    parent.kill()
                    parent.wait()
                for pid in members:
                    if live(pid):
                        os.kill(pid, signal.SIGKILL)
    print("Passed: component exit status propagation")


if __name__ == "__main__":
    main()
