"""Non-gating ordinary Native AOT JSON launch/edit/Save diagnostic.

The external clocks and child trace clocks remain separate. Native source draw
callback return is not compositor presentation. All input is synthetic and every
write is confined to repository .temp/.cache via the existing artifact contract.
"""

import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import time
import uuid

from save_diagnostic import ENVIRONMENT_KEY, SaveDiagnosticCollector

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("native_acceptance", ROOT / "benchmarks/NativeAcceptance/acceptance.py")
acceptance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(acceptance)
reader_spec = importlib.util.spec_from_file_location(
    "mote_causal_save_reader", ROOT / "tests/causal_save_trace_reader.py")
causal_save = importlib.util.module_from_spec(reader_spec)
sys.modules[reader_spec.name] = causal_save
reader_spec.loader.exec_module(causal_save)
EDIT_OFFSET = 9
POLL_SECONDS = 0.05


def digest(path):
    """Hash exact file bytes by streaming, never materialize a large document."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def expected_digest(path):
    """Compute the independent one-byte r-to-X replacement oracle before launch."""
    with path.open("rb") as stream:
        head = stream.read(EDIT_OFFSET)
        if head != b'[ {"id":"' or stream.read(1) != b"r":
            raise ValueError("fixture edit witness differs")
        result = hashlib.sha256(head + b"X")
        while block := stream.read(131072):
            result.update(block)
        return result.hexdigest()


def prepare(directory, sizes):
    """Reuse the established many-line corpus with a space at its first LF.

    The first string then has identical native/source indices on both hosts.
    Every following record/newline and exact byte size retain the prior corpus
    contract; the edit remains inside one root-array object's string value.
    """
    cases = []
    for size in sizes:
        path = acceptance.artifact_path(directory / f"json-many-{size}.json", ".temp")
        acceptance.generate(path, "json", "many", size * 1048576)
        with path.open("r+b") as stream:
            if stream.read(2) != b"[\n":
                raise ValueError("upstream corpus prefix differs")
            stream.seek(1)
            stream.write(b" ")
        cases.append({"size_mib": size, "size_bytes": path.stat().st_size,
                      "fixture": path, "input_sha256": digest(path),
                      "expected_saved_sha256": expected_digest(path)})
    return cases


def inventory(executable, rid):
    """Require the actual publish directory to contain one regular executable."""
    executable = Path(os.path.abspath(executable))
    expected = "mote.exe" if rid.startswith("win-") else "mote"
    for entry in (executable, *executable.parents):
        if entry.is_symlink() or getattr(os.path, "isjunction", lambda _: False)(entry):
            raise ValueError("linked binary ancestry")
        if entry.exists() and getattr(entry.lstat(), "st_file_attributes", 0) & 1024:
            raise ValueError("reparse binary ancestry")
    entries = list(executable.parent.iterdir())
    if executable.name != expected or not executable.is_file() or entries != [executable]:
        raise ValueError("strict single-binary inventory differs")
    actual = platform.machine().lower()
    architecture = "arm64" if actual in ("arm64", "aarch64") else "x64" if actual in ("amd64", "x86_64") else "unknown"
    host = "win" if sys.platform == "win32" else "osx" if sys.platform == "darwin" else "unknown"
    if rid != f"{host}-{architecture}":
        raise ValueError("RID differs from native observer host")
    return executable, {"binary_sha256": digest(executable), "binary_bytes": executable.stat().st_size,
                        "inventory_entries": 1, "observer_architecture": architecture}


def environment(home):
    """Isolate configuration, cache and local traces before starting a timer."""
    env = os.environ.copy()
    env.pop(ENVIRONMENT_KEY, None)
    env["MOTE_HOME"] = str(home)
    env["MOTE_TRACE"] = "1"
    return env


def bounded_command(arguments, seconds, env=None):
    """Bound tool execution; subprocess.run kills/reaps its direct child on timeout."""
    return subprocess.run(arguments, cwd=ROOT, env=env, timeout=seconds,
                          stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=True)


class WindowsDriver:
    """Exact-PID HWND observer and bounded system-message input; no foreground mutation."""

    def __init__(self, pid, size, client=None):
        """Declare pointer-width-correct ABIs on both Windows x64 and ARM64."""
        from ctypes import wintypes as w
        self.pid, self.size, self.main, self.host = pid, size, 0, 0
        self.api = ctypes.WinDLL("user32", use_last_error=True)
        self.callback = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
        signatures = {
            "EnumWindows": ([self.callback, w.LPARAM], w.BOOL),
            "GetWindowThreadProcessId": ([w.HWND, ctypes.POINTER(w.DWORD)], w.DWORD),
            "GetClassNameW": ([w.HWND, w.LPWSTR, ctypes.c_int], ctypes.c_int),
            "FindWindowExW": ([w.HWND, w.HWND, w.LPCWSTR, w.LPCWSTR], w.HWND),
            "GetDlgItem": ([w.HWND, ctypes.c_int], w.HWND),
            "SendMessageTimeoutW": ([w.HWND, w.UINT, ctypes.c_size_t, ctypes.c_ssize_t,
                                      w.UINT, w.UINT, ctypes.POINTER(ctypes.c_size_t)], ctypes.c_ssize_t),
            "PostMessageW": ([w.HWND, w.UINT, ctypes.c_size_t, ctypes.c_ssize_t], w.BOOL),
            "GetWindow": ([w.HWND, w.UINT], w.HWND),
            "IsWindowEnabled": ([w.HWND], w.BOOL),
        }
        for name, (arguments, result) in signatures.items():
            function = getattr(self.api, name)
            function.argtypes, function.restype = arguments, result

    def owned(self, handle):
        """Recheck process ownership before each action/read, never trust a stale HWND."""
        process_id = ctypes.c_ulong()
        self.api.GetWindowThreadProcessId(handle, ctypes.byref(process_id))
        if not handle or process_id.value != self.pid:
            raise RuntimeError("target HWND ownership differs")

    def send(self, handle, message, first=0, second=0):
        """Send only to owned HWNDs with a strict 2-second individual bound."""
        self.owned(handle)
        result = ctypes.c_size_t()
        if not self.api.SendMessageTimeoutW(handle, message, first, second, 0x23, 2000, ctypes.byref(result)):
            raise TimeoutError("owned Win32 message failed")
        return result.value

    def text(self, handle, capacity):
        """Read a bounded synthetic host/chrome string; never persist its contents."""
        buffer = ctypes.create_unicode_buffer(capacity)
        self.send(handle, 0x000D, capacity, ctypes.addressof(buffer))
        return buffer.value

    def observe(self, version):
        """Observe ordinary source binding and exact-version certified status separately."""
        if not self.main:
            @self.callback
            def visit(handle, _):
                process_id = ctypes.c_ulong()
                self.api.GetWindowThreadProcessId(handle, ctypes.byref(process_id))
                if process_id.value == self.pid:
                    name = ctypes.create_unicode_buffer(128)
                    self.api.GetClassNameW(handle, name, len(name))
                    if name.value == "MoteNativeEditorWindow":
                        self.main = handle
                        return False
                return True
            self.api.EnumWindows(visit, 0)
        if not self.main:
            return {"ready": False, "complete": False}
        canvas = self.api.FindWindowExW(self.main, 0, "MoteInteractiveCanvas", None)
        self.host = self.api.FindWindowExW(canvas, 0, "RICHEDIT50W", None) if canvas else 0
        if not self.host:
            return {"ready": False, "complete": False}
        length = self.send(self.host, 0x000E)
        prefix = self.text(self.host, 64) if length else ""
        status = self.api.GetDlgItem(self.main, 103)
        status_text = self.text(status, 1024) if status else ""
        expected_prefix = '[ {"id":"row"' if version == 0 else '[ {"id":"Xow"'
        return {"ready": 0 < length <= 16384 and prefix.startswith(expected_prefix),
                "complete": f"JSON · Complete · v{version}" in status_text and "No diagnostics." in status_text,
                "bounded_input_units": length, "source_proxy_scope": "bounded-native-host-not-full-source-UIA",
                "focused": None, "modified": " •" in self.text(self.main, 512)}

    def failure_observation(self):
        """Read only owned dialog count/enabled/dirty metadata; never dialog bodies."""
        count = 0
        @self.callback
        def visit(handle, _):
            nonlocal count
            process_id = ctypes.c_ulong()
            self.api.GetWindowThreadProcessId(handle, ctypes.byref(process_id))
            if process_id.value == self.pid and self.api.GetWindow(handle, 4) == self.main:
                name = ctypes.create_unicode_buffer(64)
                self.api.GetClassNameW(handle, name, len(name))
                if name.value == "#32770":
                    count += 1
            return True
        self.api.EnumWindows(visit, 0)
        self.owned(self.main)
        return {"owned_direct_dialog_count": count, "main_enabled": bool(self.api.IsWindowEnabled(self.main)),
                "main_modified": " •" in self.text(self.main, 512)}

    def edit(self):
        """Replace exactly one witnessed string byte through the native input host once."""
        if not self.observe(0)["ready"]:
            raise RuntimeError("edit prefix witness missing")
        self.send(self.host, 0x00B1, EDIT_OFFSET, EDIT_OFFSET + 1)
        self.send(self.host, 0x0102, ord("X"))

    def save(self):
        """Dispatch the ordinary target-window Save menu command without global keys."""
        self.owned(self.main)
        if not self.api.PostMessageW(self.main, 0x0111, 203, 0):
            raise RuntimeError("owned Save dispatch failed")
        return {"method": "owned-HWND-WM_COMMAND", "status": "queued-not-execution-acknowledged",
                "target_pid": self.pid, "queued_messages": 1, "execution_acknowledged": False}

    def close(self):
        """Request normal close only on the owned editor window."""
        self.owned(self.main)
        if not self.api.PostMessageW(self.main, 0x0010, 0, 0):
            raise RuntimeError("owned close dispatch failed")


class MacDriver:
    """Bounded exact-PID AX observer and process-specific Quartz client."""

    def __init__(self, pid, size, client):
        """Retain only process identity, ASCII character count and compiled client."""
        self.pid, self.size, self.client = pid, size, client
        self.last_report = None
        self.last_edit_report = None
        self.observation_started_ns = time.perf_counter_ns()
        self.observation_attempts = 0
        self.first_ax_error = None
        self.last_ax_error = None
        self.first_ready_ms = None
        self.last_observation_elapsed_ms = None
        self.copy_observation_attempts = 0
        self.first_copy_error = None
        self.last_copy_error = None
        self.count_pending_observations = 0
        self.copy_pending_observations = 0
        self.validated_observations = 0

    def command(self, operation, version):
        """A 6-second outer watchdog bounds each client's 0.15-second AX calls."""
        result = bounded_command([str(self.client), str(self.pid), str(self.size), operation, str(version)], 6)
        if len(result.stdout) > 4096:
            raise ValueError("Mac client output exceeds bound")
        data = json.loads(result.stdout)
        if data.get("requested_pid") != self.pid:
            raise ValueError("Mac client process identity differs")
        self.last_report = mac_report(data)
        data = self.last_report
        if data.get("status") == "blocked":
            raise PermissionError("Mac capability blocked")
        if data.get("status") != "observed":
            raise RuntimeError("Mac source contract failed")
        if operation != "observe" and -25204 in (data.get("ax_error"), data.get("window_copy_error")):
            raise RuntimeError("unresolved Mac read cannot certify a modifying transaction")
        return data

    def observe(self, version):
        """Return content-free source/focus/semantic certification observations."""
        self.observation_attempts += 1
        previous_report = self.last_report
        try:
            data = self.command("observe", version)
            return data
        finally:
            # Retain count/copy guard metadata even on a fatal read. These parent
            # clock measurements include client launch/IPC; they are not child
            # render timings and do not alter the outer endpoint deadline.
            self.last_observation_elapsed_ms = (time.perf_counter_ns() - self.observation_started_ns) / 1e6
            if self.last_report is not None and self.last_report is not previous_report:
                error = self.last_report.get("ax_error")
                if self.validated_observations == 0:
                    self.first_ax_error = error
                self.validated_observations += 1
                self.last_ax_error = error
                if error == -25204:
                    self.count_pending_observations += 1
                copy_error = self.last_report.get("window_copy_error")
                if copy_error is not None:
                    if self.copy_observation_attempts == 0:
                        self.first_copy_error = copy_error
                    self.copy_observation_attempts += 1
                    self.last_copy_error = copy_error
                    if copy_error == -25204:
                        self.copy_pending_observations += 1
                if self.last_report.get("ready") and self.first_ready_ms is None:
                    self.first_ready_ms = self.last_observation_elapsed_ms

    def observation_summary(self):
        """Expose bounded counts/first-last metadata without an unbounded polling history."""
        return {"attempts": self.observation_attempts, "first_ax_error": self.first_ax_error,
                "last_ax_error": self.last_ax_error, "first_ready_ms": self.first_ready_ms,
                "last_observation_elapsed_ms": self.last_observation_elapsed_ms,
                "window_copy_attempts": self.copy_observation_attempts,
                "first_window_copy_error": self.first_copy_error, "last_window_copy_error": self.last_copy_error,
                "count_pending_observations": self.count_pending_observations,
                "copy_pending_observations": self.copy_pending_observations,
                "validated_observations": self.validated_observations,
                "clock_scope": "parent-driver-attach-to-observation-return-including-client-overhead"}

    def edit(self):
        """Retain this transaction's validated guard report without retrying input.

        Later observation and cleanup commands replace ``last_report``. Keep the
        edit boundary separately, including a rejected preflight with zero posts.
        An unvalidated client return must not reuse the preceding ready report.
        """
        previous_report = self.last_report
        self.last_edit_report = None
        try:
            self.command("edit", 0)
        finally:
            if self.last_report is not previous_report:
                self.last_edit_report = self.last_report

    def save(self):
        """Only the exact process receives Command-S; source focus is rechecked."""
        data = self.command("save", 1)
        return {"method": "CGEvent.postToPid", "status": "attempted-posts-no-delivery-acknowledgement",
                "target_pid": self.pid, "attempted_events": data.get("dispatched_events"),
                "execution_acknowledged": False, "guard_report": data}

    def close(self):
        """Press the exact owned window's AX close button, without keyboard/global focus."""
        self.command("close", 1)

    def failure_observation(self):
        """Retain the last validated content-free report without extra modifying actions."""
        return self.last_report


