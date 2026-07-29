#!/usr/bin/env python3
"""
tests/perf/aggregate-and-check.py

S4-DO-01 (docs/10. Sprint 4, devops-engineer row) - aggregates N repeated
tests/perf/wbs-tree.k6.js runs into one robust statistic and checks it against both a hard
target and a rolling trend baseline. Exists because docs/perf/baseline.md (S4-QA-01) measured
P95 ranging 68-175ms across 11 runs on a shared dev workstation under the same 20-VU/30s load
(~18% of runs breached the 100ms gate on tail-latency noise alone, zero request failures) and
explicitly recommended NOT gating a nightly job on a single 30s run:

    "Consider averaging P95 over N repeated k6 runs per nightly job, or widening the sample
    window, rather than gating on one 30s run."

This script is the "averaging" half of that recommendation; `.github/workflows/perf-nightly.yml`
is the caller. It deliberately never crashes/raises on a normal "no history yet" first run - the
very first night a rolling baseline does not exist, and that is expected, not an error (see
docs/perf/baseline.md section 6: "S4-DO-01's nightly job should establish its own runner-specific
baseline the first time it runs").

INPUTS (all via environment variables, no positional args - keeps the calling workflow step a
single readable command):
  RESULTS_DIR           directory containing one or more wbs-tree.result.*.json files, each the
                         literal JSON written by wbs-tree.k6.js's handleSummary() (p95Ms, p99Ms,
                         avgMs, maxMs, requestCount, httpReqFailedRate, thresholdMs, ...).
  TREND_CSV_PATH         path to the historical trend CSV (tracked on the `perf-trend` branch,
                         see the workflow); may not exist yet (first-ever run).
  HARD_THRESHOLD_MS      the DoD's own hard target (default 100) - median-of-N P95 at or above
                         this is a HARD_BREACH regardless of trend history.
  REGRESSION_MULTIPLIER  today's median P95 vs the rolling baseline's median P95: exceeding
                         baseline * this multiplier is a TREND_REGRESSION (default 1.25, i.e.
                         >25% worse than the trailing average) - catches gradual drift even while
                         still under the 100ms hard target.
  MIN_HISTORY_POINTS     minimum number of prior *schedule*-triggered rows required before the
                         trend check is evaluated at all (default 3) - avoids a false regression
                         signal from a tiny/noisy history.
  MAX_HISTORY_WINDOW     how many of the most recent qualifying prior rows feed the rolling
                         baseline (default 7 - roughly "the last week of nightly runs").
  RUN_DATE_UTC           ISO date (YYYY-MM-DD) to stamp this run's trend row with.
  RUN_TRIGGER            'schedule' or 'workflow_dispatch' - only 'schedule' rows count toward
                         the rolling baseline (a manual/ad hoc dispatch run, e.g. with a
                         non-default N or VUS for debugging, must never silently pollute the
                         trend the nightly cadence establishes).
  COMMIT_SHA, RUN_URL    provenance columns copied verbatim into the trend row.
  OUTPUT_JSON_PATH       where to write this run's full aggregate (for the workflow's artifact
                         upload).
  NEW_TREND_ROW_PATH     where to write the single CSV row this run should append to
                         TREND_CSV_PATH (the workflow does the actual git append/commit/push -
                         this script only computes the row).
  GITHUB_STEP_SUMMARY    (optional) if set, a markdown table is appended here, same convention
                         already used by ci.yml's NuGet/npm-audit/Trivy steps.
  GITHUB_OUTPUT          (optional) if set, `verdict=<PASS|HARD_BREACH|TREND_REGRESSION|
                         HARD_BREACH+TREND_REGRESSION>` is appended here so the workflow's final
                         gate step can read it via `steps.<id>.outputs.verdict`.

EXIT CODE: always 0 unless the script itself errors (e.g. RESULTS_DIR has zero result files,
which means the k6 runs never actually produced data - that IS a script/infra failure, not a
perf regression, and must be distinguished from one). The regression/hard-breach verdict is
deliberately NOT expressed as this process's exit code - see the workflow's own "Gate" step,
which reads the `verdict` output on purpose so that later steps (trend append, artifact upload,
issue alert) still run even when today's run is red.
"""
from __future__ import annotations

