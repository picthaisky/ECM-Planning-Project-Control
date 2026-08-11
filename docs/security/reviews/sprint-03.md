# Sprint 3 Security Review (S3-SEC-01)

**Reviewer:** `security-auditor` · **Date:** 2026-07-29 · **Scope per `docs/10.` §6 / §12:**
upload surface for XER / MSPDI / XLSX — size cap, content-type/magic-byte check, files stored
outside webroot, filename sanitization, path traversal. Read alongside ADR-0002 (tenant
isolation), ADR-0003 + ADR-0011 (MSPDI parsing, interim hand-written parser).

**Original verdict: FAIL — 1 High finding open (H-01).** Four of five DoD checklist items were
met at the time of this review; item 2 (content-type/magic-byte check) was not met.

**Updated verdict (2026-07-29, same day, orchestrator re-verification): PASS — all findings
closed.** See "Re-verification" at the end of this document for the actual evidence (real timing
re-measurement, real test counts, real vulnerability-scan output) — every item below was
independently re-confirmed, not just claimed fixed by the implementer.

All findings below were verified by execution, not by reading alone: two throwaway probe
projects were built against the real `CMPlus.Infrastructure` assembly and the real EPPlus 8.5.4
dependency. No repository code was modified.

---

## DoD checklist

| # | Item | Verdict |
| :-- | :-- | :-- |
| 1 | Size cap | **MET** — with the caveat in H-01 that a byte cap does not bound parse *work* |
| 2 | Content-type / magic-byte check | **NOT MET** — no verification exists; see M-01 |
| 3 | Files stored outside webroot | **MET** — structurally guaranteed; see §3 for a correction to the implementer's claim |
| 4 | Filename sanitized / no path traversal | **Traversal: MET.** **Sanitization: partial** — see L-01 |
| 5 | Tenant scoping, XXE consistency, formula-injection export coverage | **MET** on all three |

---

## 1. Size cap — MET

Both caps are enforced on the real request path, not merely present in config:

- `ImportOptions.MaxFileSizeBytes` (50 MiB, `backend/src/CMPlus.Infrastructure/Import/ImportOptions.cs:15`)
  is bound at `backend/src/CMPlus.Infrastructure/DependencyInjection.cs:38` and checked in **both**
  command handlers before any parser is constructed —
  `ImportScheduleFileCommandHandler.cs:54` and `ImportExcelProgressCommandHandler.cs:33`.
  Rejection produces a persisted `Failed` `FileImportJob`, which is the correct modelled outcome.
  Covered by tests in `backend/tests/CMPlus.Application.Tests/Features/Import/`
  (`ImportScheduleFileCommandHandlerTests.cs:128`, `ImportExcelProgressCommandHandlerTests.cs:103`).
- `MaxDecompressedSizeBytes` (250 MiB) is enforced in
  `backend/src/CMPlus.Infrastructure/Parsers/Excel/ExcelProgressImporter.cs:42` →
  `RejectIfDecompressedSizeExceedsCap` (lines 113-151), reading only the ZIP central directory's
  declared uncompressed sizes before `ExcelPackage` is constructed. The `!CanSeek` branch
  (lines 115-122) correctly **fails closed** rather than skipping the check. The zip-bomb fix is
  genuine and correctly placed.

Note: `MaxDecompressedSizeBytes` is absent from `appsettings.json`'s `Import` section, so the
250 MiB class default applies. That is correct behaviour, but adding the key with its
`_comment` (matching how `MaxFileSizeBytes` is documented there) would make the cap discoverable
to operators.

**Caveat (see H-01):** the byte cap bounds input size, not the work the parser performs on it.
A file three orders of magnitude *under* the cap can consume hours of CPU.

## 2. Content-type / magic-byte check — NOT MET

No magic-byte or content-sniffing verification exists anywhere on the upload path.
`backend/src/CMPlus.WebApi/Controllers/Import/ImportController.cs:47-56` dispatches purely on the
URL route segment (`xer`/`mspdi`/`xlsx`); neither command handler nor any parser inspects the
leading bytes. The frontend extension check
(`web/src/features/info/ImportUploadCard.tsx:61-64, 107-112`) is a UX affordance only and is
trivially bypassed — correctly, it is not relied on as a control.