def mac_report(data):
    """Whitelist Mac failure metadata and guard enums, excluding arbitrary AX values."""
    stages = {"observe", "ax-trust", "app-ownership", "window-count", "window-read", "window-title",
              "tree-ownership", "tree-bound", "source-candidates", "source-length", "ready", "post-access",
              "source-focus", "initial-selection", "navigate", "navigate-ack", "selection-ack", "edit-dispatch",
              "edit-ack", "save-selection", "save-dispatch", "close-window", "close-button", "complete"}
    if data.get("status") not in ("observed", "blocked", "failed") or data.get("guard_stage") not in stages:
        raise ValueError("unknown Mac report classification")
    boolean = ("trusted", "post_event_access", "ready", "complete", "focused", "tree_bounded", "modified",
               "target_app_active", "frontmost_is_target", "window_main", "window_focused")
    numeric = ("requested_pid", "source_candidates", "source_units", "selection_start", "selection_length",
               "dispatched_events", "window_count", "ax_error", "window_copy_error", "window_copy_count")
    result = {"status": data["status"], "guard_stage": data["guard_stage"]}
    for name in boolean + numeric:
        value = data.get(name)
        wanted = bool if name in boolean else int
        if value is not None and type(value) is not wanted:
            raise ValueError("invalid Mac metadata type")
        result[name] = value
    return result


