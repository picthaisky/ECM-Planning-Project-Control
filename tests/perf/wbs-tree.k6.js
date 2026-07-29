/* =============================================================================================
   tests/perf/wbs-tree.k6.js

   S4-QA-01 (docs/10. Sprint 4, qa-engineer row) - full-stack HTTP-level load test for
   GET /api/v1/projects/{projectId}/wbs-tree against the REAL, running WebApi host (auth
   middleware + MediatR + EF Core + SQL Server), never the query handler in isolation - that
   handler-level number is database-engineer's S4-DB-01/S4-DB-02 measurement (P95=31.82ms). This
   script measures the number one layer up: what a real client actually experiences, including JWT
   auth, JSON serialization of a ~5,000-node tree, and network round trip.

   DoD (docs/10 §6 S4-QA-01): "report P95/P99 at 5,000 nodes in a reproducible format; exceeding
   100ms P95 = a red build." The threshold below is a genuine k6 `thresholds` entry - if P95 breaches
   it, k6 exits with a non-zero status code (99), which is what turns the build red in
   S4-DO-01's nightly CI job (.github/workflows/perf-nightly.yml) - this is not merely a report.

   PREREQUISITES (see docs/perf/baseline.md for the full runbook):
     1. The real stack is up: `docker compose -f infra/docker/docker-compose.yml up -d`
        (or at minimum mssql + api, since this script never touches web/minio).
     2. Migrations applied + S1-DB-03 dev tenant/users seeded (DevDataSeeder - gives us a real
        login: pm@siam-construction.dev / Dev@CMPlus2026!, see .env.example convention).
     3. tests/perf/seed-large-project.sql (S4-DB-02) has been run against that same database at
        least once - it is idempotent/deterministic, so re-running it is always safe and always
        produces the same ProjectId (see below).

   TARGET PROJECT ID: seed-large-project.sql derives every id deterministically from
   SHA2_256(@Seed + ':PROJECT:0') with the fixed @Seed 'CMPLUS-S4-DB-02-v1' - so the perf project's
   id is always the same, on any machine, every run: 2193e79c-a4c8-e7a5-0ffe-fb3f581af585 (verified
   by actually running the script - see docs/perf/baseline.md). Override with -e PROJECT_ID=... if
   a different @Seed or dataset is ever used.

   USAGE (host-networking, k6 installed locally):
     k6 run tests/perf/wbs-tree.k6.js \
       -e BASE_URL=http://localhost:5000 \
       -e PROJECT_ID=2193e79c-a4c8-e7a5-0ffe-fb3f581af585

   USAGE (containerized k6, no local install - what this repo's CI/nightly job and this task's own
   verification run both use): join the same compose network so the service DNS name "api" resolves,
   and target the API's *internal* container port (8080), not the host-published one.
     docker run --rm --network docker_default -v "$(pwd)/tests/perf:/scripts" \
       -e BASE_URL=http://api:8080 -e PROJECT_ID=2193e79c-a4c8-e7a5-0ffe-fb3f581af585 \
       grafana/k6 run /scripts/wbs-tree.k6.js

   ENV VARS (all optional, sensible defaults for local/CI dev-seeded stack):
     BASE_URL        default http://localhost:5000
     PROJECT_ID      default the deterministic perf-dataset project id above
     LOGIN_EMAIL     default pm@siam-construction.dev   (S1-DB-03 dev seed, any role works -
                                                          WbsController only requires [Authorize],
                                                          no role restriction)
     LOGIN_PASSWORD  default Dev@CMPlus2026!             (DevDataSeeder.DevSeedPassword)
     VUS             default 20   (concurrent virtual users hammering the endpoint)
     DURATION        default 30s
     P95_THRESHOLD_MS default 100 (the hard DoD gate - do not raise this to make a build pass)
   ============================================================================================= */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const PROJECT_ID = __ENV.PROJECT_ID || '2193e79c-a4c8-e7a5-0ffe-fb3f581af585';
const LOGIN_EMAIL = __ENV.LOGIN_EMAIL || 'pm@siam-construction.dev';
const LOGIN_PASSWORD = __ENV.LOGIN_PASSWORD || 'Dev@CMPlus2026!';
const VUS = parseInt(__ENV.VUS || '20', 10);
const DURATION = __ENV.DURATION || '30s';
const P95_THRESHOLD_MS = parseInt(__ENV.P95_THRESHOLD_MS || '100', 10);

// Separate, named Trend so the WBS-tree GET's own latency distribution is never diluted by the
// one-off setup() login call - the threshold below must apply to the endpoint under test only.
const wbsTreeDuration = new Trend('wbs_tree_duration', true);