I assessed whether this is benign, by feeding disguised files to each parser directly:

| Disguised input | Route | Actual behaviour |
| :-- | :-- | :-- |
| `PK\x03\x04…` (XLSX bytes) | `xer` | Clean rejection: `ImportMalformedFile: the file contains no TASK table.` |
| MSPDI XML | `xer` | Clean rejection: same message |
| XER text | `mspdi` | Clean rejection (`XmlReader` — not well-formed XML) |
| XER text / XML / random binary / plain non-OOXML ZIP | `xlsx` | **Unhandled `System.IO.InvalidDataException`** |

So the XER and MSPDI routes degrade cleanly and the missing check is low-impact **there**. The
**xlsx route does not** — `new ExcelPackage(excelContent)` at
`ExcelProgressImporter.cs:48` is not wrapped in a `try`/`catch`, and EPPlus 8.5.4 throws
`System.IO.InvalidDataException: The file is not a valid Package file...` for every non-OOXML
input (all four shapes above reproduced). This is also reachable by an *honest* user uploading a
password-protected workbook. Consequences are covered under M-01.

**This DoD item is therefore not confirmable and is recorded as not met.** The fix is small: a
leading-magic-byte assertion per format (`%T`/`ERMHDR` tab-delimited text for XER, `<?xml`/`<`
for MSPDI, `PK\x03\x04` for XLSX) in each command handler, before the parser is invoked,
producing a `Failed` job with a dedicated error code.

## 3. Files stored outside webroot — MET (with a correction)

The implementer's summary stated that raw uploaded files "are not persisted... no file-webroot-
storage surface for this checklist item." Verified directly, and the conclusion holds — but the
reasoning needs one correction, because a claim that no file ever touches disk is not accurate.

Verified:

- A repo-wide grep of `backend/src` for `File.Write*`/`File.Create`/`File.Open`/`FileStream`/
  `Path.Combine`/`Path.GetTempPath`/`GetTempFileName`/`Directory.Create`/`SaveAs` returns **zero
  hits in application code**. The only match anywhere is the *declaration* of
  `IFileStorage.SaveAsync` (`backend/src/CMPlus.Application/Abstractions/IFileStorage.cs:12`),
  which has **no implementation in the solution** — nothing can call it. `ImportController`,
  both command handlers, `ImportRepository`, and all three parsers operate exclusively on
  `MemoryStream`/`byte[]`.
- `FileImportJob` (`backend/src/CMPlus.Domain/Entities/FileImportJob.cs`) carries no blob key,
  path, or URL field. Only parsed results and job metadata are persisted.
- The API serves **no static files at all**: no `UseStaticFiles()` in
  `backend/src/CMPlus.WebApi/Program.cs`, and no `wwwroot` directory exists in the WebApi
  project. The SPA is built and served by a *separate* nginx container
  (`infra/docker/web.Dockerfile` + `web.nginx.conf`); the API container
  (`infra/docker/api.Dockerfile`) contains only published assemblies and runs as the non-root
  `app` user. **The API process and any web root are in different containers** — bytes written by
  the API cannot become web-reachable by construction. This is a stronger guarantee than the
  checklist asks for.

Correction: uploaded bytes **do** transit the filesystem, via ASP.NET Core itself, not via CM+
code. `IFormFile` model binding buffers each multipart file section through a
`FileBufferingReadStream`, which spills to a temp file in `Path.GetTempPath()` (`/tmp` in the
container) once the section exceeds `FormOptions.MemoryBufferThreshold` (default 64 KB) — i.e.
for essentially every real import. The temp file is named by the framework from a GUID (never
from the client-supplied filename), opened `FileOptions.DeleteOnClose`, and removed when the
request ends. Outside webroot, not client-named, not traversal-reachable: **the checklist item
passes.** It should be documented this way rather than as "no file touches disk", so that a
future change (e.g. raising `MemoryBufferThreshold`, mounting `/tmp` as a shared volume, or
adding the `IFileStorage` adapter ADR-0010 anticipates) is recognised as touching this surface.

