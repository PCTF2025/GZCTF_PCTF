#!/usr/bin/env python3
"""Kubernetes node collector: gate application startup, capture the pod netns, verify then delete.

Only the node agent has the internal API key. Challenge containers never mount the spool.
Uses Python's standard library so uploads stream from disk without loading a pcap into RAM.
"""
import hashlib
import http.client
import json
import logging
import os
from pathlib import Path
import signal
import ssl
import subprocess
import threading
import time
import urllib.parse
import urllib.request
import uuid

LOG = logging.getLogger("traffic-collector")


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    with temp.open("w", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False)
        stream.flush()
        os.fsync(stream.fileno())
    temp.replace(path)


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verified_delete(path, receipt, expected_key):
    """A successful HTTP status alone never authorizes deleting the local capture."""
    path = Path(path)
    if (receipt.get("verified") is not True or receipt.get("key") != expected_key
            or receipt.get("size") != path.stat().st_size or receipt.get("sha256") != digest(path)):
        raise ValueError("OSS verification receipt does not match the local file")
    # Persist the index BEFORE deletion, so a crash cannot lose the remote object reference.
    atomic_json(path.with_suffix(".uploaded"), receipt)
    path.unlink()


class Platform:
    def __init__(self):
        self.base = os.environ["PLATFORM_URL"].rstrip("/")
        self.key = os.environ["GZCTF_INTERNAL_API_KEY"]

    def json(self, route, value=None):
        data = None if value is None else json.dumps(value).encode()
        request = urllib.request.Request(self.base + "/api/internal/traffic/" + route,
            data=data, method="GET" if value is None else "PUT",
            headers={"X-Internal-Key": self.key, "Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=10) as response:
            payload = response.read()
            return json.loads(payload) if payload else None

    def upload(self, capture_id, path):
        boundary = uuid.uuid4().hex
        sha256 = digest(path)
        before = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"sha256\"\r\n\r\n{sha256}\r\n"
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{path.name}\"\r\n"
            "Content-Type: application/vnd.tcpdump.pcap\r\n\r\n").encode()
        after = f"\r\n--{boundary}--\r\n".encode()
        url = urllib.parse.urlsplit(self.base + f"/api/internal/traffic/sessions/{capture_id}/upload")
        cls = http.client.HTTPSConnection if url.scheme == "https" else http.client.HTTPConnection
        connection = cls(url.hostname, url.port, timeout=600)
        try:
            connection.putrequest("POST", url.path)
            connection.putheader("X-Internal-Key", self.key)
            connection.putheader("Content-Type", "multipart/form-data; boundary=" + boundary)
            connection.putheader("Content-Length", str(len(before) + path.stat().st_size + len(after)))
            connection.endheaders()
            connection.send(before)
            with path.open("rb") as stream:
                while chunk := stream.read(1024 * 1024):
                    connection.send(chunk)
            connection.send(after)
            response = connection.getresponse()
            payload = response.read()
            if response.status != 200:
                raise RuntimeError(f"Upload returned HTTP {response.status}")
            return json.loads(payload)
        finally:
            connection.close()


class Collector:
    def __init__(self):
        self.root = Path(os.getenv("CAPTURE_ROOT", "/var/lib/gzctf-traffic"))
        self.root.mkdir(parents=True, exist_ok=True)
        (self.root / "gates").mkdir(exist_ok=True)
        self.node = os.environ["NODE_NAME"]
        self.namespace = os.getenv("CHALLENGE_NAMESPACE", "gzctf-challenges")
        self.runtime = os.getenv("RUNTIME_ENDPOINT", "unix:///run/containerd/containerd.sock")
        self.segment_seconds = max(5, int(os.getenv("SEGMENT_SECONDS", "300")))
        self.platform = Platform()
        self.active = {}
        self.states = {}
        self.lock = threading.RLock()
        self.done = threading.Event()
        self.kube_ssl = ssl.create_default_context(cafile="/var/run/secrets/kubernetes.io/serviceaccount/ca.crt")
        self.kube_url = "https://" + os.environ["KUBERNETES_SERVICE_HOST"] + ":" + os.getenv("KUBERNETES_SERVICE_PORT", "443")
        for path in self.root.glob("*/state.json"):
            state = json.loads(path.read_text())
            self.states[path.parent.name] = state
            # Old tcpdump belongs to the terminated DaemonSet container. Its files are closed.
            self.finalize_parts(path.parent)

    def pods(self):
        query = urllib.parse.urlencode({"fieldSelector": "spec.nodeName=" + self.node,
            "labelSelector": "gzctf.gzti.me/ResourceId"})
        token = Path("/var/run/secrets/kubernetes.io/serviceaccount/token").read_text().strip()
        request = urllib.request.Request(self.kube_url + f"/api/v1/namespaces/{self.namespace}/pods?{query}",
            headers={"Authorization": "Bearer " + token})
        with urllib.request.urlopen(request, context=self.kube_ssl, timeout=5) as response:
            return {p["metadata"]["name"]: p for p in json.load(response)["items"]}

    def sandbox_pid(self, pod):
        def cri(*args):
            return json.loads(subprocess.check_output(["crictl", "--runtime-endpoint", self.runtime, *args], timeout=5))
        for sandbox in cri("pods", "--name", pod["metadata"]["name"], "-o", "json").get("items", []):
            if sandbox.get("metadata", {}).get("uid") == pod["metadata"]["uid"] and sandbox.get("state") == "SANDBOX_READY":
                pid = cri("inspectp", sandbox["id"]).get("info", {}).get("pid")
                if pid:
                    return int(pid)
        return None

    def save(self, capture_id):
        atomic_json(self.root / capture_id / "state.json", self.states[capture_id])

    def allow_start(self, capture_id):
        # Only the trusted init container mounts this directory, read-only.
        gate = self.root / "gates" / capture_id
        gate.touch(mode=0o644, exist_ok=True)

    def start(self, capture_id, session, pod):
        pid = self.sandbox_pid(pod)
        if pid is None:
            return
        folder = self.root / capture_id
        folder.mkdir(exist_ok=True)
        log_path = folder / "tcpdump.log"
        log = log_path.open("wb")
        # Distribution AppArmor profiles commonly allow tcpdump to write only *.pcap.
        # Keep that suffix while the open-* prefix excludes active files from uploads.
        output = folder / ("open-" + uuid.uuid4().hex + "-%s.part.pcap")
        proc = subprocess.Popen(["nsenter", "--target", str(pid), "--net", "tcpdump", "-i", "any",
            "-nn", "-s", "0", "-U", "-Z", "root", "--immediate-mode", "-G", str(self.segment_seconds), "-w", str(output)],
            stdout=subprocess.DEVNULL, stderr=log, start_new_session=True)
        self.active[capture_id] = (proc, log)
        state = self.states.setdefault(capture_id, {"node": self.node, "state": "waiting", "bytes": 0, "files": []})
        state["pod"] = session["pod"]
        state.update(state="waiting", ready=False, readyDeadline=time.monotonic() + 5, stopped=False)
        self.save(capture_id)
        self.poll_ready(capture_id)

    def poll_ready(self, capture_id):
        proc = self.active[capture_id][0]
        state = self.states[capture_id]
        log_path = self.root / capture_id / "tcpdump.log"
        # tcpdump announces readiness AFTER opening the device/output. Never release the init gate before that.
        # Readiness is polled without waiting, so one failing launch cannot delay a global stop.
        if proc.poll() is None and b"listening on" in log_path.read_bytes():
            state.update(state="capturing", error=None, gateReleased=True, stopped=False, ready=True)
            self.save(capture_id)
            self.allow_start(capture_id)
        elif proc.poll() is not None or time.monotonic() >= state["readyDeadline"]:
            self.finish(capture_id, "tcpdump did not become ready; application startup remains blocked")

    @staticmethod
    def open_parts(folder):
        # Recover the previous *.pcap.part format after an upgrade as well.
        parts = list(folder.glob("open-*.part.pcap")) + list(folder.glob("*.part"))
        return sorted(parts, key=lambda p: p.stat().st_mtime_ns)

    def finalize_parts(self, folder, keep_latest=False):
        parts = self.open_parts(folder)
        if keep_latest:
            parts = parts[:-1]
        for path in parts:
            # A zero-byte/incomplete header signals capture failure and is retained for diagnosis.
            if path.stat().st_size < 24:
                continue
            path.rename(folder / ("traffic-" + uuid.uuid4().hex + ".pcap"))

    def finish(self, capture_id, error=None):
        running = self.active.pop(capture_id, None)
        if running:
            proc, log = running
            if proc.poll() is None:
                proc.send_signal(signal.SIGINT)
                try:
                    proc.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait()
                    error = error or "tcpdump exceeded flush timeout; check capture completeness"
            elif proc.returncode != 0:
                error = error or "tcpdump exited unexpectedly; capture may be incomplete"
            log.close()
            if proc.returncode is not None:
                self.states[capture_id]["exitCode"] = proc.returncode
            if error and proc.returncode not in (None, 0):
                log_path = self.root / capture_id / "tcpdump.log"
                if log_path.exists():
                    with log_path.open("rb") as source:
                        source.seek(max(0, log_path.stat().st_size - 4096))
                        lines = source.read().decode("utf-8", errors="replace").strip().splitlines()
                    if lines:
                        error = f"{error} (exit code {proc.returncode}): {lines[-1][:1000]}"
        self.finalize_parts(self.root / capture_id)
        if self.open_parts(self.root / capture_id):
            error = error or "incomplete capture header; original part retained for diagnosis"
        state = self.states[capture_id]
        state.update(state="error" if error else "uploading", error=error, stopped=True)
        self.save(capture_id)

    def reconcile(self, control, pods):
        sessions = {s["session"]["id"].replace("-", ""): s for s in control["sessions"]}
        for capture_id, entry in sessions.items():
            session = entry["session"]
            pod = pods.get(session["pod"])
            state = self.states.get(capture_id)
            enabled = control["enabled"] and session["epoch"] == control["epoch"]
            if capture_id in self.active:
                if not enabled or pod is None or pod.get("status", {}).get("phase") in ("Succeeded", "Failed"):
                    self.finish(capture_id)
                elif self.active[capture_id][0].poll() is not None:
                    self.finish(capture_id, "tcpdump exited unexpectedly; capture may be incomplete")
                elif not state.get("ready"):
                    self.poll_ready(capture_id)
                else:
                    self.finalize_parts(self.root / capture_id, keep_latest=True)
            elif state and state.get("stopped"):
                if (pod and enabled and not entry["stopped"] and not state.get("gateReleased")
                        and pod.get("status", {}).get("phase") not in ("Failed", "Succeeded")):
                    # A failed first start has not admitted the application. Retrying is safe
                    # after a node/image fix, while operator-disabled sessions remain stopped.
                    self.start(capture_id, session, pod)
                elif pod and state.get("gateReleased"):
                    self.allow_start(capture_id)
            elif pod:
                annotations = pod["metadata"].get("annotations", {})
                if annotations.get("gzctf.gzti.me/capture-id") != capture_id:
                    continue
                if enabled and not entry["stopped"]:
                    self.start(capture_id, session, pod)
                else:
                    # Off during scheduling: this Pod starts without capture and must never resume.
                    self.states[capture_id] = {"node": self.node, "state": "stopped", "bytes": 0,
                        "files": [], "stopped": True, "gateReleased": True, "pod": session["pod"]}
                    self.save(capture_id)
                    self.allow_start(capture_id)
            elif state:
                self.finish(capture_id)

    def uploads(self):
        while not self.done.is_set():
            for path in self.root.glob("*/traffic-*.pcap"):
                capture_id = path.parent.name
                try:
                    receipt = self.platform.upload(capture_id, path)
                    # Hashing a large closed pcap must never hold the lock used by the capture controller.
                    verified_delete(path, receipt, f"traffic/{capture_id}/{path.name}")
                    with self.lock:
                        if capture_id in self.states and self.states[capture_id]["state"] != "error":
                            self.states[capture_id]["error"] = None
                            self.save(capture_id)
                except Exception as error:
                    LOG.warning("Retaining %s for retry: %s", path, error)
                    with self.lock:
                        if capture_id in self.states:
                            self.states[capture_id]["error"] = str(error)
                            self.save(capture_id)
            self.done.wait(5)

    def reports(self):
        # Independent from control/capture: OSS downtime or a slow upload cannot delay stopping tcpdump.
        while not self.done.is_set():
            for capture_id in list(self.states):
                try:
                    with self.lock:
                        folder = self.root / capture_id
                        state = self.states[capture_id]
                        if state.get("reportedArchived") and state["state"] == "archived":
                            continue
                        receipts = [json.loads(p.read_text()) for p in folder.glob("*.uploaded")]
                        state["files"] = sorted(r["key"].removeprefix("traffic/") for r in receipts)
                        state["bytes"] = sum(r["size"] for r in receipts) + sum(p.stat().st_size for p in folder.glob("*.pcap*"))
                        if state.get("stopped") and not list(folder.glob("*.pcap")) and state["state"] != "error":
                            state.update(state="archived", error=None)
                        self.save(capture_id)
                        report = {k: state.get(k) for k in ("node", "state", "bytes", "files", "error")}
                    self.platform.json(f"sessions/{capture_id}/status", report)
                    if report["state"] == "archived":
                        with self.lock:
                            self.states[capture_id]["reportedArchived"] = True
                            self.save(capture_id)
                except Exception:
                    LOG.exception("Status report failed for %s", capture_id)
            self.done.wait(5)

    def run(self):
        threading.Thread(target=self.uploads, daemon=True).start()
        threading.Thread(target=self.reports, daemon=True).start()
        try:
            while not self.done.is_set():
                try:
                    control = self.platform.json("control")
                    # A confirmed off/epoch change must stop all writers even if the Kubernetes API is down.
                    with self.lock:
                        stop_ids = []
                        for capture_id in list(self.active):
                            epoch = next((e["session"]["epoch"] for e in control["sessions"]
                                if e["session"]["id"].replace("-", "") == capture_id), None)
                            if not control["enabled"] or epoch != control["epoch"]:
                                stop_ids.append(capture_id)
                        # Stop every writer first; waiting for one busy process must not leave the
                        # remaining Pods recording after the administrator has disabled capture.
                        for capture_id in stop_ids:
                            proc = self.active[capture_id][0]
                            if proc.poll() is None:
                                proc.send_signal(signal.SIGINT)
                        for capture_id in stop_ids:
                            self.finish(capture_id)
                    pods = self.pods()
                    with self.lock:
                        self.reconcile(control, pods)
                except Exception:
                    # Continue existing captures on control-plane outages. Never discard local data.
                    LOG.exception("Collector reconciliation failed")
                self.done.wait(1)
        finally:
            with self.lock:
                for capture_id in list(self.active):
                    self.finish(capture_id, "node collector stopped; capture will continue in a new part after restart")
                    # Service restart is not a user disable. Permit resuming this same launch on restart.
                    self.states[capture_id]["stopped"] = False
                    self.save(capture_id)


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    collector = Collector()
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, lambda *_: collector.done.set())
    collector.run()
