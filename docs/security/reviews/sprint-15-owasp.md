# Sprint 15 Security Review — OWASP Top 10: Auth / Upload / Export (S15-SEC-02)

**Reviewer:** `security-auditor` · **Date:** 2026-08-11
**Scope:** OWASP Top 10 walk of the **auth**, **upload (photo)**, and **export (Excel/template)**
surfaces. This is the sibling of **S15-SEC-01** (full-system tenant-isolation sweep, PASS,
`docs/security/reviews/sprint-15-tenant-isolation.md`) — that review is **not** repeated here.
**DoD (verbatim):** *"ไม่มี finding Critical/High ค้าง; finding ระดับ Medium มีเจ้าของและกำหนดเวลาปิด"* —
no Critical/High may remain open at close; every Medium has a named owner and a target close date.

Read against **ADR-0002** (tenant isolation), **ADR-0008** (approval policy engine), **ADR-0010**
(cloud-agnostic storage / future S3 adapter), **ADR-0013** (append-only ledgers), **ADR-0014**
(EPPlus/QuestPDF licensing), **ADR-0018** (project-scoped authorization gap), and the prior reviews
`sprint-02.md` (JWT/hashing/rate-limit/FallbackPolicy), `sprint-03.md` (EPPlus licence, import
hardening), `sprint-09.md` §10, `sprint-10.md` §9/§10, and `sprint-12.md` §9/§10 (photo path).
Known open items are **folded in with a current verdict**, not re-reported as new.

---

## Verdict

> **PASS the sprint gate — 0 Critical, 0 High open across auth, upload, and export.**
> Every prior High on these surfaces is closed and re-confirmed against the current tree. Five
> **Medium** items carry a named owner and a target (below), satisfying the DoD. The DoD's
> "no High open" rule is met; no finding here is High.

| Surface | Verdict |
| :-- | :-- |
| **Auth** (A01/A07) | **PASS** — JWT sound, hashing adequate; the only gaps are Medium rate-limiting and Low defense-in-depth |
| **Upload — photo** (A03/A04/A08) | **PASS** — S12 H-01/N-01 fixes hold; magic-byte, EXIF, serve-path headers all sound |
| **Export — Excel/template** (A01/A03) | **PASS** — formula injection defended; export is tenant+project scoped; the EPPlus licence is a Medium |

---

## Method, and its limits — read before trusting anything below

Docker Desktop cannot start on this machine (requires Administrator; `docs/perf/gantt-frontend-s6.md`
§3), so **there is no SQL Server and no running API**. Nothing here is a live penetration test, and
**nothing HTTP-transport-level (TLS/HSTS/CORS/cookies/response-compression) is executable** — those
items are **code-verified only** and are listed in §7.

- **Code-verified** — read the production source directly.
- **Execution-verified** — the S12 photo fixes were re-confirmed by execution in that review against
  the real assemblies; this pass re-reads the current source to confirm no regression, and runs the
  full toolchain. No repository file was created, modified or deleted by this audit.

### Toolchain, re-run at review time (per project — no summed total)

| Command | Result |
| :-- | :-- |
| `dotnet build backend/CMPlus.sln -c Release` | **0 Warning(s), 0 Error(s)** |
| `dotnet test backend/tests/CMPlus.Domain.Tests` | **Passed! 406/406**, 0 skipped |
| `dotnet test backend/tests/CMPlus.Application.Tests` | **Passed! 691/691**, 0 skipped |
| `dotnet test backend/tests/CMPlus.Architecture.Tests` | **Passed! 17/17**, 0 skipped |
| `dotnet test backend/tests/CMPlus.Integration.Tests` | **Passed! 569/569**, 0 skipped |
| `dotnet list backend/CMPlus.sln package --vulnerable --include-transitive` | **no vulnerable packages**, 8/8 projects |
| `npm audit --omit=dev` (`web/`) | **found 0 vulnerabilities** |
| `npm audit` (incl. dev) | **2 High** — `nanoid` (GHSA-2v37-7h3g-55p8), `brace-expansion` (GHSA-rgw5-rvv9-x895); **build/test-time only**, unchanged from S12 L-08 |

**A06 (vulnerable components):** the shipped surface is clean — NuGet 8/8 clean, `npm audit
--omit=dev` 0. The two `npm` Highs are dev-toolchain only (never reach the browser bundle).