## 4. Filename sanitization / path traversal — traversal MET, sanitization partial

`IFormFile.FileName` is client-controlled and is **not** sanitized: it flows verbatim from
`ImportController.cs:50/52/54` into `FileImportJob`'s constructor
(`FileImportJob.cs:55-57`, which only trims and rejects empty). No `Path.GetFileName()`, no
control-character stripping, no length bound.

Traversal is nonetheless **not exploitable**, and this was confirmed rather than assumed by
tracing every consumer of the value:

1. **Filesystem** — no path is ever constructed from it (see §3; zero path-building APIs in
   `backend/src`). A `../../etc/passwd` filename is stored as a literal string in an `nvarchar`
   column.
2. **HTTP response headers** — the only `Content-Disposition` the API emits is the *export*
   (`ImportController.cs:96-99`), which uses the hardcoded literal
   `"progress-update-template.xlsx"`. The uploaded name is never reflected into a header, so the
   classic response-splitting / RFC 6266 vector is absent.
3. **Logs** — no code path logs the filename. `GlobalExceptionHandler.cs:22` logs only
   `Request.Method` and `Request.Path`, so CR/LF log injection is not reachable.
4. **Audit** — written via `JsonSerializer.Serialize` (`ImportRepository.cs:50-57` and
   `102-106`), which escapes control characters correctly.
5. **UI** — rendered as JSX text (`ImportUploadCard.tsx:219`); React escapes it. No
   `dangerouslySetInnerHTML` anywhere in `web/src`.

The residual gap is L-01 (length/control characters), which is a robustness issue rather than a
traversal one.

## 5. General pass

### 5a. Tenant scoping (ADR-0002) — MET

Every import operation is tenant-scoped, and no handler accepts a client-supplied tenant id:

- `FileImportJob` implements `ITenantOwned`, so it automatically receives the reflection-applied
  global query filter (`CmPlusDbContext.cs:84-111`) and server-side `TenantId` stamping
  (`CmPlusDbContext.cs:143-152`). The handlers read `tenantProvider.TenantId` (JWT-claim backed)
  — never a route or body value.
- **IDOR on import:** `ImportRepository.ProjectExistsAsync` (`ImportRepository.cs:19-20`) runs
  through the tenant filter, so importing into another tenant's `projectId` returns 404 without
  disclosing existence. Covered by
  `backend/tests/CMPlus.Integration.Tests/WebApi/ImportControllerTests.cs:63-77`.
- **IDOR on job read:** `FindJobAsync` (`ImportRepository.cs:117`) is tenant-filtered, and
  `GetImportJobQueryHandler.cs:18` additionally rejects a job whose `ProjectId` does not match
  the route — closing the same-tenant cross-project case too.
- **History:** `GetJobHistoryAsync` (`ImportRepository.cs:121-127`) is tenant-filtered; an
  arbitrary `projectId` yields an empty list, not a leak.
- **Export:** `GetActivityTemplateRowsAsync` (`ImportRepository.cs:131-136`) and
  `LoadActivitiesForProjectAsync` (`ImportRepository.cs:82-84`) are doubly scoped — the
  `Activity` filter plus a tenant-filtered `WBSNodes` subquery.
- No `IgnoreQueryFilters()` exists anywhere in the import path. The only occurrence in production
  code remains `UserReader.cs:20` (login, pre-tenant-context), unchanged since Sprint 2.

Gap: **RBAC**, see M-04.

### 5b. XXE hardening consistency — MET