def wait(child, condition, seconds):
    """Bound endpoint polling and retain failed outcomes instead of fabricated timings."""
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if child.poll() is not None:
            raise RuntimeError("editor exited before endpoint")
        observed = condition()
        if observed:
            return observed
        time.sleep(POLL_SECONDS)
    raise TimeoutError("endpoint observation timed out")


def save_exact(child, driver, working, size, expected_sha256, sample_report=None):
    """Observe Save without opening its target until native chrome acknowledges clean.

    Ordinary Python readers on Windows deny DELETE sharing and can obstruct
    atomic replacement. This read-only UI acknowledgement is necessary but not
    sufficient: one subsequent full-file digest must match the independent oracle.
    """
    if driver.observe(1).get("modified") is not True:
        raise ValueError("Save requires observed dirty source")
    started = time.perf_counter_ns()
    if sample_report is not None:
        sample_report["save_command_attempted"] = True
    command_report = driver.save()
    if sample_report is not None:
        # Persist the transaction report before polling overwrites last_report.
        # Posting/queueing has no product Save acknowledgement: only subsequent
        # clean chrome, exact bytes and normal trace/reopen establish acceptance.
        sample_report["save_command_report"] = command_report
        sample_report["save_command_return_elapsed_ms"] = (time.perf_counter_ns() - started) / 1e6
    wait(child, lambda: driver.observe(1).get("modified") is False, 60)
    if working.stat().st_size != size or digest(working) != expected_sha256:
        raise ValueError("native clean acknowledgement failed exact Save bytes")