---

## 1. Auth surface (A01 Broken Access Control, A07 Identification & Authentication Failures)

### 1.1 JWT issuance & validation — SOUND (code-verified)

- **Issuance** (`JwtTokenService.cs`): HS256 over a `SymmetricSecurityKey` from `JwtOptions`
  (config/env only, never hardcoded); embeds `tenantId`/`userId`/`role` + `sub`/`jti`; `notBefore`
  and `expires` from the injected clock. No secret is ever logged (Sprint-2 M-01 redaction of
  `PasswordHash` in audit snapshots intact).
- **Validation** (`Program.cs:114-124`): `ValidateIssuer`/`ValidateAudience`/`ValidateLifetime`/
  `ValidateIssuerSigningKey` all `true`, `ClockSkew = TimeSpan.Zero`. A missing/forged/expired token
  yields **401 + `application/problem+json`** via the `OnChallenge` handler (`:130-145`) — never a
  bare framework challenge or a stack trace. **Fail-closed.**
- **Alg confusion:** the key is **symmetric-only**; no asymmetric key exists anywhere, so the classic
  RS256→HS256 confusion has **no surface**, and `alg:none` is rejected. **L-02 (pin
  `ValidAlgorithms=[HmacSha256]`)** stays a Low defense-in-depth item — relevant only if an
  asymmetric key is later added.
- **Startup fail-fast** (`DependencyInjection.cs:171-178`): `ValidateOnStart()` rejects a
  missing/short (`< 32` char) signing key, empty issuer/audience, or non-positive expiry at boot.
- **Tenant claim trust** (`HttpContextTenantProvider.cs`): `TenantId` is read from the JWT claim
  **only** and **throws** (fail-closed) if absent — never from route/query/body. `Role`
  likewise throws on a missing/invalid role claim. Confirms ADR-0002.

### 1.2 The standing L-05 (`FallbackPolicy`) — HARD VERDICT: latent, **Low, does not block**

`Program.cs:149` calls `AddAuthorization()` with **no `FallbackPolicy`**, so a controller added
without `[Authorize]` would default to **anonymous**. Four sprints open (S2 L-03 → S9 L-05 → S10
L-05 → here); A01-class.

**Verdict.** All 27 controllers under `backend/src/CMPlus.WebApi/Controllers` are explicitly
authorized — class-level `[Authorize]`/`[Authorize(Roles=…)]`, or `[Authorize]` on every action —
and the **only** `[AllowAnonymous]` is `AuthController.Login`. So the gap is **latent, not live**;
no current endpoint is anonymously reachable. **Low, not High** → the DoD's "no High" rule does not
force it closed. It is the cheapest hardening item in the codebase — **recommended for closure now.**

**Fix.** `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();`
plus explicit `[AllowAnonymous]` on `/auth/login` and the `/health/*` endpoints.

### 1.3 Rate limiting on `/auth/login` — **OPEN (M-1)**

Repo-wide grep for `RateLimiter`/`AddRateLimiter`/`UseRateLimiter` → **zero hits**. No rate limiting,
no account lockout, no CAPTCHA anywhere. `LoginCommandHandler` returns a generic `InvalidCredentials`
for both unknown-email and wrong-password (no enumeration via the body), but nothing throttles online
brute-force / credential-stuffing on a multi-tenant SaaS holding contract/payment data. PBKDF2 @210k
raises per-attempt cost but is not a rate limit; it also short-circuits for unknown emails, leaving
Sprint-2 **L-01**'s minor timing oracle in place (Low). **Sprint-2 M-03 — the hardening class this
sprint owns.** See M-1.

### 1.4 Password hashing — ADEQUATE (code-verified)

`Pbkdf2PasswordHasher`: PBKDF2-HMAC-SHA256, **210,000 iterations** (OWASP-2023 floor), 16-byte
CSPRNG salt per hash, 32-byte key, `CryptographicOperations.FixedTimeEquals` on verify, self-
describing `v1.iters.salt.hash` format, malformed-hash → `false` (never throws). No finding.

### 1.5 Actor `?? Guid.Empty` (L-01) — money paths fixed; a few non-money handlers remain