`MspdiXmlReaderFactory.CreateHardenedReader` sets both `DtdProcessing = Prohibit` and
`XmlResolver = null` (`MspdiXmlReaderFactory.cs:27-36`). A repo-wide grep for
`XDocument`/`XmlDocument`/`XmlReader.Create`/`XPathDocument`/`XmlSerializer`/`XslCompiledTransform`/
`XmlTextReader` across all of `backend/` returns exactly **two** hits in production code —
`MspdiXmlReaderFactory.cs:36` and `MspdiScheduleParser.cs:56-60`, which consumes the hardened
reader. (The only other hit is `LayeringTests.cs:63`, reading `.csproj` files at test time.)
There is no second XML entry point and therefore no bypass. `HardeningTests.cs:37-102` covers
external-entity, declared-but-unreferenced-entity, and billion-laughs cases; `DtdProcessing.Prohibit`
forecloses entity expansion structurally, so `MaxCharactersFromEntities` is not needed
(`MaxCharactersInDocument` is a separate defence-in-depth gap — L-02).

### 5c. Excel formula-injection coverage on export — MET (complete)

`ExcelProgressTemplateWriter` writes exactly seven columns. Every one was checked, not just the
tested two:

| Column | Type written | Escaped? |
| :-- | :-- | :-- |
| ActivityId | `Guid.ToString()` | N/A — cannot begin `=`/`+`/`-`/`@` |
| ActivityCode | free text | **Yes** — `EscapeForExport` (line 57) + column `NumberFormat = "@"` (line 49) |
| ActivityName | free text | **Yes** — `EscapeForExport` (line 58) + `"@"` (line 50) |
| CurrentProgressPercentage | `decimal` | N/A — numeric cell |
| PeriodEndDate / ProgressPercentage / ActualQuantity | left blank | N/A |
| Header row | fixed literals (line 30-34) | N/A |

Both free-text fields — the only attacker-influenceable ones — are covered. There is no other
export path in the codebase (no CSV/PDF export yet), so coverage is complete for Sprint 3.

I also checked the leading-whitespace bypass (`" =cmd"`): Excel does not evaluate a
leading-space cell as a formula, the column is Text-formatted, and the *importer* trims before
testing `StartsWithFormulaTrigger` (`ExcelProgressImporter.cs:176, 204, 219`) so it is stricter
than the export. No gap today.

**Forward-looking note (not a finding):** `FormulaInjectionGuard.DangerousLeadingCharacters`
(`FormulaInjectionGuard.cs:14`) covers `= + - @`, which is correct for XLSX. When a **CSV**
export lands in a later sprint, the set must be extended to leading TAB (`\t`) and CR (`\r`),
which Excel strips before formula evaluation in CSV context only.

### 5d. Error leakage — OK

`GlobalExceptionHandler.cs:39-41` returns a fixed string for 500s and never echoes
`exception.Message`, SQL, or stack traces. `ResultProblemMapper` emits only stable error codes.
Import rejections carry parser messages (line/UID/activity code) in `ErrorJson`, which is
intended user-facing diagnostic detail, not internal state. Confirmed no path leaks host paths
or connection strings.

---

## Findings

### High — must be fixed before Sprint 3 closes

**H-01 · Quadratic WBS re-parenting in the XER parser — single-request CPU exhaustion**
`backend/src/CMPlus.Infrastructure/Parsers/Xer/XerScheduleParser.cs:86-94`

```csharp
foreach (var node in wbsNodes)
{
    var externalId = wbsExternalToInternal.First(kv => kv.Value == node.Id).Key;   // O(n) per node
    var parentExternalId = wbsParentByExternalId.GetValueOrDefault(externalId);
    if (parentExternalId is not null && wbsExternalToInternal.TryGetValue(parentExternalId, out var parentInternalId))
    {
        node.SetParent(parentInternalId, wbsNodes.ToDictionary(n => n.Id));        // O(n) alloc per node
    }
}
```

Two independent O(n) operations run once per node, so the pass is O(n²) in time *and* allocates a
fresh n-entry `Dictionary` per node (LOH churn at scale). `WBSNode.SetParent` then walks the
ancestor chain, adding a third factor on deep trees.

**Measured** against the real `XerScheduleParser` (Release build, synthetic XER, flat depth-2 tree
— i.e. an entirely *legitimate* file shape, not a crafted one):