import csv
import glob
import json
import os
import statistics
import sys
from datetime import datetime, timezone


def env(name: str, default: str | None = None) -> str | None:
    val = os.environ.get(name)
    return val if val not in (None, "") else default


def env_float(name: str, default: float) -> float:
    val = os.environ.get(name)
    return float(val) if val not in (None, "") else default


def env_int(name: str, default: int) -> int:
    val = os.environ.get(name)
    return int(val) if val not in (None, "") else default


def load_run_results(results_dir: str) -> list[dict]:
    paths = sorted(glob.glob(os.path.join(results_dir, "wbs-tree.result.*.json")))
    if not paths:
        raise SystemExit(
            f"No wbs-tree.result.*.json files found in {results_dir!r} - the k6 loop produced no "
            "output. This is an infra/wiring failure, not a perf verdict; failing loudly rather "
            "than silently reporting an empty aggregate."
        )
    runs = []
    for p in paths:
        with open(p, encoding="utf-8") as f:
            data = json.load(f)
        if data.get("p95Ms") is None:
            # A run whose own k6 process crashed before handleSummary ran would leave a
            # placeholder/partial file; skip rather than let `None` poison statistics.median.
            print(f"::warning::{p} has no p95Ms - excluding it from aggregation.")
            continue
        runs.append(data)
    if not runs:
        raise SystemExit(f"All {len(paths)} result file(s) in {results_dir!r} were unusable (no p95Ms).")
    return runs