**Every money-moving handler fails closed** on a null actor (all five Payment, all VO). Photo,
Baseline, CPM, EOT, Issues, Manpower, Weather likewise. **Residual (Low, L-01b):**
`RecordActualCostCommandHandler:65`, `ImportScheduleFileCommandHandler:50`,
`ImportExcelProgressCommandHandler:28`, `BatchRecordProgressCommandHandler:32` still fabricate
`Guid.Empty` — unreachable behind `[Authorize]`, but a `Guid.Empty` actor on an audit row is
evidentially worthless. Role-gating on money endpoints verified consistent (S9 §5, S10 §6).

### 1.6 Error leakage (A04/A05) — CONTAINED

`GlobalExceptionHandler` maps 500s to a fixed ProblemDetails and **never** emits `exception.Message`,
SQL, stack traces, or file paths. `DomainException`/`ValidationException` → 400 with their
developer-authored message (informational: such messages must never embed internal detail).

---

## 2. Upload surface — the photo path (A03 Injection, A04 Insecure Design, A08 Integrity)

The Sprint-12 fixes are **present and correct in the current tree** (re-read this pass; S12 §9
execution-verified them on stored bytes):

- **A08 · EXIF stripping (H-01) holds.** `ExifScrubber.StripJpeg` walks **past** SOS via
  `FindEndOfEntropyCodedData` and resumes filtering; truncates unconditionally at the first EOI and
  discards MPF/motion-photo trailers. **N-01 holds** — a nested `FF D8` is **rejected**, not
  mis-framed into a verbatim copy. PNG path enforces per-chunk length bounds **and CRC-32** (L-02).
- **A03/A08 · Magic-byte validation cannot be bypassed.** `ImageSignatureValidator.DetectFormat` is
  the **only** format decision; client filename and multipart `Content-Type` never reach the handler.
- **A03 (stored XSS) · Serve path defended.** `ProjectPhotosController.Get` sets `nosniff` (indexer
  assignment) and `File(..., contentType, fileName)` forces `Content-Disposition: attachment` with a
  server-derived content-type (closed enum) and id-derived name. No `wwwroot`/`UseStaticFiles`, no
  signed URL. `GetPhotoContentQueryHandler` cross-checks `photo.ProjectId == request.ProjectId` → bare
  `NotFound` on same-tenant-wrong-project.
- **A04 · Size/DoS.** Rejects on `IFormFile.Length` before buffering; `[RequestSizeLimit(20 MiB)]`;
  handler cap `Math.Max(DeclaredContentLength ?? 0, Content.LongLength)` (L-01). Scrubber malformed-
  input safety re-proven in S12 §9.2 (no hang/OOM; only `ImageProcessingException` escapes).

### Open photo-path items (folded in)

- **M-3 · Storage root defaults to OS temp, no startup guard (S12 M-03) — OPEN.**
  `FileStorageOptions.LocalRootPath` defaults to `Path.Combine(Path.GetTempPath(),
  "cmplus-file-storage")`; `appsettings.json` `Storage` is comment-only; DI binds with no
  `ValidateOnStart`. On Linux that is `/tmp` — world-traversable, ephemeral, unquota'd. Compounding:
  the blob is written **before** `SaveChangesAsync`, and `IFileStorage.DeleteAsync` has no production
  caller, so a failed save orphans a blob. No static-file serving exists, so nothing under the root is
  web-servable. See M-3.
- **M-4 · Future S3/pre-signed-URL adapter would strip the tenant check + headers (S12 M-02) — OPEN.**
  Forward-looking; the "no stored XSS / no cross-tenant read" property rests on the handler's
  tenant+project check plus two hand-set headers on one action. A pre-signed URL takes both out of
  the path. See M-4.
- **No per-tenant upload quota / rate limit** on the 10–20 MiB upload from the broad `Site` role —
  folded into M-1 (A04).
- **S12 M-1 (duplicate photo on outbox replay)** — now mitigated: `IdempotencyMiddleware` shipped
  (S13-BE-01), `[Idempotent]` is on the photo upload action.
- **L-03 (path-traversal `OrdinalIgnoreCase`)** — Low, unreachable (server-derived keys).
- **S12 M-04 (no photo erasure, PDPA)** — product-scope, `po-analyst`; standing.

---

## 3. Export surface — Excel/template (A01, A03)

### 3.1 CSV / formula injection — DEFENDED (code-verified)

