import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import MagicMock

from collector import Collector, atomic_json, digest, verified_delete


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def pcap(self):
        path = self.root / "traffic-001.pcap"
        path.write_bytes(b"pcap" * 20)
        return path

    def test_verified_delete_keeps_a_durable_receipt(self):
        path = self.pcap()
        receipt = {"verified": True, "key": "traffic/a/" + path.name,
            "size": path.stat().st_size, "sha256": digest(path)}
        verified_delete(path, receipt, receipt["key"])
        self.assertFalse(path.exists())
        self.assertEqual(json.loads(path.with_suffix(".uploaded").read_text()), receipt)

    def test_invalid_receipts_never_delete_local_data(self):
        path = self.pcap()
        good = {"verified": True, "key": "traffic/a/" + path.name,
            "size": path.stat().st_size, "sha256": digest(path)}
        for field, bad in (("verified", False), ("key", "other"), ("size", 1), ("sha256", "0" * 64)):
            with self.subTest(field=field):
                with self.assertRaises(ValueError):
                    verified_delete(path, {**good, field: bad}, good["key"])
                self.assertTrue(path.exists())

    def collector(self):
        collector = Collector.__new__(Collector)
        collector.root = self.root
        collector.node = "node-1"
        collector.active = {}
        collector.states = {}
        collector.lock = threading.RLock()
        collector.done = threading.Event()
        collector.platform = MagicMock()
        collector.start = MagicMock()
        collector.finish = MagicMock()
        (self.root / "gates").mkdir()
        return collector

    def entry(self, epoch="one", stopped=False):
        return {"session": {"id": "a" * 32, "pod": "challenge", "epoch": epoch}, "stopped": stopped}

    def pod(self):
        return {"metadata": {"annotations": {"gzctf.gzti.me/capture-id": "a" * 32}}}

    def test_no_solved_condition_and_only_new_epoch_is_captured(self):
        collector = self.collector()
        collector.reconcile({"enabled": True, "epoch": "one", "sessions": [self.entry()]}, {"challenge": self.pod()})
        collector.start.assert_called_once()

    def test_fast_off_on_never_resumes_old_launch(self):
        collector = self.collector()
        collector.reconcile({"enabled": True, "epoch": "two", "sessions": [self.entry()]}, {"challenge": self.pod()})
        collector.start.assert_not_called()
        self.assertTrue((self.root / "gates" / ("a" * 32)).exists())
        self.assertTrue(collector.states["a" * 32]["stopped"])

    def test_failed_start_never_releases_the_application_gate(self):
        collector = self.collector()
        collector.states["a" * 32] = {"stopped": True, "state": "error", "gateReleased": False}
        collector.reconcile({"enabled": True, "epoch": "one", "sessions": [self.entry()]}, {"challenge": self.pod()})
        collector.start.assert_not_called()
        self.assertFalse((self.root / "gates" / ("a" * 32)).exists())

    def test_disable_and_destroy_finalize_current_capture(self):
        for enabled, pods in ((False, {"challenge": self.pod()}), (True, {})):
            with self.subTest(enabled=enabled):
                collector = Collector.__new__(Collector)
                collector.root = self.root
                collector.active = {"a" * 32: (MagicMock(), None)}
                collector.states = {"a" * 32: {}}
                collector.finish = MagicMock()
                collector.reconcile({"enabled": enabled, "epoch": "one", "sessions": [self.entry()]}, pods)
                collector.finish.assert_called_once_with("a" * 32)

    def test_rotation_does_not_upload_the_open_part(self):
        collector = self.collector()
        for number in range(3):
            path = self.root / f"open-{number}.pcap.part"
            path.write_bytes(b"x" * 40)
            import os
            os.utime(path, ns=(number + 1, number + 1))
        collector.finalize_parts(self.root, keep_latest=True)
        self.assertEqual(len(list(self.root.glob("traffic-*.pcap"))), 2)
        self.assertTrue((self.root / "open-2.pcap.part").exists())

    def test_restart_recovers_closed_parts_without_removing_contents(self):
        collector = self.collector()
        path = self.root / "open-old.pcap.part"
        path.write_bytes(b"x" * 40)
        collector.finalize_parts(self.root)
        self.assertEqual(next(self.root.glob("*.pcap")).read_bytes(), b"x" * 40)


if __name__ == "__main__":
    unittest.main()
