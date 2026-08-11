#!/usr/bin/env python3
"""
tests/perf/aggregate-and-check-fps.py

S6-DO-01 (docs/10. Sprint 6, devops-engineer row; DoD: "รันอัตโนมัติ + เก็บ trend เฟรมเรต" - run
automatically + track frame-rate trend) - aggregates N repeated web/e2e/gantt-perf.spec.ts
(S6-QA-01) runs into one robust statistic and checks it against both a hard floor and a rolling
trend baseline. This is the frame-rate sibling of tests/perf/aggregate-and-check.py (S4-DO-01's
k6/WBS-tree-latency aggregator); `.github/workflows/perf-nightly.yml`'s `perf-nightly-frontend`
job is the caller, the same way `perf-nightly` calls the latency script.

WHY A SIBLING SCRIPT INSTEAD OF EXTENDING aggregate-and-check.py
(S6-DO-01 devops call - recorded here per that task's instruction to justify it and to never
silently invert a threshold):

  1. Direction inversion risk. Latency is "lower is better" (a regression is the value going UP);
     frame rate is "higher is better" (a regression is the value going DOWN). Both the
     hard-threshold comparison AND the rolling-trend-multiplier comparison flip sign/operator (see
     `hard_breach`/`trend_regression` below). One shared function toggled by a `direction` flag is
     possible, but a single copy-paste or off-by-operator mistake in that shared function would
     then corrupt BOTH metrics' gates simultaneously - for a script whose entire job is "decide
     whether the nightly build is red", that shared blast radius is a real cost. Two independent,
     individually-readable scripts make each gate's comparison direction statically obvious by
     reading that file top to bottom, not derived from a flag threaded through from the workflow
     YAML into a shared branch.
  2. Different input shapes, so there is little to actually share on the parsing side.
     wbs-tree.k6.js writes one structured JSON file per run (p95Ms/p99Ms/avgMs/...) via k6's own
     handleSummary(). gantt-perf.spec.ts (S6-QA-01, already shipped this sprint - not this task's
     artifact to change) only `console.log`s one machine-parseable `[S6-QA-01] key=value ...` line
     per run to stdout; there is no JSON result file to reuse aggregate-and-check.py's
     `load_run_results()` against. This script's own distinctive job is "capture Playwright's
     stdout per run, then find and parse that line" - it has no analogue in the k6 script.
  3. Zero regression risk to the already-relied-upon latency gate. This sprint's Docker Desktop
     outage (WSL backend wedged - see the workflow's own comments and this sprint's devops report)
     meant aggregate-and-check.py could not be re-run end-to-end this session to prove a refactor
     of it changed nothing. Leaving it untouched and adding a new file next to it is the
     conservative choice while that verification gap exists - it was instead verified standalone
     with synthetic fixtures (see this sprint's devops report / test invocation history).

  The genuinely shared shape (env-var plumbing, median-of-N, rolling-baseline-over-last-N-
  schedule-triggered-rows, CSV/JSON/step-summary/GITHUB_OUTPUT writing) is duplicated in miniature
  rather than factored into a shared imported module, for the same reasons #1/#3 above. Each
  frame-rate-specific inversion is called out inline below.

INPUTS (env vars, no positional args - mirrors aggregate-and-check.py's own convention so the
calling workflow step stays a single readable command):
  RESULTS_DIR            directory containing one or more gantt-perf.result.run-*.log files, each
                          the raw captured stdout of one `npm run test:e2e:perf`
                          (playwright test e2e/gantt-perf.spec.ts) invocation. This script greps
                          each file for its own `[S6-QA-01] frames=... avgFps=... ...` line (see
                          `extract_metrics_from_log`) rather than expecting a JSON file, because
                          gantt-perf.spec.ts only ever prints that one line - it has no
                          handleSummary()-equivalent JSON writer.
  TREND_CSV_PATH         path to the historical frame-rate trend CSV. Tracked on the *same*
                          `perf-trend` branch as the latency trend (same orphan branch, same
                          workflow-level trend-persistence mechanism), but its own file -
                          `gantt-fps-trend.csv` - never mixed into the latency script's `trend.csv`
                          (different unit, different direction, different column schema; a shared
                          file would need nullable metric-specific columns and would make the
                          latency script's own `read_history()` column assumptions load-bearing
                          for a metric it was never written to know about).
  HARD_THRESHOLD_FPS      the DoD's own hard floor (default 30 - matches gantt-perf.spec.ts's own
                          FRAME_RATE_THRESHOLD_FPS default). Median-of-N avgFps at or above this is
                          fine ("ไม่ต่ำกว่าเกณฑ์" - not below the threshold, i.e. the boundary value
                          itself passes); strictly below it is a HARD_BREACH regardless of trend
                          history. This mirrors gantt-perf.spec.ts's own per-run assertion
                          (`toBeGreaterThanOrEqual`), just evaluated against the median-of-N instead
                          of a single run.
  REGRESSION_MULTIPLIER  today's median avgFps vs. the rolling baseline's median avgFps: today
                          falling below baseline / this multiplier is a TREND_REGRESSION (default
                          1.25). This is the direction-correct mirror of the latency script's
                          "today's median P95 > baseline * multiplier" - not the same operation
                          with the operator flipped, but the *reciprocal* threshold
                          (baseline / multiplier, not baseline * multiplier), because fps and
                          latency move in opposite directions but are roughly reciprocal
                          quantities (fps ~ 1000 / frame_time_ms). baseline * 1.25 (25% slower is a
                          latency regression) and baseline / 1.25 = baseline * 0.8 (20% fewer
                          frames per second is a frame-rate regression) express the same relative
                          magnitude of degradation on each metric's own axis. See `trend_regression`
                          below - this is the one line in this file most worth double-checking on
                          review, precisely because getting a "higher is better" gate backwards
                          (e.g. accidentally writing `median_avg_fps > rolling_baseline_avg_fps *
                          regression_multiplier`) would silently turn an improvement into a
                          reported regression and vice versa.
  MIN_HISTORY_POINTS     same meaning as aggregate-and-check.py (default 3) - minimum number of
                          prior *schedule*-triggered rows required before the trend check runs.
  MAX_HISTORY_WINDOW     same meaning (default 7 - roughly "the last week of nightly runs").
  RUN_DATE_UTC           ISO date (YYYY-MM-DD) to stamp this run's trend row with.
  RUN_TRIGGER            'schedule' or 'workflow_dispatch' - only 'schedule' rows count toward the
                          rolling baseline, same reasoning as aggregate-and-check.py (a manual
                          debugging dispatch with a non-default N/threshold must never silently
                          pollute the trend the nightly cadence establishes).
  COMMIT_SHA, RUN_URL    provenance columns copied verbatim into the trend row.
  OUTPUT_JSON_PATH       where to write this run's full aggregate (for the workflow's artifact
                          upload).
  NEW_TREND_ROW_PATH     where to write the single CSV row this run should append to
                          TREND_CSV_PATH (the workflow does the actual git append/commit/push -
                          this script only computes the row - same division of labour as
                          aggregate-and-check.py).
  GITHUB_STEP_SUMMARY    (optional) markdown table appended here, same convention as
                          aggregate-and-check.py and ci.yml's own summary-writing steps.
  GITHUB_OUTPUT          (optional) `verdict=<PASS|HARD_BREACH|TREND_REGRESSION|
                          HARD_BREACH+TREND_REGRESSION>` and `median_avg_fps=<n>` appended here so
                          the workflow's Gate/alert steps can read
                          `steps.<id>.outputs.verdict` / `.median_avg_fps`.

EXIT CODE: always 0 unless the script itself errors (RESULTS_DIR has zero usable result files,
meaning the Playwright loop never actually produced a parseable measurement - that is an
infra/wiring failure, not a frame-rate verdict, and must be distinguished from one, exactly as
aggregate-and-check.py distinguishes "no k6 output at all" from "k6 ran and was slow"). The
regression/hard-breach verdict is deliberately NOT this process's exit code - see the workflow's
own "Gate" step, which reads the `verdict` output on purpose so later steps (trend append,
artifact upload, issue alert) still run even when today's run is red.
"""
from __future__ import annotations