def native_menu_inventory(records, *, terminated, normal_exit):
    """Count independent menu checkpoints without joining them to Save requests.

    Retained positives survive an owned kill, but missing rows never prove that
    native menu callbacks did not execute. No order or entry/return pairing is
    inferred from session-parent records.
    """
    counts = {operation: {status: 0 for status in acceptance.STATUSES}
              for operation in sorted(acceptance.MENU_OPERATIONS)}
    for row in records:
        if row["operation"] in counts:
            counts[row["operation"]][row["status"]] += 1
    observed = any(sum(outcomes.values()) for outcomes in counts.values())
    return {"status": "observed" if observed else "unobserved", "counts": counts,
            "boundary": "normal-exit-observed" if normal_exit else (
                "censored" if terminated else "open"),
            "absence_certified": False, "request_correlation": "none",
            "endpoint": "independent-native-menu-boundary-not-save-delivery"}


def native_input_inventory(records, *, terminated, normal_exit):
    """Count local monitor positives without inventing input-to-Save parentage.

    Candidate means the local filter matched, not physical input or delivery to
    a menu or request callback. Missing rows, even on normal exit, prove nothing
    about execution; bounded transport has no lossless receipt certificate.
    """
    counts = {operation: {status: 0 for status in acceptance.STATUSES}
              for operation in sorted(acceptance.INPUT_OPERATIONS)}
    for row in records:
        if row["operation"] in counts:
            counts[row["operation"]][row["status"]] += 1
    observed = any(sum(outcomes.values()) for outcomes in counts.values())
    return {"status": "observed" if observed else "unobserved", "counts": counts,
            "boundary": "normal-exit-observed" if normal_exit else (
                "censored" if terminated else "open"),
            "absence_certified": False, "request_correlation": "none",
            "candidate_to_menu_edge": "unknown", "menu_to_request_edge": "unknown",
            "endpoint": "local-monitor-filter-candidate-not-physical-input-or-save-delivery"}


