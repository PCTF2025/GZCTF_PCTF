"""Linux smoke test with a real network namespace and tcpdump (run inside the collector image)."""
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time
import unittest

from collector import Collector


@unittest.skipUnless(os.name == "posix" and shutil.which("tcpdump"), "Linux + tcpdump required")
class LinuxCaptureTest(unittest.TestCase):
    def test_startup_gate_rotation_and_stop_preserve_application_packets(self):
        with tempfile.TemporaryDirectory() as folder:
            collector = Collector.__new__(Collector)
            collector.root = Path(folder)
            (collector.root / "gates").mkdir()
            collector.node = "test-node"
            collector.segment_seconds = 5
            collector.active = {}
            collector.states = {}
            collector.sandbox_pid = lambda _: os.getpid()
            capture_id = "c" * 32
            self.assertFalse((collector.root / "gates" / capture_id).exists())
            collector.start(capture_id, {"pod": "test-pod"}, {})
            deadline = time.monotonic() + 5
            while not (collector.root / "gates" / capture_id).exists() and time.monotonic() < deadline:
                collector.poll_ready(capture_id)
                time.sleep(0.05)
            self.assertTrue((collector.root / "gates" / capture_id).exists())
            self.assertEqual(collector.states[capture_id]["state"], "capturing")
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
                udp.sendto(b"application-first-packet", ("127.0.0.1", 19876))
                time.sleep(6)
                udp.sendto(b"application-after-rotation", ("127.0.0.1", 19876))
                time.sleep(0.2)
            collector.finalize_parts(collector.root / capture_id, keep_latest=True)
            self.assertEqual(len(list((collector.root / capture_id).glob("traffic-*.pcap"))), 1)
            self.assertEqual(len(list((collector.root / capture_id).glob("*.part"))), 1)
            collector.finish(capture_id)
            self.assertEqual(collector.states[capture_id]["state"], "uploading")
            pcaps = list((collector.root / capture_id).glob("traffic-*.pcap"))
            self.assertEqual(len(pcaps), 2)
            data = b"".join(subprocess.check_output(["tcpdump", "-nn", "-A", "-r", str(p)], stderr=subprocess.DEVNULL) for p in pcaps)
            self.assertIn(b"application-first-packet", data)
            self.assertIn(b"application-after-rotation", data)
            self.assertEqual(list((collector.root / capture_id).glob("*.part")), [])


if __name__ == "__main__":
    unittest.main()