import csv
import glob
import json
import os
import re
import statistics
from datetime import datetime, timezone

S6_QA_01_MARKER = "[S6-QA-01]"

# Keys this script actually reads out of the `[S6-QA-01] key=value key=value ...` line. Parsing is
# order- and extra-field-tolerant (split on whitespace, not a fixed-position regex) so a future,
# additive change to gantt-perf.spec.ts's own log line (e.g. a new field appended at the end) does
# not require touching this script - only removing/renaming one of the keys below would.
_TOKEN_RE = re.compile(r"^(?P<key>[A-Za-z0-9_]+)=(?P<value>.+)$")


def env(name: str, default: str | None = None) -> str | None:
    val = os.environ.get(name)
    return val if val not in (None, "") else default


def env_float(name: str, default: float) -> float:
    val = os.environ.get(name)
    return float(val) if val not in (None, "") else default


def env_int(name: str, default: int) -> int:
    val = os.environ.get(name)
    return int(val) if val not in (None, "") else default


def parse_s6_qa_01_tokens(line: str) -> dict[str, str]:
    """Parses the tail of a `[S6-QA-01] frames=9532 elapsedMs=29985.3 avgFps=31.78 ...` line into a
    str->str token dict. Tolerates `longTaskCount=unsupported`/`longTaskTotalMs=unsupported`
    (gantt-perf.spec.ts's own literal for "Long Tasks API unsupported in this browser/context",
    never coerced to a false zero - see `_num_or_none`)."""
    after_marker = line.split(S6_QA_01_MARKER, 1)[1]
    tokens: dict[str, str] = {}
    for tok in after_marker.strip().split():
        m = _TOKEN_RE.match(tok)
        if m:
            tokens[m.group("key")] = m.group("value")
    return tokens