def read_history(trend_csv_path: str, trigger_filter: str, window: int) -> list[float]:
    if not trend_csv_path or not os.path.isfile(trend_csv_path):
        return []
    rows = []
    with open(trend_csv_path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row.get("trigger") == trigger_filter:
                try:
                    rows.append((row["date"], float(row["median_p95_ms"])))
                except (KeyError, ValueError):
                    continue
    rows.sort(key=lambda r: r[0])
    return [p95 for _date, p95 in rows[-window:]]


def main() -> None:
    results_dir = env("RESULTS_DIR", ".")
    hard_threshold_ms = env_float("HARD_THRESHOLD_MS", 100.0)
    regression_multiplier = env_float("REGRESSION_MULTIPLIER", 1.25)
    min_history_points = env_int("MIN_HISTORY_POINTS", 3)
    max_history_window = env_int("MAX_HISTORY_WINDOW", 7)
    run_date = env("RUN_DATE_UTC", datetime.now(timezone.utc).strftime("%Y-%m-%d"))
    run_trigger = env("RUN_TRIGGER", "workflow_dispatch")
    commit_sha = env("COMMIT_SHA", "unknown")
    run_url = env("RUN_URL", "")
    trend_csv_path = env("TREND_CSV_PATH", "")
    output_json_path = env("OUTPUT_JSON_PATH", "aggregate.json")
    new_row_path = env("NEW_TREND_ROW_PATH", "new-trend-row.csv")

    runs = load_run_results(results_dir)
    n_runs = len(runs)
    p95_values = [r["p95Ms"] for r in runs]
    p99_values = [r["p99Ms"] for r in runs if r.get("p99Ms") is not None]
    fail_rates = [r["httpReqFailedRate"] for r in runs if r.get("httpReqFailedRate") is not None]

    median_p95 = statistics.median(p95_values)
    min_p95 = min(p95_values)
    max_p95 = max(p95_values)
    mean_p95 = statistics.fmean(p95_values)
    median_p99 = statistics.median(p99_values) if p99_values else None
    max_http_req_failed_rate = max(fail_rates) if fail_rates else None

    pass_count = sum(1 for v in p95_values if v < hard_threshold_ms)
    fail_count = n_runs - pass_count

    # Trend: only ever compared against prior *schedule*-triggered rows (see module docstring) -
    # a workflow_dispatch debugging run (e.g. N=3 for a quick check) must never become part of
    # the baseline other nights are measured against.
    history = read_history(trend_csv_path, trigger_filter="schedule", window=max_history_window)
    rolling_baseline_p95 = statistics.fmean(history) if len(history) >= min_history_points else None

    hard_breach = median_p95 >= hard_threshold_ms
    trend_regression = (
        rolling_baseline_p95 is not None
        and median_p95 > rolling_baseline_p95 * regression_multiplier
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
        "hardThresholdMs": hard_threshold_ms,
        "regressionMultiplier": regression_multiplier,
        "medianP95Ms": median_p95,
        "minP95Ms": min_p95,
        "maxP95Ms": max_p95,
        "meanP95Ms": mean_p95,
        "medianP99Ms": median_p99,
        "maxHttpReqFailedRate": max_http_req_failed_rate,
        "passCount": pass_count,
        "failCount": fail_count,
        "rollingBaselineP95Ms": rolling_baseline_p95,
        "rollingBaselineNPoints": len(history),
        "hardBreach": hard_breach,
        "trendRegression": trend_regression,
        "verdict": verdict,
        "perRun": [
            {
                "p95Ms": r.get("p95Ms"),
                "p99Ms": r.get("p99Ms"),
                "avgMs": r.get("avgMs"),
                "maxMs": r.get("maxMs"),
                "requestCount": r.get("requestCount"),
                "httpReqFailedRate": r.get("httpReqFailedRate"),
            }
            for r in runs
        ],
    }

    with open(output_json_path, "w", encoding="utf-8") as f:
        json.dump(aggregate, f, indent=2)

    # The trend row this run contributes - the workflow appends it to TREND_CSV_PATH itself
    # (after this script runs, so today's own row is never part of today's baseline computation
    # above) and commits/pushes it on the dedicated `perf-trend` branch.
    with open(new_row_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow([
            run_date, run_trigger, commit_sha, n_runs,
            f"{median_p95:.2f}", f"{min_p95:.2f}", f"{max_p95:.2f}", f"{mean_p95:.2f}",
            pass_count, fail_count,
            f"{rolling_baseline_p95:.2f}" if rolling_baseline_p95 is not None else "",
            hard_breach, trend_regression, verdict, run_url,
        ])

    baseline_str = f"{rolling_baseline_p95:.2f} ms (n={len(history)})" if rolling_baseline_p95 is not None else "n/a (insufficient history)"
    summary_lines = [
        "### Nightly WBS-tree perf (S4-DO-01)",
        "",
        f"**Verdict: {verdict}**",
        "",
        "| Metric | Value |",
        "| --- | --- |",
        f"| Runs aggregated (N) | {n_runs} |",
        f"| Median P95 | {median_p95:.2f} ms |",
        f"| Min / Max P95 | {min_p95:.2f} / {max_p95:.2f} ms |",
        f"| Mean P95 | {mean_p95:.2f} ms |",
        f"| Median P99 | {median_p99:.2f} ms |" if median_p99 is not None else "| Median P99 | n/a |",
        f"| Individual-run pass rate | {pass_count}/{n_runs} under {hard_threshold_ms:.0f} ms |",
        f"| Rolling baseline (last {max_history_window} scheduled nights) | {baseline_str} |",
        f"| Hard target ({hard_threshold_ms:.0f} ms) | {'BREACHED' if hard_breach else 'met'} |",
        f"| Trend regression (> {regression_multiplier}x baseline) | {'YES' if trend_regression else 'no'} |",
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
            f.write(f"median_p95_ms={median_p95:.2f}\n")


if __name__ == "__main__":
    main()