export const options = {
  scenarios: {
    steady_load: {
      executor: 'constant-vus',
      vus: VUS,
      duration: DURATION,
    },
  },
  // k6's default end-of-test summary only computes avg/min/med/max/p90/p95 - p(99) and a request
  // count must be requested explicitly to appear in data.metrics.*.values (handleSummary below).
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)', 'count'],
  thresholds: {
    // The DoD gate (docs/10 S4-QA-01): P95 must stay under 100ms at the 5,000-node/10,000-activity
    // dataset. A breach fails this threshold, which fails the k6 run (exit code 99) - a hard,
    // automated red build, not a number a human has to notice in a report.
    'wbs_tree_duration': [`p(95)<${P95_THRESHOLD_MS}`],
    'http_req_failed': ['rate<0.01'],
  },
};

export function setup() {
  const loginRes = http.post(
    `${BASE_URL}/api/v1/auth/login`,
    JSON.stringify({ email: LOGIN_EMAIL, password: LOGIN_PASSWORD }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  if (loginRes.status !== 200) {
    throw new Error(
      `Setup login failed with HTTP ${loginRes.status}: ${loginRes.body}. ` +
      'Has the S1-DB-03 dev seed run against this database? (DevDataSeeder.SeedAsync)',
    );
  }

  const token = loginRes.json('accessToken');
  if (!token) {
    throw new Error('Login response had no accessToken field - check LoginResponse shape.');
  }

  return { token };
}

export default function (data) {
  const res = http.get(`${BASE_URL}/api/v1/projects/${PROJECT_ID}/wbs-tree`, {
    headers: { Authorization: `Bearer ${data.token}` },
    tags: { endpoint: 'wbs_tree' },
  });

  const ok = check(res, {
    'status is 200': (r) => r.status === 200,
    'body has rootNodes': (r) => {
      try {
        return Array.isArray(r.json('rootNodes'));
      } catch {
        return false;
      }
    },
  });

  if (ok) {
    // Only successful, real 200 responses count toward the perf statistic - a failed/auth-error
    // response would otherwise skew the latency distribution optimistically (fast failures).
    wbsTreeDuration.add(res.timings.duration);
  }

  sleep(0.05);
}

export function handleSummary(data) {
  const wbs = data.metrics.wbs_tree_duration;
  const p95 = wbs ? wbs.values['p(95)'] : undefined;
  const p99 = wbs ? wbs.values['p(99)'] : undefined;
  const avg = wbs ? wbs.values['avg'] : undefined;
  const max = wbs ? wbs.values['max'] : undefined;
  const count = wbs ? wbs.values['count'] : undefined;

  const reqFailedRate = data.metrics.http_req_failed
    ? data.metrics.http_req_failed.values.rate
    : undefined;

  const passed = p95 !== undefined && p95 < P95_THRESHOLD_MS;

  const summary = {
    endpoint: `GET ${BASE_URL}/api/v1/projects/${PROJECT_ID}/wbs-tree`,
    dataset: '5,000 WBSNodes / 10,000 Activities / 15,000 ActivityRelations (tests/perf/seed-large-project.sql)',
    vus: VUS,
    duration: DURATION,
    requestCount: count,
    httpReqFailedRate: reqFailedRate,
    p95Ms: p95,
    p99Ms: p99,
    avgMs: avg,
    maxMs: max,
    thresholdMs: P95_THRESHOLD_MS,
    thresholdPassed: passed,
    generatedAt: new Date().toISOString(),
  };

  const lines = [
    '=== S4-QA-01 WBS Tree API perf result (tests/perf/wbs-tree.k6.js) ===',
    `Endpoint     : ${summary.endpoint}`,
    `Dataset      : ${summary.dataset}`,
    `Load profile : ${VUS} VUs for ${DURATION}`,
    `Requests     : ${count}`,
    `http_req_failed rate: ${reqFailedRate}`,
    `P95: ${p95 !== undefined ? p95.toFixed(2) : 'n/a'} ms  (threshold < ${P95_THRESHOLD_MS} ms)`,
    `P99: ${p99 !== undefined ? p99.toFixed(2) : 'n/a'} ms`,
    `avg: ${avg !== undefined ? avg.toFixed(2) : 'n/a'} ms   max: ${max !== undefined ? max.toFixed(2) : 'n/a'} ms`,
    `RESULT: ${passed ? 'PASS' : 'FAIL'} - P95 ${passed ? 'is under' : 'EXCEEDS'} the ${P95_THRESHOLD_MS} ms DoD target`,
    '=======================================================================',
  ];

  return {
    stdout: lines.join('\n') + '\n',
    // Relative to the process's working directory - run k6 from tests/perf/ (or with that
    // directory mounted as the container's working directory) so this lands next to the script,
    // not wherever k6 happens to be invoked from.
    'wbs-tree.result.json': JSON.stringify(summary, null, 2),
  };
}