def save_chain_evidence(home, *, terminated, normal_exit=False):
    """Retain typed native Save evidence even after failed dispatch or owned kill.

    Absence of a retained receipt is unobserved, never proof of non-delivery.
    The byte oracle and generic edit/draw audit remain separate contracts.
    """
    files = sorted((home / "traces").glob("*.jsonl"))
    summary = {"contract": "native-save-causal-v1", "status": "unobserved",
               "terminated": terminated, "normal_exit": normal_exit,
               "absence_certified": False, "requests": [], "trace_sha256": [],
               "native_menu_inventory": native_menu_inventory(
                   [], terminated=terminated, normal_exit=normal_exit),
               "native_input_inventory": native_input_inventory(
                   [], terminated=terminated, normal_exit=normal_exit),
               "discarded_partial_files": 0, "endpoint": "target-callback-to-local-ui-not-physical-input-or-pixels"}
    if not files:
        return summary
    try:
        if len(files) > 8 or any(not re.fullmatch(r"mote-trace-[a-f0-9]{32}-[0-9]{6}\.jsonl", path.name) for path in files):
            raise ValueError("trace inventory differs")
        records, hashes = acceptance.load_records([str(path) for path in files], discard_partial=terminated)
        summary["native_menu_inventory"] = native_menu_inventory(
            records, terminated=terminated, normal_exit=normal_exit)
        summary["native_input_inventory"] = native_input_inventory(
            records, terminated=terminated, normal_exit=normal_exit)
        partial = 0
        for path in files:
            prefix = causal_save.read_prefix(path.read_bytes(), terminated=terminated)
            partial += int(prefix.discarded_partial)
        summary.update(causal_save.classify_requests(records, causal_save.MOTE_SAVE_CONTRACT, terminated=terminated))
        summary["trace_sha256"] = hashes
        summary["discarded_partial_files"] = partial
        requests = summary["requests"]
        complete = (len(requests) == 1 and requests[0]["command_operation"] == "command.save"
                    and requests[0]["successful_chain_complete"]
                    and requests[0]["coverage"] == "instrumented_chain_only" and normal_exit)
        summary["status"] = "complete" if complete else ("censored" if terminated and not normal_exit else "incomplete")
        if not requests:
            summary["status"] = "unobserved"
    except (ValueError, OSError) as error:
        summary["status"] = "invalid"
        summary["error_class"] = type(error).__name__
    return summary