| PROJWBS rows | File size | Elapsed |
| --: | --: | --: |
| 8,000 | 221 KB | 1,158 ms |
| 16,000 | 463 KB | 5,713 ms |
| 24,000 | 711 KB | 14,298 ms |

Growth exponent ≈ 2.35 (a deep-chain tree is worse: 24,000 rows → 34,284 ms at 796 KB).

**Attack scenario.** Any authenticated user POSTs one XER file of ~750,000 PROJWBS rows — about
24 MB, comfortably under the 50 MiB `MaxFileSizeBytes` cap, and easily generated. Extrapolating
the measured curve, that single request consumes on the order of tens of hours of CPU on one
core. Parsing is fully synchronous inside the request, holds a thread pool thread for the
duration, honours no `CancellationToken` (the token is never threaded into the parser), and has
no timeout. A handful of concurrent such requests renders the API unavailable; there is no rate
limiting on the endpoint (inherits Sprint 2 M-03). The uploader does not even need to wait for a
response.

This also means the size cap alone does **not** bound import work, and it independently threatens
the S3-DB-01 DoD ("10,000 activities + 15,000 relations within the recorded perf budget") for any
WBS-heavy real file — at 10,000 WBS nodes the re-parent pass alone costs seconds.

**Fix.**
1. Build the reverse map once before the loop
   (`var externalIdByInternalId = wbsExternalToInternal.ToDictionary(kv => kv.Value, kv => kv.Key);`)
   and hoist `var nodesById = wbsNodes.ToDictionary(n => n.Id);` out of the loop, rebuilding
   nothing per iteration. This makes the pass O(n) (plus the ancestor walk) with no behaviour
   change.
2. Add an entity-count cap alongside the byte cap — e.g. `ImportOptions.MaxEntityCount` checked
   against `PROJWBS`/`TASK`/`TASKPRED` row counts after tokenisation and before graph
   construction — so that no future O(n·k) step can be driven arbitrarily by a within-cap file.
3. Thread the handler's `CancellationToken` into `IXerScheduleParser.Parse`/`IMspdiScheduleParser.Parse`
   so a client disconnect or request timeout actually stops the work.

**QA follow-up:** add a scaling regression test asserting the pass stays sub-linear-squared (e.g.
parse time for 20,000 WBS nodes stays within a fixed budget), so the algorithm cannot silently
regress.

### Medium

**M-01 · Parser exceptions are not caught: five proven single-request paths to HTTP 500 with no
`FileImportJob` row and no audit entry**

Neither `ImportScheduleFileCommandHandler` (line 68-70) nor `ImportExcelProgressCommandHandler`
(line 46) wraps the parser call in a `try`/`catch`. Both classes' own doc comments state the
contract that *any* rejected file "is still a `FileImportJobDto` whose `status` is `Failed`, not
an HTTP-level error" — that contract is currently violated for every exception the parsers do not
convert to a `Result.Failure` themselves. Confirmed by execution against the real assemblies:

| # | Location | Trigger | Thrown |
| :-- | :-- | :-- | :-- |
| 1 | `ExcelProgressImporter.cs:48` — `new ExcelPackage(excelContent)` | any non-OOXML or password-protected file on the `xlsx` route | `System.IO.InvalidDataException` |
| 2 | `ExcelProgressImporter.cs:171` — `DateTime.FromOADate(oleAutomationDate)` | numeric `PeriodEndDate` cell out of OLE range (e.g. `1e30`) | `System.ArgumentException: Not a legal OleAut date.` |
| 3 | `ExcelProgressImporter.cs:198-200` — `(decimal)doubleValue` | numeric `ProgressPercentage`/`ActualQuantity` cell of `1e30`, `NaN`, or `Infinity` | `System.OverflowException` |
| 4 | `XerScheduleParser.cs:154` — `(int)Math.Round(durationHours / 8m, …)` | `target_drtn_hr_cnt` = `99999999999999999999` | `System.OverflowException` |
| 5 | `XerScheduleParser.cs:205` — `(int)Math.Round(lagHours / 8m, …)` | `lag_hr_cnt` = `99999999999999999999` | `System.OverflowException` |

