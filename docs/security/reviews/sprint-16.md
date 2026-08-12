# Security Review — Sprint 16A (S16-SEC-01): Pre-Production Gate

**Scope (per plan §16A DoD):** secret management, TLS/HSTS/security headers, rate limiting, CORS,
error responses not leaking system detail, logs free of PII/token. This is the review that gates
the promotion of staging to production (US-16.1 sign-off).

**Method:** verification against the shipped code on branch `supachai.nil` (not a checklist walk).
Each PASS below cites the code that makes it true; each OPEN finding cites the concrete gap and the
exact, topology-correct fix. One gap (L-06) was closed *in* this review and is proven by an
end-to-end test.

**Verdict:** **CONDITIONAL PASS.** No Critical/High is left in the application code itself. One
High-severity finding (**F-1, ForwardedHeaders**) and three lower findings are **deployment-topology
dependent** — they cannot be fixed correctly without the real reverse-proxy/LB and web-app origin,
and a *wrong* fix (e.g. trust-all `UseForwardedHeaders`) is worse than the gap. They are therefore
handed to S16-DO-01/02 as required deploy-config, and re-checked on the running staging environment
before the US-16.1 sign-off. **F-1 must be resolved before production** because it silently defeats
the login brute-force protection.

---

## Fixed during this review