def trace_evidence(home, editing):
    """Reuse the reviewed schema/causal auditor with exact action counts and versions."""
    files = sorted((home / "traces").glob("*.jsonl"))
    if not 1 <= len(files) <= 8 or any(not re.fullmatch(r"mote-trace-[a-f0-9]{32}-[0-9]{6}\.jsonl", path.name) for path in files):
        raise ValueError("trace inventory differs")
    records, hashes = acceptance.load_records([str(path) for path in files])
    expected = {"mote.session": 1, "mote.startup_to_editable": 1, "document.open": 1, "document.open_to_editable": 1,
                "document.open_to_draw_submission": 1}
    if editing:
        expected.update({"document.edit": 1, "edit.committed": 1, "document.edit_to_presentation": 1, "document.save": 1,
                         "save.completed": 1, "document.edit_to_draw_submission": 1})
    elif any(row["operation"] in ("document.edit", "edit.committed", "document.save") for row in records):
        raise ValueError("reopen mutated source")
    audit = acceptance.audit(records, expected)
    audit["trace_sha256"] = hashes
    endpoints = {}
    for name in ("mote.startup_to_editable", "document.open", "document.open_to_editable", "document.open_to_draw_submission",
                 "document.edit", "edit.committed", "document.edit_to_presentation", "document.edit_to_draw_submission", "document.save", "save.completed"):
        rows = [row for row in records if row["operation"] == name]
        endpoints[name] = [{"status": row["status"], "duration_us": row["duration_us"],
                            "version": row["attributes"].get("version")} for row in rows]
    audit["child_monotonic_endpoints"] = endpoints
    audit["endpoint_issues"] = []
    required_versions = {"mote.startup_to_editable": 0, "document.open_to_editable": 0,
                         "document.open_to_draw_submission": 0, "document.edit": 0,
                         "edit.committed": 1, "document.edit_to_presentation": 1, "document.edit_to_draw_submission": 1}
    optional_versions = {"document.open": 0, "document.save": 1, "save.completed": 1}
    # Engine I/O spans currently omit revisions. Preserve that fact instead of
    # fabricating certainty, but reject conflicting revisions if present. The
    # accepted native v0/v1 status and exact Save byte oracle are separate facts.
    audit["io_revision_contract"] = "open-engine-record-optional-version;save-captured-version-required-by-separate-causal-contract"
    for name in expected:
        if name == "mote.session":
            continue
        rows = endpoints[name]
        if len(rows) != 1 or rows[0]["status"] != "success":
            audit["endpoint_issues"].append(name)
            continue
        version = rows[0]["version"]
        if (name in required_versions and version != required_versions[name] or
                name in optional_versions and version is not None and version != optional_versions[name]):
            audit["endpoint_issues"].append(name)
    audit["endpoint_integrity"] = "pass" if not audit["endpoint_issues"] and audit["causal_integrity"] == "pass" else "incomplete"
    return audit