For #2 and #3, the crafted `.xlsx` was written with EPPlus, saved, reloaded, and the cells
confirmed to materialise as `System.Double` — i.e. the importer's `is double` branches are
genuinely reached; this is not a theoretical cast.

**Impact.** One request produces a 500 instead of the modelled `Failed` job. The failure is
invisible in job history (the DoD's "history, not fire-and-forget" guarantee silently does not
hold for this whole class), no audit row is written (contradicting CLAUDE.md's "every mutating
domain operation writes an audit log entry"), and `GlobalExceptionHandler.cs:22` logs a full
stack trace at `Error` level on every attempt — a cheap log-flooding and alert-fatigue vector.
The response body itself does not leak internals (verified), so this is not information
disclosure. Trigger #1 is reachable by an ordinary honest user with a protected workbook.

**Fix.** Two layers:
1. Guard the specific conversions — bounds-check before `FromOADate` (valid range is
   `-657435.0 < oa < 2958466.0`), range-check before `(decimal)` (reject `NaN`/`±Infinity` and
   values outside decimal range), and range-check before `(int)` in both XER call sites — each
   returning the existing `MalformedFile` failure with a row/field reference.
2. Wrap the parse call in each command handler in a `try`/`catch` that marks the job `Failed`
   with a generic `ImportErrorCodes.MalformedFile` (never the exception text), so anything
   unforeseen still lands as a queryable, audited `Failed` job — which is what both handlers
   already claim to guarantee.

Adding the magic-byte check from §2 removes trigger #1's most common cause but does **not**
substitute for either layer.

**M-02 · Up to 200 MB is buffered into the managed heap before the 50 MiB cap is applied**
`backend/src/CMPlus.WebApi/Controllers/Import/ImportController.cs:27, 34, 43-45`

`[RequestSizeLimit(100 MB)]` (line 34) raises the ceiling well above the configured 50 MiB
`MaxFileSizeBytes`, and the action then copies the whole upload into a `MemoryStream` and calls
`.ToArray()` — a second full copy — *before* the cap is checked, which happens downstream in the
command handler. A 100 MB upload therefore costs ~200 MB of managed allocation (both arrays on
the Large Object Heap, with `MemoryStream` doubling pushing the transient peak higher) purely to
be rejected. Concurrent requests multiply this directly.

**Fix.** Check `file.Length` against `IImportOptionsProvider.MaxFileSizeBytes` in the controller
*before* `CopyToAsync` — `IFormFile.Length` is known at action-invocation time — and return the
same `Failed`-job outcome. Then either derive `MaxRequestBodyBytes` from the configured cap or
document why the ceiling is deliberately higher. Replacing `MemoryStream` + `ToArray()` with a
single pre-sized `byte[]` rented from `ArrayPool` (or passing the `IFormFile` stream straight
through) removes the second copy.

**M-03 · Known-vulnerable transitive dependency: `Microsoft.OpenApi` 2.0.0 (High)**
`backend/src/CMPlus.WebApi/CMPlus.WebApi.csproj:6`

`dotnet list package --vulnerable --include-transitive` reports, for `CMPlus.WebApi`,
`CMPlus.Integration.Tests`, and `CMPlus.Architecture.Tests`:

```
> Microsoft.OpenApi   2.0.0   High   https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
```

Pulled transitively by `Microsoft.AspNetCore.OpenApi 10.0.10`. Reachability in production is low
— CM+ only *generates* an OpenAPI document, and `MapOpenApi()` is gated to Development
(`Program.cs:116-119`) — but the vulnerable assembly ships in the runtime image, and ADR-0010's
CI gate requires a clean vulnerable-package scan. This has been outstanding since Sprint 1 and
was previously recorded as "non-blocking" in `lessons-learned.md`; that framing should be
retired.

**Fix.** Add a direct pin to a patched 2.x (the 2.x line currently extends to 2.4.x) in
`CMPlus.WebApi.csproj` — exactly the pattern already used successfully for
`System.Security.Cryptography.Xml` in `CMPlus.Infrastructure.csproj:34-35`, which now resolves
clean. Re-run `dotnet list package --vulnerable --include-transitive` and confirm zero findings
across all projects.

**M-04 · No role check on the import endpoints — any authenticated user can rewrite a project's
schedule**
`backend/src/CMPlus.WebApi/Controllers/Import/ImportController.cs:23`

`[Authorize]` with no roles or policy. `UserRole` has seven values
(`PM/Planning/Site/QS/Executive/Admin/ProjectDirector`), and every one of them can currently
POST an XER/MSPDI file that bulk-inserts WBS nodes, activities and relations into any project in
their tenant, or POST an XLSX that appends `ActivityProgressLog` rows. Progress is the input to
EV, which feeds EVM, S-Curve and ultimately payment certification (ADR-0009) — so a `Site` user
can move numbers that money is calculated from, with no approval step. Compare
`TenantApprovalPoliciesController.cs:22`, which does gate on `Roles = nameof(UserRole.Admin)`.

This is not an implementation defect against a written spec: `docs/10.` §6 S3-BE-04's DoD
specifies only tenant binding, and no import permission matrix exists in the docs or knowledge
base. It is a genuine broken-access-control gap that needs a product decision.

**Fix.** `po-analyst`/`domain-expert` decide which roles may import schedules (likely
`PM`/`Planning`/`Admin`) versus progress (likely additionally `Site`/`QS`); then enforce with
distinct `[Authorize(Roles = ...)]` or a named policy per action — Application-layer enforcement,
not UI-only. Add negative integration tests per role. Re-audit at the S15 full-RBAC pass.

### Low (tracked, non-blocking)

| ID | Finding | Fix |
| :-- | :-- | :-- |
| L-01 | `FileImportJob.FileName` takes `IFormFile.FileName` verbatim (`ImportController.cs:50/52/54` → `FileImportJob.cs:55-57`) with no length bound, while the column is `nvarchar(250)` (`FileImportJobConfiguration.cs:17`). A >250-char filename — trivially sent by a scripted client — raises `DbUpdateException` ("String or binary data would be truncated") on SQL Server → 500 and no job row. Invisible to the existing tests, which use the EF Core InMemory provider (no length enforcement). No traversal impact (§4). | In the controller or entity: `Path.GetFileName()`, strip control characters, truncate to 250 with an ellipsis. Add a SQL Server-backed or explicit-length test. |
| L-02 | `MspdiScheduleParser.cs:56-60` materialises a full `XDocument` DOM from up to 50 MiB of XML — typically 5-10× memory amplification (~250-500 MB managed per request). `XmlReaderSettings.MaxCharactersInDocument` is not set. | Set `MaxCharactersInDocument` in `MspdiXmlReaderFactory` proportional to `MaxFileSizeBytes`; consider streaming (`XmlReader` forward-only) for the Tasks pass. |
| L-03 | `ImportRepository.cs:125` — `Skip((page - 1) * pageSize)` overflows `int` for a large `page` (e.g. `?page=2147483647`), yielding a negative `OFFSET` → SQL error → 500. `GetImportJobHistoryQueryHandler.cs:14-15` clamps `pageSize` but not `page`'s upper bound. | Clamp `page` to a sane maximum, or compute the skip in `long` and reject overflow. |
| L-04 | `DependencyInjection.cs:38` binds `ImportOptions` with no `.Validate(...).ValidateOnStart()`, unlike `JwtOptions` (lines 57-64, added by Sprint 2's M-02). A misconfigured `MaxFileSizeBytes: 0` fails closed but silently rejects every import with a confusing message. | Add `.Validate(o => o.MaxFileSizeBytes > 0 && o.MaxDecompressedSizeBytes >= o.MaxFileSizeBytes, ...).ValidateOnStart()`. |
| L-05 | `ImportController.cs:27` — `MaxRequestBodyBytes` is a hardcoded 100 MB const. If an operator raises `Import:MaxFileSizeBytes` above it, uploads fail at the framework layer with an opaque 413 rather than the modelled `FileTooLarge` job. | Derive the attribute value from configuration, or assert the relationship at startup alongside L-04. |
| L-06 | `ExcelPackageLicense` still runs on the EPPlus Polyform **Noncommercial** license (`appsettings.json` `Excel:NonCommercialOrganizationName`). A commercial key is required before production. Legal/compliance, not security — already flagged in-code and repeated here so it is not lost at the S16 production gate. | Set `Excel__CommercialLicenseKey` from a real license before production sign-off. |
| L-07 | No rate limiting on the import endpoints, which materially compounds H-01 and M-01. Inherits Sprint 2 finding M-03 (deferred to Sprint 15). | Include the import endpoints explicitly in Sprint 15's rate-limiting design, not just `/auth/login`. |

---

## Re-verification (orchestrator, 2026-07-29, same day) — all items closed

All five re-verification requirements below were checked against the actual fixed code, not
assumed from the implementer's own report:

1. **H-01 fixed and re-measured.** `XerScheduleParser.cs`'s WBS re-parenting now hoists both
   lookups (`externalIdByInternalId`, `nodesById`) once before the loop instead of rebuilding them
   per node. Re-ran the scaling regression test
   (`XerScheduleParserPerformanceTests.Parsing_20000_Flat_Wbs_Nodes_Completes_Well_Under_A_Generous_Time_Budget`)
   myself: **20,000 WBS nodes parse in ~65 ms**, down from the original measurement of 14.3 s for
   just 24,000 rows — roughly a 200×+ improvement, confirming the O(n²)→O(n) fix is real, not
   just asserted. The entity-count cap (`ImportOptions.MaxEntityCount`, default 50,000) is also
   confirmed present and tested at the boundary.
2. **M-01 fixed.** Read `ImportScheduleFileCommandHandler.cs` and `ImportExcelProgressCommandHandler.cs`
   directly: both now wrap the parser call in a `try`/`catch` safety net (never echoing the
   exception message), and the five specific triggers (`DateTime.FromOADate` range, `(decimal)`
   NaN/Infinity/overflow, two XER `(int)` cast sites) are now bounds-checked before the unsafe
   cast is ever attempted, confirmed in `ExcelProgressImporter.cs` and `XerScheduleParser.cs`
   (the shared `TryConvertHoursToWholeDays` helper).
3. **DoD item 2 (magic-byte check) closed.** Confirmed `FileSignatureValidator.IsXer`/`IsMspdi`/`IsXlsx`
   are called in both command handlers before any parser runs, each producing a `FormatMismatch`
   failed job rather than a parser-level exception.
4. **M-02, M-03, M-04 resolved:**
   - M-02: `ImportController.cs` now checks `file.Length` against `MaxFileSizeBytes` before
     `CopyToAsync`, confirmed in the controller source.
   - M-03: `System.Security.Cryptography.Xml` (found separately, same class of issue) and
     `Microsoft.OpenApi` are both directly pinned to patched versions — confirmed via
     `dotnet list package --vulnerable --include-transitive`: **zero vulnerable packages across
     all 8 projects** in the solution.
   - M-04: `ImportController.cs` now enforces `ScheduleImportRoles`
     (PM/Planning/Admin) and `ProgressImportRoles` (PM/Planning/Site/QS/Admin) — an interim policy,
     explicitly pending formal po-analyst/domain-expert review, not final product policy.
5. **Full re-run, real numbers:** `dotnet build backend/CMPlus.sln` — 0 errors, 0 warnings.
   `dotnet test` — **260/260 passing** at the time these fixes landed (94 Domain + 29 Application
   → grew further in Sprint 4). `dotnet list package --vulnerable --include-transitive` — zero
   findings, all 8 projects. Secret scan (gitleaks) — clean.

Findings marked "verified by execution" in the original review above were reproduced in
throwaway probe projects under the session scratchpad referencing the real
`CMPlus.Infrastructure` assembly and EPPlus 8.5.4 — that methodology is unchanged; this
addendum re-verifies the *fixes* the same way the original review verified the *findings*.