### L-06 — No global security-headers middleware → **FIXED + mutation-verified**
- **Was:** only `ProjectPhotosController.Get` set `X-Content-Type-Options: nosniff`, on photo
  responses alone; every other response (API JSON, errors, health) carried no security headers.
  Tracked since sprint-10.md as L-06 and explicitly anticipated by
  `ProjectPhotosControllerHeaderTests` ("a future global security-header middleware … tracked
  separately").
- **Now:** `SecurityHeadersMiddleware` (registered outermost in `Program.cs`) sets on **every**
  response, via an `OnStarting` callback so it also covers `UseExceptionHandler` error responses and
  the rate limiter's 429 short-circuit:
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
  `Cross-Origin-Resource-Policy: same-origin`. Written with indexer assignment (never `.Append`), so
  it never doubles the header the photo controller already sets ("nosniff,nosniff").
- **Proof:** `SecurityHeadersTests` asserts all four headers end-to-end against `/health/live`
  (an assertion that was *impossible* before this middleware, per the photo test's own note).
  Mutation-checked: commenting out the registration turns all 5 cases **red**; reverting restores
  green (5/5). `ProjectPhotosControllerHeaderTests` still passes (no double-set regression).

### HSTS not emitted → **FIXED (production only)**
- `Program.cs` now calls `app.UseHsts()` in non-Development environments (never in Development, where
  the app is reached over plain-HTTP localhost and an HSTS pin would wrongly persist in a developer's
  browser). Pairs with the existing `UseHttpsRedirection`. See F-5 for the preload/subdomain decision
  deferred to a stable domain.

---

## Verified PASS (cited)

| Area | Verdict | Evidence |
| --- | --- | --- |
| **Secret management** | PASS | `Jwt__SigningKey`, `ConnectionStrings__CmPlusDb`, `Excel__CommercialLicenseKey` are all env/config-only and absent from `appsettings.json` and every image layer — confirmed by the explicit `_comment` markers in `appsettings.json` and `docs/security/secrets-policy.md`. |
| **Error non-leakage** | PASS | `ResultProblemMapper` sets `Detail = errorCode` — stable machine codes (e.g. `ContractValueNotConfigured`), never exception text or stack traces. JWT auth failures return 401 + `ProblemDetails` (`Program.cs` JWT events). `GlobalExceptionHandler` renders a stable ProblemDetails body for anything unexpected; `UseExceptionHandler` is registered so it wraps the whole pipeline. |
| **Log hygiene (PII/token)** | PASS | No logging of password/token/secret anywhere in `backend/src` (grep swept; all "Log" hits are domain entities — weather/issue/manpower logs — or the `LoginCommand` record *declaring* a field, not logging it). No `Console.Write*`. No `UseHttpLogging`/request-body logging middleware that could capture credentials. |
| **Rate limiting** | PASS (see F-1) | M-1 login limiter: chained per-IP (10/60 s) + per-account (5/300 s) sliding windows scoped to the login route; 429 + `Retry-After` + `application/problem+json`. **Effectiveness of the per-IP arm depends on F-1.** |
| **JWT / authn** | PASS | Algorithm pinned to `HS256` only (`ValidAlgorithms`, L-02) — no `alg` confusion. Global `FallbackPolicy` requires an authenticated user (L-05), so no endpoint is accidentally anonymous; health probes are explicitly `AllowAnonymous`. |
| **Tenant isolation** | PASS | Ambient EF global query filter on every `ITenantOwned` entity (ADR-0002); `TenantIsolationBypassGuardTests` (S15-SEC-01) fences the only three sanctioned filter-bypasses and fails on any new `IgnoreQueryFilters`/raw-SQL. |
| **HTTPS redirect** | PASS | `UseHttpsRedirection` present; HSTS now added (above). |

---

## OPEN findings — deployment-topology dependent (owner: S16-DO-01/02, re-check on running staging)

### F-1 (HIGH) — `UseForwardedHeaders` is absent; behind the LB the per-IP rate limiter sees the proxy IP
- **Impact:** The staging/production topology (§16A: `api + web + MSSQL + object storage`, TLS) puts
  a reverse proxy / load balancer in front of the API. The M-1 login limiter partitions its per-IP
  window on `HttpContext.Connection.RemoteIpAddress`, which behind a proxy is the **proxy's** address
  — so every client shares one bucket and the per-IP brute-force protection is effectively defeated
  (either everyone is throttled together, or the limit never bites per attacker). `UseHttpsRedirection`
  and scheme detection can also misjudge the original scheme.
- **Fix (must be correct, not just present):** add `UseForwardedHeaders` (processing
  `X-Forwarded-For` + `X-Forwarded-Proto`) **before** `UseHttpsRedirection` and the rate limiter —
  **but restrict it to the known proxy** via `KnownProxies`/`KnownNetworks` set to the real LB
  address/subnet. A trust-all configuration is *worse than the gap*: any client could spoof
  `X-Forwarded-For` and trivially evade the limiter. Because the correct value is the deploy's proxy
  address, this is wired in `infra/staging` config, not hard-coded now.
- **Gate:** re-test on running staging — repeated failed logins from one client must 429 that client
  without 429-ing others. **Blocks production.**

### F-2 (MEDIUM) — No `Content-Security-Policy`
- Add `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` for this JSON API. Not
  set in `SecurityHeadersMiddleware` now because the exact policy depends on whether anything HTML is
  ever served on this origin (the dev-only OpenAPI document, any future first-party page); for a pure
  JSON API on its own origin the strict policy above is safe and should be added once the topology is
  confirmed. `X-Frame-Options: DENY` already covers framing in the interim.

### F-3 (MEDIUM) — No CORS policy configured
- If the web app is served **same-origin** (a reverse proxy fronts both `api` and `web`), no CORS is
  correct and this is a non-issue — confirm the topology. If the web app is a **different origin**,
  add a strict `AddCors`/`UseCors` allow-listing exactly that origin (placed before
  `UseAuthentication`); **never** `AllowAnyOrigin` together with credentials.

### F-4 (LOW–MEDIUM) — `AllowedHosts: "*"`
- Pin `AllowedHosts` to the real production hostname(s) to close Host-header injection / cache-poison
  vectors. Wildcard is fine for local/dev only.

### F-5 (INFO) — HSTS `preload` / `includeSubDomains`
- HSTS is now emitted with framework defaults. Once the production domain is final and confirmed
  HTTPS-only across all subdomains, consider `includeSubDomains` and (deliberately, and only when
  ready to commit long-term) `preload`.

---

## Sign-off checklist for US-16.1 (fill on running staging)

- [ ] F-1 resolved in staging config and re-tested (per-client 429 isolation) — **prod blocker**
- [ ] F-3 topology decided (same-origin ⇒ no CORS, else strict allow-list) and verified from the web app
- [ ] F-2 CSP added and app still functions
- [ ] F-4 `AllowedHosts` pinned
- [ ] Smoke test (S16-QA-02) green post-deploy: login, WBS load, Gantt, EVM, doc create/approve
- [ ] TLS terminates correctly; HSTS header observed on a prod-like HTTPS response
- [ ] Secrets sourced from the platform store, not the image (spot-check the running container env)

*Prepared under S16-SEC-01. Code-verified findings only; nothing in this document is asserted without
either a cited code path or a test.*