def sample(executable, case, directory, client, mac_save_witness=False):
    """Run one fresh ordinary process, exact local edit/Save and fresh GUI reopen."""
    working = acceptance.artifact_path(directory / "working.json", ".temp")
    shutil.copyfile(case["fixture"], working)
    home = acceptance.artifact_path(directory / "home", ".temp")
    env = environment(home)
    result = {key: value for key, value in case.items() if key != "fixture"}
    result.update({"status": "failed", "phase": "runtime-control", "cache_state": "just-written-not-cache-evicted",
                   "trace_enabled": True, "route": "ordinary-product-no-launch-flags", "edit_attempts": 0,
                   "normal_exit": False, "reopen_normal_exit": False, "poll_interval_ms": 50,
                   "editor_exit_code": None, "reopen_exit_code": None})
    child = None
    original_child = None
    reopen_child = None
    original_forced_cleanup = False
    collector = SaveDiagnosticCollector() if mac_save_witness else None
    result["save_witness_mode"] = "diagnostic-on-not-performance-sample" if collector else "disabled"
    driver = None
    driver_type = WindowsDriver if sys.platform == "win32" else MacDriver
    try:
        started = time.perf_counter_ns()
        bounded_command([str(executable), "--check-runtime"], 15, env)
        result["parent_runtime_control_exit_ms"] = (time.perf_counter_ns() - started) / 1e6
        result["phase"] = "launch-to-source-bound"
        started = time.perf_counter_ns()
        if collector is not None:
            env[ENVIRONMENT_KEY] = "1"
        child = subprocess.Popen([str(executable), str(working)], cwd=ROOT, env=env,
                                 stdout=subprocess.DEVNULL,
                                 stderr=subprocess.PIPE if collector else subprocess.DEVNULL,
                                 bufsize=0 if collector else -1)
        original_child = child
        if collector is not None:
            collector.attach(child.stderr)
        driver = driver_type(child.pid, case["size_bytes"], client)
        def ready(version):
            observed = driver.observe(version)
            return observed if observed.get("ready") else None
        result["source_observation"] = wait(child, lambda: ready(0), 60)
        if isinstance(driver, MacDriver):
            result["source_readiness_observation_summary"] = driver.observation_summary()
        result["parent_launch_to_source_bound_ms"] = (time.perf_counter_ns() - started) / 1e6
        result["phase"] = "initial-whole-document-semantics"
        wait(child, lambda: driver.observe(0).get("complete"), 60)
        result["initial_complete_zero_diagnostics_version"] = 0
        if digest(working) != case["input_sha256"]:
            raise ValueError("open mutated source")
        result["phase"] = "native-local-edit"
        started = time.perf_counter_ns()
        # This legacy count witnesses transaction entry, not keyboard posts or
        # delivery. The Mac transaction report retains its separate post count.
        result["edit_attempts"] = 1
        try:
            driver.edit()
        finally:
            if isinstance(driver, MacDriver):
                result["mac_edit_transaction_report"] = getattr(driver, "last_edit_report", None)
        result["edited_source_observation"] = wait(child, lambda: ready(1), 15)
        result["parent_edit_dispatch_to_source_ack_ms"] = (time.perf_counter_ns() - started) / 1e6
        result["phase"] = "edited-whole-document-semantics"
        wait(child, lambda: driver.observe(1).get("complete"), 60)
        result["edited_complete_zero_diagnostics_version"] = 1
        if digest(working) != case["input_sha256"]:
            raise ValueError("edit wrote disk before Save")
        result["disk_unchanged_before_save"] = True
        result["phase"] = "save-exact-bytes"
        save_exact(child, driver, working, case["size_bytes"], case["expected_saved_sha256"], result)
        def saved():
            try:
                return working.stat().st_size == case["size_bytes"] and digest(working) == case["expected_saved_sha256"]
            except OSError:
                return False
        result["saved_sha256"] = digest(working)
        result["phase"] = "normal-close"
        driver.close()
        if child.wait(timeout=15) != 0:
            raise RuntimeError("editor normal exit failed")
        result["normal_exit"] = True
        if collector is not None:
            result["mac_save_witness"] = collector.finish(original_normal_exit=True)
            collector = None
        result["phase"] = "trace-audit"
        result["save_causal_evidence"] = save_chain_evidence(home, terminated=True, normal_exit=True)
        result["trace_evidence"] = trace_evidence(home, True)
        if result["trace_evidence"]["endpoint_integrity"] != "pass":
            raise ValueError("trace endpoint integrity incomplete")
        if result["save_causal_evidence"]["status"] != "complete":
            raise ValueError("native Save causal chain incomplete")
        child = None
        result["phase"] = "gui-reopen"
        reopen_home = acceptance.artifact_path(directory / "reopen-home", ".temp")
        child = subprocess.Popen([str(executable), str(working)], cwd=ROOT, env=environment(reopen_home),
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        reopen_child = child
        driver = driver_type(child.pid, case["size_bytes"], client)
        # Fresh reopened source is the edited byte spelling, but native version is zero.
        wait(child, lambda: ready(1), 60)
        wait(child, lambda: driver.observe(0).get("complete"), 60)
        if not saved():
            raise ValueError("GUI reopen changed exact source bytes")
        driver.close()
        if child.wait(timeout=15) != 0:
            raise RuntimeError("reopen normal exit failed")
        result["reopen_normal_exit"] = True
        result["reopen_trace_evidence"] = trace_evidence(reopen_home, False)
        if result["reopen_trace_evidence"]["endpoint_integrity"] != "pass":
            raise ValueError("reopen trace endpoint integrity incomplete")
        result["input_sha256_after"] = digest(case["fixture"])
        if result["input_sha256_after"] != case["input_sha256"]:
            raise ValueError("immutable original changed")
        result["status"], result["phase"] = "pass", "complete"
    except Exception as error:
        result["status"] = "blocked" if isinstance(error, PermissionError) else "failed"
        result["error_class"] = type(error).__name__
        if driver is not None:
            if isinstance(driver, MacDriver):
                result["failed_readiness_observation_summary"] = driver.observation_summary()
            try:
                result["failure_observation"] = driver.failure_observation()
            except Exception as failure:
                result["failure_observation_error_class"] = type(failure).__name__
        if child is not None and child.poll() is None and driver is not None:
            # One owned normal-close request may drain the existing trace. A
            # dirty/error modal may prevent it; never dismiss an unknown dialog,
            # retry Save, or label this cleanup as successful workload acceptance.
            try:
                result["failure_close_attempted"] = True
                driver.close()
                result["failure_cleanup_normal_exit"] = child.wait(timeout=5) == 0
            except Exception as failure:
                result["failure_close_error_class"] = type(failure).__name__
    finally:
        if child is not None and child.poll() is None:
            if child is original_child:
                original_forced_cleanup = True
            child.kill()
            child.wait(timeout=10)
            result["forced_cleanup"] = True
        # Retain actual child outcomes independently of workload/cleanup claims.
        # poll() never waits and returns None for an unobserved process exit.
        result["editor_exit_code"] = original_child.poll() if original_child is not None else None
        result["reopen_exit_code"] = reopen_child.poll() if reopen_child is not None else None
        if collector is not None:
            original_normal_exit = (original_child is not None and
                                    not original_forced_cleanup and original_child.poll() == 0)
            result["mac_save_witness"] = collector.finish(original_normal_exit=original_normal_exit)
        # The original process owns this home, even after child becomes reopen.
        original_terminated = original_child is not None and original_child.poll() is not None
        result["save_causal_evidence"] = save_chain_evidence(
            home, terminated=original_terminated,
            normal_exit=bool(original_terminated and not original_forced_cleanup and original_child.poll() == 0))
        # Always retain the immutable-fixture witness even if trust/focus blocks
        # the probe. A failed Save may legitimately leave working bytes changed;
        # record its opaque digest without pretending the expected oracle passed.
        result["input_sha256_after"] = digest(case["fixture"])
        result["working_sha256_after"] = digest(working)
    return result


def main():
    """Execute the explicit 1/100 MiB pilot and retain all samples without pooling."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", required=True)
    parser.add_argument("--rid", choices=("win-x64", "win-arm64", "osx-x64", "osx-arm64"), required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--sizes", type=int, choices=(1, 100), nargs="+", default=[1, 100])
    parser.add_argument("--mac-save-witness", action="store_true",
                        help="Opt-in original Mac child Save witness; diagnostic-on, not a performance sample")
    args = parser.parse_args()
    if args.mac_save_witness and (sys.platform != "darwin" or not args.rid.startswith("osx-")):
        parser.error("--mac-save-witness requires a native Mac pilot")
    output = acceptance.artifact_path(args.output, ".cache")
    if output.exists():
        raise ValueError("refuse report overwrite")
    directory = acceptance.artifact_path(f".temp/native-json-large/{uuid.uuid4().hex}", ".temp")
    directory.mkdir(parents=True)
    report = {"schema_version": 1, "status": "failed", "rid": args.rid,
              "scope": "trace-on-fresh-process-capability-pilot-not-tail-SLA-or-physical-paint",
              "clock_contract": "parent-and-child-monotonic-durations-never-subtracted",
              "os": platform.system(), "host_release": platform.release(), "python": platform.python_version(),
              "runner_image": os.environ.get("ImageVersion"), "source_commit": os.environ.get("GITHUB_SHA"),
              "scratch_id": directory.name, "samples": [],
              "save_witness_mode": "diagnostic-on-not-performance-sample" if args.mac_save_witness else "disabled"}
    try:
        executable, identity = inventory(args.executable, args.rid)
        report.update(identity)
        report["driver_sha256"] = digest(Path(__file__))
        if args.mac_save_witness:
            report["save_witness_collector_sha256"] = digest(Path(__file__).with_name("save_diagnostic.py"))
        report["save_causal_reader_sha256"] = digest(ROOT / "tests/causal_save_trace_reader.py")
        report["artifact_auditor_sha256"] = digest(ROOT / "benchmarks/NativeAcceptance/acceptance.py")
        client = None
        if sys.platform == "darwin":
            source = Path(__file__).with_name("MacClient.swift")
            report["mac_client_source_sha256"] = digest(source)
            client = directory / "mac-client"
            bounded_command(["xcrun", "swiftc", str(source), "-o", str(client)], 120)
            report["mac_client_compiled"] = True
        cases = prepare(directory, args.sizes)
        for case in cases:
            sample_directory = acceptance.artifact_path(directory / str(case["size_mib"]), ".temp")
            sample_directory.mkdir()
            report["samples"].append(sample(executable, case, sample_directory, client, args.mac_save_witness))
        report["status"] = "pass" if all(row["status"] == "pass" for row in report["samples"]) else "incomplete"
        report["binary_sha256_after"] = digest(executable)
        if report["binary_sha256_after"] != report["binary_sha256"]:
            report["status"] = "failed"
    except Exception as error:
        report["error_class"] = type(error).__name__
    acceptance.write_json(output, report)
    print(json.dumps({"status": report["status"], "rid": args.rid,
                      "samples": len(report["samples"])}, separators=(",", ":")))
    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
