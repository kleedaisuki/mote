"""Reconstruct explicit adapter attempts, never infer an external UIA request.

Missing records are incomplete coverage, not absence certificates. A positive
adapter result is independent of thread/focus query health and of client timing.
All schema/privacy validation is shared with the strict native acceptance reader.
"""

from __future__ import annotations

import importlib.util
from pathlib import Path


_SPEC = importlib.util.spec_from_file_location(
    "mote_focus_strict_acceptance",
    Path(__file__).resolve().parents[1] / "benchmarks/NativeAcceptance/acceptance.py",
)
_STRICT = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(_STRICT)


class FocusTraceIntegrityError(ValueError):
    """A retained complete record or explicit edge contradicts the contract."""


def _key(row):
    """Scope span identity to its original session and trace."""
    return row["session_id"], row["trace_id"], row["span_id"]


def _parent(row):
    """Return an explicit edge only; no temporal or ambient reconstruction."""
    parent = row["parent_span_id"]
    return (*_key(row)[:2], parent) if parent is not None else None


def _check_cycles(by_id):
    """Bound iterative graph traversal; a missing parent is not a cycle."""
    done = set()
    for start in by_id:
        trail = set()
        current = start
        while current in by_id and current not in done:
            if current in trail:
                raise FocusTraceIntegrityError("cyclic trace lineage")
            trail.add(current)
            current = _parent(by_id[current])
        done.update(trail)


def classify_focus(records, *, terminated=False):
    """Return independent closed observations from fully validated v1 records.

    ``terminated`` records that the owned process is no longer live; it does not
    invent a normal exit. An actual successful root terminal is the sole normal
    session witness. Complete pairs remain positive under drops or missing roots,
    but their coverage is degraded. Orphan terminals, duplicates, invalid parents
    and conflicting repeated dimensions are integrity errors, not missing data.
    No input, provider-entry, physical transfer, or client-to-server certificate
    is constructed. Example: ``classify_focus(rows, terminated=True)``.

    At most 100,000 records are retained. The first excess item is fetched only
    to detect overflow, then rejected before validation/storage; an oversized or
    unbounded iterable is never exhausted. Direct decoded objects do not have a
    serialized byte/line limit; file bounds belong to ``read_focus_paths``.
    """
    rows = []
    by_id = {}
    for row in records:
        if len(rows) >= 100000:
            raise FocusTraceIntegrityError("trace exceeds bounded record count")
        _STRICT.validate_record(row)
        key = _key(row)
        if key in by_id:
            raise FocusTraceIntegrityError("duplicate trace span")
        by_id[key] = row
        rows.append(row)
    _check_cycles(by_id)
    dropped_scopes = {_key(row)[:2] for row in rows
                      if row["operation"] == "telemetry.dropped"}
    roots = {}
    for key, row in by_id.items():
        if row["operation"] != "mote.session":
            continue
        if key[:2] in roots:
            raise FocusTraceIntegrityError("repeated session terminal")
        roots[key[:2]] = key
    receipts = {key: row for key, row in by_id.items()
                if row["operation"] == _STRICT.FOCUS_RECEIPT_OPERATION}
    terminals = {}
    for row in rows:
        if row["operation"] != _STRICT.FOCUS_TERMINAL_OPERATION:
            continue
        parent = _parent(row)
        if parent not in receipts:
            raise FocusTraceIntegrityError("orphan focus adapter terminal")
        if parent in terminals:
            raise FocusTraceIntegrityError("repeated focus adapter terminal")
        if any(row["attributes"][name] != receipts[parent]["attributes"][name]
               for name in _STRICT.FOCUS_RECEIPT_ATTRIBUTES):
            raise FocusTraceIntegrityError("conflicting focus adapter dimensions")
        terminals[parent] = row

    observations = []
    for key, receipt in receipts.items():
        parent = _parent(receipt)
        root = by_id.get(parent)
        if root is not None and (root["operation"] != "mote.session"
                                 or root["parent_span_id"] is not None):
            raise FocusTraceIntegrityError("focus receipt parent is not a session root")
        normal = bool(root and root["status"] == "success")
        drops = key[:2] in dropped_scopes
        terminal = terminals.get(key)
        missing = []
        if root is None:
            missing.append("session_root")
        elif not normal:
            missing.append("normal_session_terminal")
        if terminal is None:
            missing.append("adapter_terminal")
        coverage = "incomplete" if missing else ("degraded" if drops else "explicit_pair_only")
        observations.append({
            "session_id": key[0], "trace_id": key[1], "receipt_span_id": key[2],
            "session_root_span_id": parent[2],
            "terminal_span_id": terminal["span_id"] if terminal else None,
            "receipt_observed": True, "terminal_observed": terminal is not None,
            "classification": terminal["status"] if terminal else (
                "censored" if terminated else "incomplete"),
            "coverage": coverage, "missing_evidence": missing,
            "boundary": "normal_exit_observed" if normal else (
                "censored" if terminated else "open"),
            "normal_session_terminal_observed": normal,
            "dropped_records_observed": drops,
            "facts": dict(terminal["attributes"] if terminal else receipt["attributes"]),
        })
    return {
        "status": "observed" if observations else "unobserved",
        "observations": observations,
        "absence_certified": False, "client_correlation": "none",
        "cross_process_edge": "unjoined", "provider_entry_coverage": "adapter_attempt_only",
        "transport_health": "not_certified",
    }


def read_focus_paths(paths, *, terminated=False):
    """Strictly read bounded repo artifacts, then reconstruct only explicit edges.

    An unterminated final row is discarded only after the owned process has
    terminated; complete malformed rows always fail. File hashes/paths are not
    returned in the graph report, avoiding accidental client attribution.

    The shared loader permits at most 32 MiB per file, 16,384 decoded characters
    per retained line and 100,000 retained records across all paths. It does not
    impose a path-count or aggregate file-byte limit; callers must supply a finite
    artifact inventory. These are the existing loader bounds, not a new policy.
    """
    records, _ = _STRICT.load_records(paths, discard_partial=terminated)
    return classify_focus(records, terminated=terminated)