def _num_or_none(tokens: dict[str, str], key: str) -> float | None:
    raw = tokens.get(key)
    if raw is None or raw == "unsupported":
        return None
    try:
        return float(raw)
    except ValueError:
        return None


def _int_or_none(tokens: dict[str, str], key: str) -> int | None:
    """Same as `_num_or_none` but for genuinely integer *counts* (framesOverBudget33ms,
    longTaskCount) rather than continuous ms measurements - keeps these displaying/serializing as
    `3`, not `3.0`, in the JSON aggregate/CSV trend row/step summary."""
    raw = tokens.get(key)
    if raw is None or raw == "unsupported":
        return None
    try:
        return int(float(raw))  # int(float(...)), not int(...) directly: tolerates "30.0" too
    except ValueError:
        return None


def extract_metrics_from_log(path: str) -> dict | None:
    """Reads one gantt-perf.result.run-*.log file (raw captured stdout of a single
    `playwright test e2e/gantt-perf.spec.ts` process) and pulls the one real measurement out of it.
    Returns None (never raises) if the file has no usable `[S6-QA-01]` line - e.g. the run crashed
    before the test's own console.log executed (navigation timeout, login failure, etc.) - mirroring
    aggregate-and-check.py's own "no p95Ms -> skip, don't poison the aggregate" handling of a run
    whose k6 process died early."""
    with open(path, encoding="utf-8", errors="replace") as f:
        content = f.read()

    matching_lines = [line for line in content.splitlines() if S6_QA_01_MARKER in line]
    if not matching_lines:
        return None
    if len(matching_lines) > 1:
        print(f"::warning::{path} contains {len(matching_lines)} '{S6_QA_01_MARKER}' lines - using the last one.")

    tokens = parse_s6_qa_01_tokens(matching_lines[-1])
    try:
        avg_fps = float(tokens["avgFps"])
        frame_count = int(tokens["frames"])
    except (KeyError, ValueError) as exc:
        print(f"::warning::{path}: required field(s) avgFps/frames missing or unparseable ({exc}) - excluding this run.")
        return None

    return {
        "avgFps": avg_fps,
        "frameCount": frame_count,
        "elapsedMs": _num_or_none(tokens, "elapsedMs"),
        "maxFrameDeltaMs": _num_or_none(tokens, "maxFrameDeltaMs"),
        "p95FrameDeltaMs": _num_or_none(tokens, "p95FrameDeltaMs"),
        "framesOverBudget33ms": _int_or_none(tokens, "framesOverBudget33ms"),
        "longTaskCount": _int_or_none(tokens, "longTaskCount"),
        "longTaskTotalMs": _num_or_none(tokens, "longTaskTotalMs"),
        "sourceFile": os.path.basename(path),
    }


def load_run_results(results_dir: str) -> list[dict]:
    paths = sorted(glob.glob(os.path.join(results_dir, "gantt-perf.result.run-*.log")))
    if not paths:
        raise SystemExit(
            f"No gantt-perf.result.run-*.log files found in {results_dir!r} - the Playwright loop "
            "produced no captured output at all. This is an infra/wiring failure, not a frame-rate "
            "verdict; failing loudly rather than silently reporting an empty aggregate."
        )
    runs = []
    for p in paths:
        metrics = extract_metrics_from_log(p)
        if metrics is None:
            print(f"::warning::{p} has no parseable '{S6_QA_01_MARKER}' line - excluding it from aggregation.")
            continue
        runs.append(metrics)
    if not runs:
        raise SystemExit(
            f"All {len(paths)} result file(s) in {results_dir!r} were unusable (no '{S6_QA_01_MARKER}' line "
            "in any of them - every Playwright run in this batch failed before reaching the measurement)."
        )
    return runs


