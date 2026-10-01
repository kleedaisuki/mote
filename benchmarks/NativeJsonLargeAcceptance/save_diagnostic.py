"""Bounded, content-free collector for one owned Mac Save diagnostic pipe.

Only whitelisted protocol stages survive framing. Unknown stderr is drained but
never retained. Receipt durations use the parent clock and are not execution times.
"""

import threading
import time

ENVIRONMENT_KEY = "MOTE_NATIVE_MAC_SAVE_TRACE"
PREFIX = b"mote-save-diag-v1:"
STAGES = ("ready", "selector_entered", "controller_admitted", "overflow", "completed")
FRAMES = {PREFIX + stage.encode("ascii"): stage for stage in STAGES}
FRAME_BYTES = 64
READ_BYTES = 1024
RECORD_LIMIT = 16
COUNT_LIMIT = 65535
JOIN_SECONDS = 2.0


class SaveDiagnosticCollector:
    """Preinitialize bounded state before launch, then attach the owned stderr.

    The daemon continuously drains even after record/counter saturation. A timed
    out join does not close a stream underneath its blocked reader; process exit
    normally supplies EOF. A snapshot is detached and cannot mutate afterwards.
    """

    def __init__(self):
        """Create state without starting a thread or referencing a child pipe."""
        self._lock = threading.Lock()
        self._started = time.perf_counter_ns()
        self._thread = None
        self._records = []
        self._counts = {stage: 0 for stage in STAGES}
        self._rejected = 0
        self._overlong = 0
        self._retention_loss = False
        self._counter_capped = False
        self._partial_tail = False
        self._read_error = False
        self._eof = False
        self._attach_error = False
        self._order_valid = True
        self._terminal_seen = False

    def attach(self, stream):
        """Immediately start draining the original child's binary unbuffered pipe.

        Call once just after Popen. Errors are diagnostic state, never acceptance
        failures; initialization failure must not change the Save transaction.
        """
        if self._thread is not None:
            raise ValueError("collector already attached")
        try:
            self._thread = threading.Thread(target=self._drain, args=(stream,), daemon=True)
            self._thread.start()
        except Exception:
            with self._lock:
                self._attach_error = True

    def _increment(self, value):
        """Saturate metadata counters without hiding that counts were censored."""
        if value == COUNT_LIMIT:
            self._counter_capped = True
            return value
        return value + 1

    def _frame(self, frame):
        """Retain only fixed known stages and bounded parent-local receipt times."""
        stage = FRAMES.get(frame)
        with self._lock:
            if stage is None:
                self._rejected = self._increment(self._rejected)
                return
            if (self._terminal_seen or
                    stage == "ready" and any(self._counts.values()) or
                    stage != "ready" and self._counts["ready"] != 1 or
                    stage == "controller_admitted" and not self._counts["selector_entered"]):
                self._order_valid = False
            self._terminal_seen |= stage == "completed"
            self._counts[stage] = self._increment(self._counts[stage])
            if len(self._records) == RECORD_LIMIT:
                self._retention_loss = True
                return
            self._records.append({"stage": stage,
                                  "parent_receipt_ms": (time.perf_counter_ns() - self._started) / 1e6})

    def _drain(self, stream):
        """Bound framing before allocation and discard overlong lines through LF."""
        frame = bytearray()
        discarding = False
        try:
            while True:
                block = stream.read(READ_BYTES)
                if not block:
                    with self._lock:
                        self._partial_tail = bool(frame) or discarding
                        self._eof = True
                    return
                for byte in block:
                    if byte == 10:
                        if not discarding:
                            self._frame(bytes(frame))
                        frame.clear()
                        discarding = False
                    elif not discarding:
                        if len(frame) == FRAME_BYTES:
                            frame.clear()
                            discarding = True
                            with self._lock:
                                self._overlong = self._increment(self._overlong)
                        else:
                            frame.append(byte)
        except Exception:
            with self._lock:
                self._read_error = True
        finally:
            # EOF/error means no active read; close safely on the reader thread.
            try:
                stream.close()
            except Exception:
                with self._lock:
                    self._read_error = True

    def finish(self, original_normal_exit=False):
        """Join at most two seconds and return bounded, content-free evidence.

        completed is only a producer-closed/drained/loss-accounted watermark,
        never target exit, timely drain or Save success. Full-session completeness
        independently requires normal exit of this original owned child and EOF.
        Unknown exit/forced kill, local loss, malformed output and transport error
        censor completeness even if completed arrived before termination or late
        after a target shutdown timeout. Positive records remain useful.
        """
        thread = self._thread
        if thread is not None and thread.ident is not None:
            thread.join(JOIN_SECONDS)
        timed_out = thread is not None and thread.is_alive()
        with self._lock:
            malformed = bool(self._rejected or self._overlong or self._partial_tail)
            loss = self._retention_loss or self._counter_capped or bool(self._counts["overflow"])
            normal_exit = original_normal_exit is True
            healthy = (normal_exit and self._eof and not timed_out and not self._read_error and
                       not self._attach_error and not malformed and not loss and
                       self._order_valid and self._counts["ready"] == 1 and self._counts["completed"] == 1)
            return {"schema_version": 1, "mode": "diagnostic-on-not-performance-sample",
                    "session": "original-gui-child-only", "protocol": "mote-save-diag-v1",
                    "clock": "parent-local-receipt-duration-not-target-execution-time",
                    "records": [dict(row) for row in self._records],
                    "stage_counts": dict(self._counts), "rejected_frames": self._rejected,
                    "overlong_frames": self._overlong, "partial_tail": self._partial_tail,
                    "malformed": malformed, "retention_loss": self._retention_loss,
                    "counter_capped": self._counter_capped, "loss": loss,
                    "read_error": self._read_error, "attach_error": self._attach_error,
                    "eof": self._eof, "join_timeout": timed_out, "original_normal_exit": normal_exit,
                    "completed_stage_semantics": "producer-closed-drained-loss-accounted-not-exit-timeliness-or-save",
                    "protocol_order_valid": self._order_valid, "healthy_completed_stream": healthy,
                    "absence_interpretation": "not-proof-of-callback-nonexecution",
                    "stream_completion": "healthy-full-session-transport" if healthy else "censored"}
