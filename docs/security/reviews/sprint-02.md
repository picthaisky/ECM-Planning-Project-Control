# Sprint 2 Security Review (S2-SEC-01)

**Reviewer:** `security-auditor` · **Date:** 2026-07-28 · **Scope per `docs/10.` §6:** JWT
(alg/lifetime/secret), tenant claim trust, approval-policies endpoint authorization.

**Verdict: PASS — zero Critical, zero High findings open.** DoD met: the approval-policies
endpoint does not leak cross-tenant existence and correctly separates 403 (wrong role) from 404
(wrong tenant).

## 1. JWT configuration — OK

`backend/src/CMPlus.WebApi/Program.cs:72-82`: issuer/audience/lifetime/signing-key validation all
genuinely enabled, `ClockSkew = Zero`, unsigned (`alg: none`) tokens rejected, no algorithm-confusion
surface (symmetric key only). No signing key ships anywhere: `infra/docker/docker-compose.yml` uses
`${JWT_SIGNING_KEY:?set JWT_SIGNING_KEY in .env}` (no default), `appsettings.json` has no key.

The lazy `IOptions<JwtOptions>` pattern (needed so `WebApplicationFactory` test overrides apply)
was flagged as an acceptable-but-improvable gap: a missing `Jwt:SigningKey` previously surfaced
only as an obscure failure on the first authenticated request, not at boot. **Fixed** (see §6).

## 2. Password hashing — OK

Verified in code, not by trusting the stated numbers: PBKDF2-HMACSHA256, 210,000 iterations
(`Pbkdf2PasswordHasher.cs:18`), 16-byte CSPRNG salt generated per-hash (line 26), constant-time
comparison via `CryptographicOperations.FixedTimeEquals` on verify (line 51).

## 3. Tenant claim trust — OK

The JWT `tenantId` claim is confirmed the sole source of tenant context: `HttpContextTenantProvider.cs:21`,
the global EF Core query filter (`CmPlusDbContext.cs:95`), and server-side `TenantId` stamping
(lines 128-137). No handler reads a `tenantId` from route/query/body and trusts it directly.
`GetApprovalPolicyQuery` carries no client-supplied tenant id at all. One `IgnoreQueryFilters()`
exists in production code (`UserReader.cs:20`) — scoped narrowly to the login lookup, which by
definition has no tenant context yet.

## 4. Policy endpoint authorization — OK

`TenantApprovalPoliciesController.cs`: `[Authorize(Roles = "Admin")]` rejects non-Admin callers
with 403 before the action body runs (line 22); a path `tenantId` that doesn't match the caller's
JWT `tenantId` claim returns a bare 404 before any further I/O (lines 29-32) — no existence leak,
no timing oracle, no bypass found.

## 5. Frontend token storage — decision confirmed correct

`frontend-developer` chose in-memory-only storage (Zustand, no `persist` middleware) specifically
to bound XSS blast radius, at the cost of requiring re-login on a full page reload. Reviewed
`web/src/store/authStore.ts` and `web/src/services/apiClient.ts`: a repo-wide grep for
`localStorage`/`sessionStorage`/`persist`/`devtools`/`console.*` in `web/src` returns zero hits.
Decision stands as-is.

## 6. General pass — one real finding, fixed

- No signing key or other secret reaches `AuditLog.BeforeJson`/`AfterJson`.
- `ApprovalRoutingService`/`ApprovalPolicy` persistence is pure LINQ/EF Core — no dynamic SQL,
  no injection surface.
- **M-01 (fixed):** `AuditSaveChangesInterceptor` was writing `User.PasswordHash` into audit
  snapshots in cleartext (the hash itself, not the password — but an audit trail has none of the
  access restrictions or rotation discipline the `Users` table's own column has, so a leaked/
  queried `AuditLog` would become a second place credential material is recoverable from). Fixed:
  `PasswordHash` (and any future sensitive property added to the same redaction set) is now
  written as `***REDACTED***` in both `BeforeJson` and `AfterJson`. Covered by a new test,
  `PasswordHash_Is_Redacted_From_Audit_Snapshots_Never_Stored_In_Cleartext`.

## Findings

### Medium (fixed this sprint)

| ID | Finding | Resolution |
| :-- | :-- | :-- |
| M-01 | `User.PasswordHash` written in cleartext into `AuditLog.BeforeJson`/`AfterJson` | Redacted via a property-name denylist in `AuditSaveChangesInterceptor`; test added |
| M-02 | No startup validation on `Jwt:SigningKey` (missing/short key only failed on first authenticated request); `JwtOptions.cs` doc comment falsely claimed startup validation already existed | `AddOptions<JwtOptions>().Validate(...).ValidateOnStart()` added (min 32 chars / 256 bits for HS256, non-empty issuer/audience, positive expiry); comment corrected |

### Medium (tracked, not fixed this sprint — deferred to Sprint 15's full security-hardening pass)

| ID | Finding | Why deferred |
| :-- | :-- | :-- |
| M-03 | No rate limiting on `POST /api/v1/auth/login` (credential-stuffing/brute-force exposure) | Needs a deliberate rate-limiting policy choice (fixed-window vs sliding vs token-bucket, per-IP vs per-account) and testing under load — a design decision, not a one-line fix. Sprint 15 (`docs/10.` §9, "Security audit เต็มรอบ") owns this class of hardening. |
| M-04 | No Content-Security-Policy header on `web.nginx.conf` (defense-in-depth against XSS, which is the exact threat the in-memory-token decision in §5 is already designed to bound) | A CSP must be authored against the actual built asset graph (inline styles/scripts, connect-src to the real API origin) and regression-tested against the running app, not guessed at during a docs-only review pass. Tracked for Sprint 15 alongside M-03. |

### Low (tracked, non-blocking)

| ID | Finding |
| :-- | :-- |
| L-01 | Login response-time difference between "unknown email" and "wrong password" paths is a minor timing oracle for account enumeration |
| L-02 | `TokenValidationParameters` should pin `ValidAlgorithms = [HmacSha256]` explicitly rather than relying on the default algorithm set |
| L-03 | No `FallbackPolicy` configured — an endpoint added later without an explicit `[Authorize]` attribute would default to anonymous access rather than fail closed |
| L-04 | No token revocation mechanism (expected — full logout/refresh-token design is out of Sprint 2's scope) |
| L-05 | Auth events (login success/failure) are not themselves written to a dedicated audit/security-event log, only ordinary entity mutations are |
| L-06 | `DevDataSeeder.cs:54-63`: a tenant-creation audit row is attributed to the wrong actor context in dev seeding (dev-only code path, not production-reachable) |
| L-07 | `IApprovalRoutingService` trusts its caller-supplied candidate policy list rather than independently re-querying/re-validating it — fine while the only caller is the read endpoint reviewed here, worth re-checking once Sprint 9/10 add real callers |

## Re-verification after fixes

`dotnet build backend/CMPlus.sln` — 0 errors. `dotnet test` — **175/175 passing** (94 Domain + 29
Application + 4 Architecture + 48 Integration), up from the pre-fix 174/174 (net +1: the new
redaction test).