The only production export path is the progress template
(`ExportProgressTemplateQueryHandler` → `ExcelProgressTemplateWriter`). The only user-controlled cells
(`ActivityCode`, `ActivityName`) are written through `FormulaInjectionGuard.EscapeForExport`
(prefixes `'` when a value begins `=`/`+`/`-`/`@`) **and** their columns are Text number-format. The
importer (`ExcelProgressImporter`) never calls `Calculate`, so a formula-typed cell yields only cached
text. Matches `conventions.md`. **No finding.**

### 3.2 Export authorization — tenant+project scoped, no cross-tenant leak

`GetProgressTemplate` (class-level `[Authorize]`) gates on `ProjectExistsAsync` (tenant-scoped by the
ambient filter; S15-SEC-01 row 21 = A+P). A foreign `projectId` → `ProjectNotFound`. No role
restriction (any authenticated tenant user can export any project's template **within their tenant**)
— the **within-tenant** ADR-0018 gap (M-5), not a cross-tenant breach. No export aggregates across
tenants.

### 3.3 EPPlus Polyform Noncommercial licence — **OPEN (M-2), production/legal blocker**

`EPPlus 8.5.4`; `ExcelPackageLicense.EnsureConfigured` calls `SetNonCommercialOrganization` unless
`Excel:CommercialLicenseKey` is set, and `appsettings.json` supplies only the noncommercial org name
— so **the shipped path runs on Polyform Noncommercial**. CM+ is a commercial SaaS; hard
production/legal blocker (A06-adjacent: a licence, not a CVE), open since Sprint 3 (L-06), restated in
ADR-0014. Not a code defect — the wiring already prefers a commercial key when present. See M-2.

---

## 4. Findings

### 4.1 Critical — none.
### 4.2 High — none.

**Unambiguously: zero Critical and zero High on auth, upload, or export.** Every prior High is closed
and re-confirmed. The sprint passes the DoD's "no High open" gate.

### 4.3 Medium — each with a named owner and a target (DoD requirement)

| ID | Finding (surface / OWASP) | Owner | Target |
| :-- | :-- | :-- | :-- |
| **M-1** | **No rate limiting / lockout on `POST /api/v1/auth/login`** — brute-force / credential-stuffing; also the missing per-tenant upload quota (A07/A04). S2 M-03; this sprint owns the class. | `backend-developer` | **Sprint 15 (this sprint)** |
| **M-2** | **EPPlus on Polyform Noncommercial** — production/legal release blocker (A06-adjacent). Procure a commercial key; wire `Excel__CommercialLicenseKey`. ADR-0014 / S3 L-06. | `human` (legal/procurement) + `backend-developer` (wire key) | **before the S16 production gate** |
| **M-3** | **`Storage:LocalRootPath` defaults to OS temp, no startup validation** — `/tmp` world-traversable/ephemeral; blob written before DB save; no orphan reaper (A04/A05). S12 M-03. | `devops-engineer` | **before the S16 staging deploy** |
| **M-4** | **A future S3/pre-signed-URL adapter would strip the tenant check + `nosniff`/`attachment` headers** (A01/A08). Record as a hard ADR constraint now; enforce when the adapter ships. S12 M-02. | `system-architect` (ADR) + `backend-developer` (adapter) | **ADR note S15; enforcement S16** |
| **M-5** | **No project-scoped authorization** — any tenant user with the right role can act on / export any project in that tenant (A01, within-tenant). ADR-0018; sprint-sized (new entity + migration + every handler). Not cross-tenant. | `backend-developer` (impl) + `po-analyst` (rollout) | **dedicated sprint (target S16)** |

### 4.4 Low (tracked, non-blocking)

- **L-05 · No `FallbackPolicy`** — latent (every controller explicitly authorized). **Recommended for
  closure this sprint.** `backend-developer`.
- **L-02 · JWT `ValidAlgorithms` not pinned** — no live surface (symmetric-only); pin `[HmacSha256]`.
  `backend-developer`.
- **L-01 · Login timing oracle** (unknown-email short-circuits PBKDF2) — minor enumeration aid.
- **L-01b · `?? Guid.Empty` actor** in `RecordActualCost`, `ImportScheduleFile`, `ImportExcelProgress`,
  `BatchRecordProgress` (money paths all fixed). Apply the `is not { }` guard.
- **L-03 · Path-traversal guard uses `OrdinalIgnoreCase`** — unreachable today; use `Ordinal` on
  non-Windows.
- **L-08 · `npm audit` (incl. dev) 2 High** — build/test-time only; `npm audit fix`; add full audit to
  CI. `frontend-developer`.

---

## 5. Areas explicitly checked and found sound

JWT issuance/validation/startup-validation, symmetric-only (no alg-confusion), fail-closed challenge,
JWT-only tenant/role resolution; PBKDF2-HMAC-SHA256 @210k with per-hash CSPRNG salt and constant-time
verify; photo scrubber (H-01/N-01/L-02 present), magic-byte validation, serve-path
`nosniff`/`attachment`/server-content-type/project-cross-check, size caps, malformed-input safety;
export formula-injection two-sided defense with a non-calculating importer, tenant+project scoped;
ProblemDetails-only error handling with no internal leakage; NuGet 8/8 clean; shipped npm bundle
clean; idempotency middleware shipped.

---

## 6. Standing findings — current status (folded in, not re-reported)

| ID (origin) | Item | Status |
| :-- | :-- | :-- |
| S9/S10/S12 all prior Highs (money/photo) | | **Closed** — re-confirmed |
| S2 M-03 (rate limiting) | Login brute-force | **Open → M-1** |
| S3 L-06 / ADR-0014 (EPPlus licence) | Polyform Noncommercial | **Open → M-2** |
| S12 M-03 (storage temp default) | `/tmp` root | **Open → M-3** |
| S12 M-02 (future storage adapter) | pre-signed URL strips controls | **Open → M-4** |
| S9 M-02 / S10 M-04 / ADR-0018 | within-tenant authorization | **Open → M-5** |
| S2 L-03 / S9 L-05 / S10 L-05 | FallbackPolicy latent | **Open, Low (L-05)** — verified latent |
| S12 M-04 (photo erasure / PDPA) | no delete path | **Open** — `po-analyst` |
| S12 L-08 (npm dev audit) | 2 High dev-only | **Open, Low** |
| ADR-0021 (NULL-`ProjectId` index) | approval-policy index | **Closed this sprint** — not an auth/upload/export item |

---

## 7. What could not be verified without a running system

1. **HTTP-transport-level.** `Program.cs` has `UseHttpsRedirection` but **no `UseHsts`** and **no
   `UseCors`/`AddCors`** — HSTS and CORS expected to terminate at the nginx proxy (same layer as
   Sprint-2 M-04's missing CSP). **Code-verified only.** Cookies n/a (in-memory tokens +
   `Authorization` header). Response-compression vs `application/problem+json` — not exercised.
2. **Rate-limit behavior** (M-1) — cannot be load-tested; does not yet exist.
3. **M-3/M-4 storage paths** on a real Linux container / S3 endpoint — no container, no adapter.
4. **EPPlus licence enforcement** at runtime with a real commercial key — no key configured.
5. **Live probing** — no timing analysis at scale, no fuzzing, no concurrency racing against a real DB.

Photo-path claims marked as holding are code-verified this pass and execution-verified in S12 §9.
Auth and export claims are code-verified. The toolchain table is tool-verified this pass.

---

## 8. Required before this item can close (DoD compliance)

The DoD is **met**: **0 Critical / 0 High open**, and **every Medium (M-1…M-5) has a named owner and
target** (§4.3). To retire the threads:

1. **M-1** — login rate limiter (per-IP + per-account) + per-tenant upload quota. `backend-developer`, S15.
2. **M-2** — procure + wire the EPPlus commercial key. `human` + `backend-developer`, before S16 gate.
3. **M-3** — fail startup when `Storage:LocalRootPath` unset outside Development; volume mount;
   orphan reaper / DB-before-blob. `devops-engineer`.
4. **M-4** — record the storage-adapter constraint in ADR-0010 now. `system-architect`.
5. **M-5** — per-project assignment enforcement. `backend-developer` + `po-analyst`, dedicated sprint.
6. **Recommended this sprint (cheap):** L-05 (FallbackPolicy), L-02 (pin `ValidAlgorithms`), L-01b
   (fail-closed actor on the four handlers), `npm audit fix` (L-08).
