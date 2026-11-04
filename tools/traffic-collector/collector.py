#!/usr/bin/env python3
"""Kubernetes node collector: gate application startup, capture the pod netns, verify then delete.

Only the node agent has the internal API key. Challenge containers never mount the spool.
Uses Python's standard library so uploads stream from disk without loading a pcap into RAM.
"""
import faulthandler
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
from concurrent.futures import ThreadPoolExecutor
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
        started = time.monotonic()
        try:
            return self._json(route, value)
        finally:
            elapsed = time.monotonic() - started
            if elapsed > 2:
                LOG.warning("slow platform call %s: %.2fs", route, elapsed)

    def _json(self, route, value=None):
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
        # 门控最长等待：超时即放行应用（宁可漏抓，也不能让靶机起不来）
        self.grace_start = max(3, int(os.getenv("GRACE_START_SECONDS", "8")))
        self.platform = Platform()
        self.active = {}
        self.states = {}
        # 每个新 Pod 的启动是 crictl + tcpdump，几百毫秒起；串行会让 init 门控排长队。
        # 用线程池并发启动，把「靶机创建 → 应用放行」的等待压到秒级。
        self.starting = set()
        self.upload_failures = {}
        self.report_fingerprints = {}
        self.starters = ThreadPoolExecutor(max_workers=int(os.getenv("START_WORKERS", "12")),
            thread_name_prefix="capture-start")
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

    def pod_waited(self, pod):
        import datetime as _dt
        created = pod["metadata"].get("creationTimestamp")
        if not created:
            return 0
        try:
            born = _dt.datetime.fromisoformat(created.replace("Z", "+00:00"))
            return (_dt.datetime.now(_dt.timezone.utc) - born).total_seconds()
        except Exception:
            return 0

    def allow_start(self, capture_id):
        # Only the trusted init container mounts this directory, read-only.
        gate = self.root / "gates" / capture_id
        gate.touch(mode=0o644, exist_ok=True)

    def start_async(self, capture_id, session, pod):
        """串行 reconciler 里绝不阻塞：把 slow 的 pid 查询与 tcpdump 启动交给线程池。"""
        if capture_id in self.starting or capture_id in self.active:
            return
        self.starting.add(capture_id)

        def work():
            try:
                pid = self.sandbox_pid(pod)
            except Exception:
                LOG.exception("lookup sandbox pid failed for %s", capture_id)
                pid = None
            with self.lock:
                try:
                    if pid is None:
                        return
                    if capture_id in self.active or self.states.get(capture_id, {}).get("stopped"):
                        return
                    self.start(capture_id, session, pod, pid)
                except Exception:
                    LOG.exception("start capture failed for %s", capture_id)
                finally:
                    self.starting.discard(capture_id)

        self.starters.submit(work)

    def start(self, capture_id, session, pod, pid=None):
        if pid is None:
            pid = self.sandbox_pid(pod)
        if pid is None:
            return
        folder = self.root / capture_id
        folder.mkdir(exist_ok=True)
        log_path = folder / "tcpdump.log"
        log = log_path.open("wb")
        # AppArmor 的 tcpdump 策略只允许写 *.pcap，文件名必须以 .pcap 结尾
        output = folder / ("open-" + uuid.uuid4().hex + "-%s.pcap")
        proc = subprocess.Popen(["nsenter", "--target", str(pid), "--net", "tcpdump", "-i", "any",
            "-nn", "-s", "0", "-U", "-Z", "root", "--immediate-mode", "-G", str(self.segment_seconds), "-w", str(output)],
            stdout=subprocess.DEVNULL, stderr=log, start_new_session=True)
        self.active[capture_id] = (proc, log)
        state = self.states.setdefault(capture_id, {"node": self.node, "state": "waiting", "bytes": 0, "files": []})
        state["pod"] = session["pod"]
        state.update(state="waiting", ready=False, readyDeadline=time.monotonic() + 3, stopped=False)
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

    def finalize_parts(self, folder, keep_latest=False):
        parts = sorted(folder.glob("open-*.pcap"), key=lambda p: p.stat().st_mtime_ns)
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
        self.finalize_parts(self.root / capture_id)
        if list((self.root / capture_id).glob("open-*.pcap")):
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
                if pod and state.get("gateReleased"):
                    self.allow_start(capture_id)
                elif pod and not state.get("gateReleased") and self.pod_waited(pod) >= self.grace_start:
                    # tcpdump 起不来（如权限/策略限制）也必须放行应用，不能把靶机卡死
                    state.update(gateReleased=True, error=state.get("error") or "capture unavailable")
                    self.save(capture_id)
                    self.allow_start(capture_id)
                    LOG.warning("gate force-released (stopped state) for %s", capture_id)
            elif pod:
                # 兜底：Pod 已存在超过 GRACE_START 秒仍未开始采集，也必须放行应用，
                # 绝不能让靶机因为采集器问题而起不来。
                waited = self.pod_waited(pod)
                if not entry["stopped"]:
                    if waited >= self.grace_start and capture_id not in self.active:
                        self.states.setdefault(capture_id, {"node": self.node, "state": "waiting",
                            "bytes": 0, "files": [], "pod": session["pod"]})
                        self.states[capture_id].update(state="error", stopped=True, gateReleased=True,
                            error="collector did not start in time; application released without capture")
                        self.save(capture_id)
                        self.allow_start(capture_id)
                        LOG.warning("gate force-released for %s after %.0fs without capture", capture_id, waited)

                annotations = pod["metadata"].get("annotations", {})
                if annotations.get("gzctf.gzti.me/capture-id") != capture_id:
                    continue
                if enabled and not entry["stopped"]:
                    self.start_async(capture_id, session, pod)
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
                if self.upload_failures.get(str(path), 0) >= 3:
                    continue  # 连续失败已暂停，文件保留待人工处理；不再压平台
                try:
                    receipt = self.platform.upload(capture_id, path)
                    self.upload_failures.pop(str(path), None)
                    # Hashing a large closed pcap must never hold the lock used by the capture controller.
                    verified_delete(path, receipt, f"traffic/{capture_id}/{path.name}")
                    with self.lock:
                        if capture_id in self.states and self.states[capture_id]["state"] != "error":
                            self.states[capture_id]["error"] = None
                            self.save(capture_id)
                except Exception as error:
                    attempts = self.upload_failures.get(str(path), 0) + 1
                    self.upload_failures[str(path)] = attempts
                    if attempts >= 3:
                        LOG.error("Upload of %s failed %d times; pausing retries (file kept): %s",
                            path, attempts, error)
                    else:
                        LOG.warning("Retaining %s for retry (%d/3): %s", path, attempts, error)
                    with self.lock:
                        if capture_id in self.states:
                            self.states[capture_id]["error"] = str(error)
                            self.save(capture_id)
            self.done.wait(5)

    def reports(self):
        # Independent from control/capture: OSS downtime or a slow upload cannot delay stopping tcpdump.
        # 只上报「有变化」的会话：历史归档与已结束会话重复上报会持续冲击平台，反而拖慢控制面。
        while not self.done.is_set():
            for capture_id in list(self.states):
                try:
                    with self.lock:
                        folder = self.root / capture_id
                        state = self.states[capture_id]
                        if state.get("reportedArchived") and state["state"] == "archived":
                            continue
                        if state.get("reportedTerminal") and state["state"] == "error":
                            continue
                        receipts = [json.loads(p.read_text()) for p in folder.glob("*.uploaded")]
                        state["files"] = sorted(r["key"].removeprefix("traffic/") for r in receipts)
                        state["bytes"] = sum(r["size"] for r in receipts) + sum(p.stat().st_size for p in folder.glob("*.pcap*"))
                        if state.get("stopped") and not list(folder.glob("*.pcap")) and state["state"] != "error":
                            state.update(state="archived", error=None)
                        self.save(capture_id)
                        report = {k: state.get(k) for k in ("node", "state", "bytes", "files", "error")}
                    fingerprint = (report["state"], report["bytes"], len(report["files"] or []))
                    if self.report_fingerprints.get(capture_id) == fingerprint:
                        continue  # 无变化不再上报
                    self.platform.json(f"sessions/{capture_id}/status", report)
                    self.report_fingerprints[capture_id] = fingerprint
                    if report["state"] == "archived":
                        with self.lock:
                            self.states[capture_id]["reportedArchived"] = True
                            self.save(capture_id)
                    elif report["state"] == "error" and report.get("stopped"):
                        with self.lock:
                            self.states[capture_id]["reportedTerminal"] = True
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
    # 诊断：请求卡住时打印全部线程栈，用于定位阻塞点
    if os.getenv("STALL_DUMP", "1") == "1":
        try:
            faulthandler.dump_traceback_later(6, repeat=True)
            LOG.info("stall watchdog armed: 6s")
        except Exception:
            LOG.exception("cannot arm stall watchdog")
    collector = Collector()
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, lambda *_: collector.done.set())
    collector.run()