def read_history(trend_csv_path: str, trigger_filter: str, window: int) -> list[float]:
    if not trend_csv_path or not os.path.isfile(trend_csv_path):
        return []
    rows = []
    with open(trend_csv_path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row.get("trigger") == trigger_filter:
                try:
                    rows.append((row["date"], float(row["median_avg_fps"])))
                except (KeyError, ValueError):
                    continue
    rows.sort(key=lambda r: r[0])
    return [fps for _date, fps in rows[-window:]]


def main() -> None:
    results_dir = env("RESULTS_DIR", ".")
    hard_threshold_fps = env_float("HARD_THRESHOLD_FPS", 30.0)
    regression_multiplier = env_float("REGRESSION_MULTIPLIER", 1.25)
    min_history_points = env_int("MIN_HISTORY_POINTS", 3)
    max_history_window = env_int("MAX_HISTORY_WINDOW", 7)
    run_date = env("RUN_DATE_UTC", datetime.now(timezone.utc).strftime("%Y-%m-%d"))
    run_trigger = env("RUN_TRIGGER", "workflow_dispatch")
    commit_sha = env("COMMIT_SHA", "unknown")
    run_url = env("RUN_URL", "")
    trend_csv_path = env("TREND_CSV_PATH", "")
    output_json_path = env("OUTPUT_JSON_PATH", "aggregate-fps.json")
    new_row_path = env("NEW_TREND_ROW_PATH", "new-fps-trend-row.csv")

    runs = load_run_results(results_dir)
    n_runs = len(runs)
    avg_fps_values = [r["avgFps"] for r in runs]
    p95_delta_values = [r["p95FrameDeltaMs"] for r in runs if r["p95FrameDeltaMs"] is not None]
    long_task_counts = [r["longTaskCount"] for r in runs if r["longTaskCount"] is not None]

    median_avg_fps = statistics.median(avg_fps_values)
    min_avg_fps = min(avg_fps_values)
    max_avg_fps = max(avg_fps_values)
    mean_avg_fps = statistics.fmean(avg_fps_values)
    median_p95_frame_delta_ms = statistics.median(p95_delta_values) if p95_delta_values else None
    # Corroborating signal only (see module docstring - the DoD/gate is avgFps-only). Reported as
    # the worst (max) observed across the N runs, not a median/mean: a single run with even one
    # long task is a more useful red flag for a human triaging a regression than an averaged-away
    # count would be, and averaging away an outlier here would cost real information for free.
    max_long_task_count = max(long_task_counts) if long_task_counts else None

    # Per-run pass boundary matches gantt-perf.spec.ts's own `expect(avgFps).toBeGreaterThanOrEqual(...)`
    # (>= , not >) - "ไม่ต่ำกว่าเกณฑ์" (not below the threshold) reads as the boundary value itself
    # passing, same convention this script's own hard_breach check below uses on the median.
    pass_count = sum(1 for v in avg_fps_values if v >= hard_threshold_fps)
    fail_count = n_runs - pass_count

    # Only ever compared against prior *schedule*-triggered rows - see module docstring.
    history = read_history(trend_csv_path, trigger_filter="schedule", window=max_history_window)
    rolling_baseline_avg_fps = statistics.fmean(history) if len(history) >= min_history_points else None

    # --- Frame-rate-direction gates (see module docstring's REGRESSION_MULTIPLIER entry) ---------
    # Higher is better: a breach is the median falling BELOW the floor (latency's hard_breach uses
    # `>=`; this uses `<`, the mirror image for a floor instead of a ceiling).
    hard_breach = median_avg_fps < hard_threshold_fps
    # Reciprocal of the latency script's `median_p95 > rolling_baseline_p95 * regression_multiplier`:
    # today's fps must not fall below baseline / multiplier. This is intentionally division, not
    # `rolling_baseline_avg_fps * regression_multiplier` with the comparison flipped to `<` - that
    # would make the "tolerance band" asymmetric in the wrong direction (multiplying a fps baseline
    # by 1.25 would ALLOW a 25%-worse regression to still pass, backwards from the intent). Division
    # is the correct reciprocal so both metrics tolerate the same relative magnitude of degradation.
    trend_regression = (
        rolling_baseline_avg_fps is not None
        and median_avg_fps < rolling_baseline_avg_fps / regression_multiplier
    )

    if hard_breach and trend_regression:
        verdict = "HARD_BREACH+TREND_REGRESSION"
    elif hard_breach:
        verdict = "HARD_BREACH"
    elif trend_regression:
        verdict = "TREND_REGRESSION"
    else:
        verdict = "PASS"

    aggregate = {
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "runDateUtc": run_date,
        "runTrigger": run_trigger,
        "commitSha": commit_sha,
        "runUrl": run_url,
        "nRuns": n_runs,
        "hardThresholdFps": hard_threshold_fps,
        "regressionMultiplier": regression_multiplier,
        "medianAvgFps": median_avg_fps,
        "minAvgFps": min_avg_fps,
        "maxAvgFps": max_avg_fps,
        "meanAvgFps": mean_avg_fps,
        "medianP95FrameDeltaMs": median_p95_frame_delta_ms,
        "maxLongTaskCount": max_long_task_count,
        "passCount": pass_count,
        "failCount": fail_count,
        "rollingBaselineAvgFps": rolling_baseline_avg_fps,
        "rollingBaselineNPoints": len(history),
        "hardBreach": hard_breach,
        "trendRegression": trend_regression,
        "verdict": verdict,
        "perRun": [
            {
                "avgFps": r.get("avgFps"),
                "frameCount": r.get("frameCount"),
                "elapsedMs": r.get("elapsedMs"),
                "maxFrameDeltaMs": r.get("maxFrameDeltaMs"),
                "p95FrameDeltaMs": r.get("p95FrameDeltaMs"),
                "framesOverBudget33ms": r.get("framesOverBudget33ms"),
                "longTaskCount": r.get("longTaskCount"),
                "longTaskTotalMs": r.get("longTaskTotalMs"),
                "sourceFile": r.get("sourceFile"),
            }
            for r in runs
        ],
    }

    with open(output_json_path, "w", encoding="utf-8") as f:
        json.dump(aggregate, f, indent=2)

    # The trend row this run contributes - the workflow appends it to TREND_CSV_PATH itself (after
    # this script runs, so today's own row is never part of today's baseline computation above) and
    # commits/pushes it on the shared `perf-trend` branch, into `gantt-fps-trend.csv` specifically.
    with open(new_row_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow([
            run_date, run_trigger, commit_sha, n_runs,
            f"{median_avg_fps:.2f}", f"{min_avg_fps:.2f}", f"{max_avg_fps:.2f}", f"{mean_avg_fps:.2f}",
            f"{median_p95_frame_delta_ms:.2f}" if median_p95_frame_delta_ms is not None else "",
            max_long_task_count if max_long_task_count is not None else "",
            pass_count, fail_count,
            f"{rolling_baseline_avg_fps:.2f}" if rolling_baseline_avg_fps is not None else "",
            hard_breach, trend_regression, verdict, run_url,
        ])

    baseline_str = (
        f"{rolling_baseline_avg_fps:.2f} fps (n={len(history)})"
        if rolling_baseline_avg_fps is not None
        else "n/a (insufficient history)"
    )
    summary_lines = [
        "### Nightly Gantt frame-rate perf (S6-DO-01)",
        "",
        f"**Verdict: {verdict}**",
        "",
        "| Metric | Value |",
        "| --- | --- |",
        f"| Runs aggregated (N) | {n_runs} |",
        f"| Median avgFps | {median_avg_fps:.2f} fps |",
        f"| Min / Max avgFps | {min_avg_fps:.2f} / {max_avg_fps:.2f} fps |",
        f"| Mean avgFps | {mean_avg_fps:.2f} fps |",
        (
            f"| Median p95 frame delta (corroborating) | {median_p95_frame_delta_ms:.2f} ms |"
            if median_p95_frame_delta_ms is not None
            else "| Median p95 frame delta (corroborating) | n/a |"
        ),
        (
            f"| Worst longTaskCount observed (corroborating) | {max_long_task_count} |"
            if max_long_task_count is not None
            else "| Worst longTaskCount observed (corroborating) | n/a (unsupported in this browser) |"
        ),
        f"| Individual-run pass rate | {pass_count}/{n_runs} at or above {hard_threshold_fps:.0f} fps |",
        f"| Rolling baseline (last {max_history_window} scheduled nights) | {baseline_str} |",
        f"| Hard floor ({hard_threshold_fps:.0f} fps) | {'BREACHED' if hard_breach else 'met'} |",
        f"| Trend regression (< baseline / {regression_multiplier}) | {'YES' if trend_regression else 'no'} |",
        "",
    ]
    print("\n".join(summary_lines))

    step_summary_path = env("GITHUB_STEP_SUMMARY")
    if step_summary_path:
        with open(step_summary_path, "a", encoding="utf-8") as f:
            f.write("\n".join(summary_lines) + "\n")

    github_output_path = env("GITHUB_OUTPUT")
    if github_output_path:
        with open(github_output_path, "a", encoding="utf-8") as f:
            f.write(f"verdict={verdict}\n")
            f.write(f"median_avg_fps={median_avg_fps:.2f}\n")


if __name__ == "__main__":
    main()
