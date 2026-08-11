# Security Review — Sprint 12 (S12-SEC-01)

**Scope:** photo upload & serving (S12-BE-01), offline photo outbox (S12-FE-01).
**Reviewer:** `security-auditor` · **Date:** 2026-08-11
**DoD under review (`docs/10.` §8, S12-SEC-01):** *"ยืนยัน: ไม่มี content sniffing ที่อันตราย, URL รูปไม่ใช่ guessable แบบเข้าถึงข้าม tenant ได้, EXIF ถูกลบ, ข้อมูลบนอุปกรณ์ (IndexedDB) ไม่เก็บ token/PII เกินจำเป็น"*

> **CURRENT STATUS (2026-08-11, after the fix cycle): PASS — Sprint 12 can close.**
> H-01 and H-02 were both fixed and independently re-verified by execution; all four recommended
> Lows are closed; N-01, opened by the re-verification, has since been fixed and tested. See **§9**,
> which is the authoritative current verdict, and **§10** for N-01's closure. Everything from here to
> §8 is the *original* review that found the two Highs, kept verbatim as the record of what was
> wrong and why — do not read the original verdict line below as the present state.
>
> **Original verdict (first pass): FAIL — 2 High, 4 Medium, 8 Low. 0 Critical.**
> Checklist items 1 and 2 were met. Item 3 (**EXIF stripped**) was **not met** — proven on stored
> bytes, not on the code path. Item 4 was met for **tokens** and **not met** for **PII on a shared
> device**. Neither High was a design flaw; both were bounded, local fixes.

---

## Method, and its limits

Findings were hunted by **probing the real assemblies**, in the same way as sprints 9 and 10 — never
by reading the implementation's own tests. Throwaway harnesses in the session scratchpad, outside the
repository, drove:

- the real `ExifScrubber`, `ImageSignatureValidator`, `UploadPhotoCommandHandler`,
  `GetPhotoContentQueryHandler`, `PhotoRepository`, `CmPlusDbContext` (with the three production
  interceptors attached), `LocalDiskFileStorage` **against a real local disk**, and the real
  `ProjectPhotosController` + `FileContentResultExecutor`;
- the real `outboxStore.ts` / `syncEngine.ts` / `storage.ts` on `fake-indexeddb`, via `vitest` rooted
  outside the repository.

`git status --porcelain` = **249 entries before and after**. No repository file was created,
modified or deleted.

**Environment limits (Docker cannot start — no SQL Server, no running API):** everything below marked
*execution-verified* ran against the real production classes on EF Core InMemory + real disk.
Everything marked *code-verified* did not run. Nothing HTTP-transport-level was exercised. See §7.

### Toolchain, re-run at review time

| Command | Result |
| :-- | :-- |
| `dotnet build backend/CMPlus.sln -c Release` | **0 Warning(s), 0 Error(s)** |
| `dotnet test tests/CMPlus.Domain.Tests` | **Passed! 370/370**, 0 skipped |
| `dotnet test tests/CMPlus.Application.Tests` | **Passed! 641/641**, 0 skipped |
| `dotnet test tests/CMPlus.Architecture.Tests` | **Passed! 14/14**, 0 skipped |
| `dotnet test tests/CMPlus.Integration.Tests` | **Passed! 472/472**, 0 skipped |
| `dotnet list CMPlus.sln package --vulnerable --include-transitive` | **no vulnerable packages**, 8/8 projects |
| `npm run lint` (`web/`) | clean |
| `npm run build` (`web/`) | clean (chunk-size advisory only) |
| `npx vitest run` (`web/`) | **1130 passed \| 1 skipped** (157 files passed, 1 skipped) |
| `npm audit --omit=dev` (`web/`) | **0 vulnerabilities** |
| `npm audit` (incl. dev) | **2 High** — build-time only, see **L-08** |

The S12-QA-01 Playwright suite (12/12) was **not** re-run — it needs a running app (§7).

---

## 1. The four DoD items

### 1.1 "ไม่มี content sniffing ที่อันตราย" — **PASS**

Execution-verified end to end through the real controller and the real `FileContentResultExecutor`:

```text
result type      : FileContentResult
Content-Type     : image/jpeg
FileDownloadName : 019fef1e201f7486b371f86f04fbaf21.jpg
response header  : X-Content-Type-Options: nosniff
response header  : Content-Type: image/jpeg
response header  : Content-Disposition: attachment; filename=019fef…21.jpg; filename*=UTF-8''019fef…21.jpg
```

- The format is decided **only** by `ImageSignatureValidator` magic bytes. A PHP payload, a `GIF89a`
  file and an SVG were all rejected `PhotoUnsupportedFormat`; the client filename and multipart
  `Content-Type` never reach the handler (`UploadPhotoCommand` carries neither).
- `Photo.ContentType` is a computed projection of a closed two-value enum — there is no stored
  free-text content type to poison.
- The download name is `{photo.Id:N}.{jpg|png}`, derived from the id, so no attacker-controlled
  string can reach `Content-Disposition`.

**Caveat, tracked as M-02, not as a failure of this item:** the "an uploaded file that renders as HTML
in the user's origin is stored XSS" defence is currently carried entirely by two headers hand-set on
this one action, and the stored bytes are genuinely attacker-influenced (H-01). That holds today. It
stops holding the moment ADR-0010's S3/CDN adapter or any pre-signed URL ships.

### 1.2 "URL รูปไม่ใช่ guessable แบบเข้าถึงข้าม tenant ได้" — **PASS**

Execution-verified against the real handlers and `CmPlusDbContext`:

```text
B1  t1 -> own project             : ok=True
B2  t1 -> t2 projectId            : ok=False PhotoProjectNotFound
B3  t1 tags t2 activityId         : ok=False PhotoUnknownActivity
B14 t2 GETs t1 photo, right ids   : ok=False PhotoNotFound
B15 t1 GETs own photo, wrong proj : ok=False PhotoNotFound
```

**On the question actually asked — is the id doing any work, or is authorization the only thing
standing up?** Authorization is the only thing standing up, and for this design that is *sufficient*.

- `Guid.CreateVersion7()`'s first 48 bits are a unix-ms timestamp and move predictably
  (execution-verified: two ids 3 ms apart share their prefix). ~74 bits remain random — still far
  beyond brute force, but that is not what protects anything here.
- What protects the bytes is: `Photo : ITenantOwned` picked up by `CmPlusDbContext`'s reflective
  `ApplyTenantQueryFilters`, **plus** the explicit `photo.ProjectId != request.ProjectId` check
  (`GetPhotoContentQueryHandler.cs:22`), **plus** `[Authorize]` on the action. A cross-tenant id, an
  unknown id, and a same-tenant-wrong-project id are **indistinguishable** — one bare
  `PhotoNotFound` → the generic `not-found` ProblemDetails.
- There is no unauthenticated path to bytes, no signed URL, no static-file passthrough (no `wwwroot`,
  no `UseStaticFiles()` anywhere), and `StorageKey` is never returned to a client.

So the id is an opaque handle, not a capability. That is the right design **as long as it stays that
way** — the risk to record now is that a pre-signed-URL adapter would silently convert the id into a
bearer capability with neither the tenant filter nor the project check in the path (M-02).

Also accepted, not a finding: `GET` is `[Authorize]` with **no role restriction**, so any
authenticated user in the tenant can read any project's photos in that tenant. There is no
project-membership model anywhere in `backend/src` (verified), and this matches
`ProjectWeatherLogsController`'s established "read wider than write". It is a deliberate, consistent
posture, not an oversight — but it means the photo store's real granularity is *tenant*, not project.

### 1.3 "EXIF ถูกลบ" — **FAIL** (H-01)

The scrubber's guarantee is structurally limited to the region *before* the first scan. Verified on
**stored bytes read back off disk after a real `UploadPhotoCommandHandler` run**:

```text
B13 STORED BYTES (98B) : GPSbeforeSOS=False  GPSafterSOS=True  IMEI=True  script=True
```

See H-01 for the full evidence.

### 1.4 "ข้อมูลบนอุปกรณ์ (IndexedDB) ไม่เก็บ token/PII เกินจำเป็น" — **PASS on tokens, FAIL on PII**

**Tokens: clean, and `authStore.ts` has not regressed.** Verified by inspection of every write into
the outbox and by dumping a real stored record:

- `OutboxItem` carries `id`, `kind`, `idempotencyKey`, `payload`, `blob`, `blobFileName`,
  `blobContentType`, `status`, `attemptCount`, `lastError`, timestamps, `serverId`. **No access
  token, no refresh token, no `userId`, no `tenantId`, no role, no GPS.**
- `PhotoOutboxPayload` = `projectId`, `fields{activityId, caption, capturedAt}`, `fileName`. The app
  never calls `navigator.geolocation` anywhere (repo-wide grep: zero hits).
- `authStore.ts` is still in-memory only — no `persist`, no `localStorage`/`sessionStorage`. The one
  `localStorage` user in the app is `projectStore.ts` (last-selected project id), unchanged.
- The `Idempotency-Key` header is minted client-side by `generateOutboxId` and is not a credential.

**PII: fails, on the shared-device axis the checklist names.** See H-02 and L-06. Summarised:
the outbox is a single unscoped device-wide queue that survives logout, renders previous users'
photos as thumbnails, and never purges.

**Retention after successful sync — partially good, incomplete.** `markSynced` correctly drops the
`blob` (execution-verified: `blob: null`), which is the biggest item. But the rest of the record is
kept forever, with no purge, no TTL and no logout hook:

```text
P2 record left in IndexedDB after a SUCCESSFUL sync:
{"id":"342e…","kind":"photo","idempotencyKey":"a167…","payload":{"projectId":"P",
 "fields":{"activityId":"ACT-1","caption":"crack in beam B12, worker somchai injured here",
 "capturedAt":"2026-08-11T03:00:00Z"},"fileName":"IMG_20260811_0930.jpg"},
 "blob":null,…,"status":"synced","serverId":"server-guid-123"}
```

A free-text Thai caption is exactly where a site engineer writes a person's name or an incident
detail, and `fileName` on a real phone encodes the capture timestamp. That is durable PII on a device
that may be shared or lost, with no way to clear it short of the browser's own site-data reset.

---

## 2. Findings — High (block sprint close)

### H-01 — EXIF/metadata stripping stops at the first SOS; everything after it is stored verbatim

`backend/src/CMPlus.Application/Photos/ExifScrubber.cs:137-144`

```csharp
if (marker == 0xDA) // Start Of Scan: entropy-coded data follows …
                    // copy the remainder of the file verbatim and stop.
{
    output.Write(content, offset, content.Length - offset);
    offset = content.Length;
    break;
}
```

The comment's premise — that after SOS there is "never another parseable marker segment" — is false
for progressive JPEG (multiple scans, with legal marker segments between them) and for every
container that appends data after the primary image's EOI. The `0xD9` branch above it (`:104-110`)
*does* truncate correctly, but it is unreachable for any file that has a scan, i.e. every real image.

**Execution-verified, against the real `ExifScrubber`:**

```text
A1  EXIF before SOS         : in=85B out=20B   Exif=False  GPS=False       <- works
A2  EXIF AFTER first SOS    : in=85B out=85B   Exif=True   GPS=True   IMEI=True
A2b EXIF between two scans  : GPS=True
A3  payload after EOI       : in=99B out=99B   "<script>" survives=True
A4  XMP before SOS          : survives=False   <- works
A4b XMP after SOS           : survives=True
```

**Execution-verified on the realistic, non-adversarial device shapes:**

```text
R2.1 MPF-shaped file (a second complete JPEG, with its own EXIF+GPS, after the primary EOI)
     primary EXIF stripped   : True
     secondary EXIF stripped : False        <- the GPS is still there
R2.2 motion-photo-shaped (MP4 trailer appended after EOI)
     appended MP4 survives   : True
```

**Execution-verified on the bytes actually written to disk**, through the real
`UploadPhotoCommandHandler` + `LocalDiskFileStorage`:

```text
B13 STORED BYTES (98B) : GPSbeforeSOS=False  GPSafterSOS=True  IMEI=True  script=True
B6  served bytes contain <script> : True
```

**Attack / failure scenarios**

1. *No attacker required.* A phone that writes a Multi-Picture-Format secondary image or a
   motion-photo trailer produces a file whose **second** EXIF block — with its own GPS — is kept.
   `Photo` is then a stored site coordinate the DoD says it must not be. (The exact behaviour of any
   specific handset was **not** tested here — no real device corpus in this environment — but the
   container shape is standard and the scrubber demonstrably preserves it.)
2. *Trivially attacker-driven.* Any authenticated `Site` user can skip the client compressor entirely
   (`POST /api/v1/projects/{id}/photos` with curl) and upload a JPEG with the metadata placed after
   the scan. The server-side control that the DoD relies on is bypassed with a byte reordering.
3. *Verbatim arbitrary storage.* The API stores attacker-chosen bytes unaltered in a file it labels
   `image/jpeg`. Today the serving path's `nosniff` + `attachment` contain the consequence (§1.1) —
   but the sanitiser is not sanitising, and M-02 is the reason that matters.

The client-side canvas re-encode (`compression.ts`) does produce metadata-free JPEGs, so the honest
in-app path is clean. That is a UI convenience, not the security boundary; the server is.

**Fix.** After emitting the SOS segment, continue parsing instead of bailing out. The entropy-coded
stream is byte-stuffed, so the next *real* marker is the first `FF xx` where `xx` ∉ {`00`, `D0`–`D7`,
`FF`}; scan forward to it and resume the existing filter loop, which already handles APPn/COM
correctly. Terminate at the first EOI and **discard everything after it**. (A cheaper variant —
truncate at the first EOI unconditionally — is correct for baseline JPEG and closes the whole class,
at the cost of deliberately destroying legitimate trailers; that is an acceptable trade for this
product and should be stated as such if chosen.) Add fixtures for: EXIF after SOS, EXIF between two
progressive scans, a second complete JPEG after EOI, and an arbitrary binary trailer.

### H-02 — the IndexedDB outbox is scoped to nothing: not a user, not a tenant, not a project, and nothing purges it

- `web/src/services/outbox/storage.ts:20` — one fixed database name, `cmplus-outbox`, opened with no
  argument by `usePhotoOutbox.ts:33`.
- `web/src/services/outbox/syncEngine.ts:60` — `store.pending()`, no kind and no owner filter.
- `web/src/services/outbox/outboxStore.ts:151-158` — `reconcileInterruptedSyncs` revives *every*
  stranded item, whoever queued it.
- `web/src/features/photo/usePhotoOutbox.ts:48-50` — `store.list(PHOTO_OUTBOX_KIND)`, no `projectId`.
- `web/src/features/photo/components/PhotoOutboxList.tsx:42-63` — renders each item's stored `Blob`
  as a thumbnail via `URL.createObjectURL`.
- `web/src/store/authStore.ts:76` — `logout: () => set({ ...emptySession })`. Memory only. Nothing in
  the repository ever calls `indexedDB.deleteDatabase` or clears the store outside tests.

**Execution-verified** against the real `createIndexedDbOutboxStorage` / `createOutboxStore` /
`createSyncEngine` on `fake-indexeddb`:

```text
P1 flush result       : {"synced":2,"failed":0,"skippedUnknownKind":0}
P1 uploaded by ONE flush (a single session/token): ["PROJECT-A idem=d244effd","PROJECT-B idem=45f888fe"]
P1 list() ignores projectId, returns : ["PROJECT-A/defect at column C4 - USER A PRIVATE",
                                        "PROJECT-B/user B photo"]
```

**Attack scenario (precondition: a shared browser profile — i.e. a site tablet, which is precisely
what ADR-0005 was written for).** Site engineer A captures photos offline on the shared tablet, loses
signal, hands the tablet over and signs out. Engineer B signs in. `PhotoPage` mounts →
`reconcileInterruptedSyncs` → `registerOutboxSyncTriggers` sees `navigator.onLine` → `syncNow()` →
`flush()` drains **A's** items. Three distinct consequences:

1. **Confidentiality.** Before any of that, `PhotoOutboxList` has already painted A's un-synced photos
   as live thumbnails, with A's Thai captions and activity ids, for B to read — across tenants, since
   nothing in the record identifies a tenant.
2. **Audit integrity.** The upload goes out on **B's** bearer token (`apiClient.ts:36` reads the
   current store). The server stamps `Photo.UploadedByUserId = B` and `AuditLog.UserId = B`.
   `conventions.md` requires an audit entry on every mutation; this one is written, and it names the
   wrong person. Nothing on the wire lets the server detect it.
3. **Availability / "รูปหาย".** If B is in a different tenant, every attempt returns
   `PhotoProjectNotFound` (execution-verified, B2) → `markFailed` → retried on every subsequent mount,
   forever, never succeeding. A's photos are lost while the UI shows a permanent red failure to a
   user who has no idea what they are.

**Fix.**
- Bind ownership to the record: store `ownerUserId` + `ownerTenantId` at `enqueue`, and make
  `pending()`/`list()`/`reconcileInterruptedSyncs` refuse anything whose owner ≠ the current session.
  (Namespacing the database name per user is the cheaper variant but leaks the previous user's id
  into `indexedDB.databases()`; the per-record field is better.)
- Clear or quarantine the queue on `logout` — at minimum drop `blob`s and hide the list.
- Give `synced` records a bounded retention (drop them once acknowledged, or after N days) instead of
  keeping caption/fileName/capturedAt indefinitely.
- Filter the displayed list by the route's `projectId` (L-06).

---

## 3. Findings — Medium

| ID | Finding | Fix |
| :-- | :-- | :-- |
| **M-01** | **`reconcileInterruptedSyncs` can replay an upload that already committed server-side, and nothing honours `Idempotency-Key` today.** `outboxStore.ts:151-158` moves stranded `syncing` items to `failed`; `syncEngine.flush()` then re-sends them. The key is correctly minted once at `enqueue` and **is** reused on retry (execution-verified: `P3 idempotency key reused on retry : 8e528f0e (minted at enqueue: 8e528f0e)`), so the client half is right. The server half does not exist — S13-BE-01's `IdempotencyMiddleware` is a *later* sprint than the feature that depends on it, and the header is silently ignored. Execution-verified consequence: `P3 server-side rows after replay : ["photo-created-by-attempt-1","photo-created-by-attempt-2 idem=8e528f0e"]` — a duplicate `Photo` row, which S12-QA-01's DoD ("ไม่มีรูปซ้ำ") forbids and which **no one can delete** (see M-04). The function is still a net improvement over the orphaned-forever state it replaced; the residual risk is real, is honestly documented in the code, and is now measured. | Ship S13-BE-01 in the same release as the outbox, or — cheaper interim — dedupe `Photo` on `(TenantId, ProjectId, IdempotencyKey)` with a unique index in the upload handler specifically, so the guarantee does not wait on the general middleware. |
| **M-02** | **The whole "no stored XSS" property rests on two headers hand-set on one action, and ADR-0010's future storage adapter will not inherit them.** `ProjectPhotosController.cs:111,113`. Today: correct and execution-verified (§1.1). But with H-01 unfixed the stored object genuinely contains attacker-chosen bytes, and ADR-0010 defers an S3-API-compatible adapter to after the Sprint 16 gate. The natural implementation of that adapter — a pre-signed object URL handed to the browser — takes **both** the tenant query filter and these headers out of the request path in one move, converting the photo id from an opaque handle into a bearer capability (§1.2). Sprint-02 M-04 (no CSP) and the absence of global security-header middleware are already tracked as **L-06** and are not re-reported here. | Record as a hard constraint on the future adapter, in the ADR: object responses must carry `Content-Type` from `Photo.Format`, `X-Content-Type-Options: nosniff` and `Content-Disposition: attachment`; a pre-signed URL must be short-lived and issued only after the same tenant+project check `GetPhotoContentQueryHandler` performs. Land the global header middleware so it is not per-endpoint discipline. |
| **M-03** | **`Storage:LocalRootPath` is unset in configuration and silently defaults to the OS temp directory.** `FileStorageOptions.cs:19` defaults to `Path.Combine(Path.GetTempPath(), "cmplus-file-storage")`; `appsettings.json`'s `Storage` section contains only a comment. On the Linux containers ADR-0010 targets that is `/tmp` — tenant photos land in ephemeral, world-traversable space, are lost on container restart, and nothing validates at startup that a real, persistent, access-controlled volume was configured. `LocalDiskFileStorage` is a singleton that `Directory.CreateDirectory`s whatever it is given. Compounding it: **`IFileStorage.DeleteAsync` has zero production callers** (repo-wide grep), and `UploadPhotoCommandHandler` writes the file *before* `SaveChangesAsync`, so a failed DB save leaves an orphan blob that nothing will ever reap. | Fail startup outside Development when `Storage:LocalRootPath` is not explicitly set; document the required volume mount in the compose/staging topology; add an orphan reaper, or write the `Photo` row first and the blob second. |
| **M-04** | **No erasure path for a photo, at all.** `ProjectPhotosController` exposes only `POST` and `GET`; there is no delete command, no soft-delete flag, and `IFileStorage.DeleteAsync` is uncalled. A site photo can contain faces, plates, or a whiteboard with personal data, so a Thai-PDPA erasure request cannot be satisfied; nor can a mis-uploaded or harmful image be taken down; nor can M-01's duplicate be removed. This is a product-scope gap rather than an exploit, but it is the incident-response half of storing user-generated binary content. | Product decision needed. Minimum: an authorised soft-delete that also calls `IFileStorage.DeleteAsync`, audited like every other mutation. |

---

## 4. Findings — Low (tracked, non-blocking)

| ID | Finding | Fix |
| :-- | :-- | :-- |
| **L-01** | `UploadPhotoCommandHandler.cs:56-60` decides the size cap from `request.DeclaredContentLength ?? request.Content.LongLength` — i.e. it prefers a **caller-supplied number** over the actual array. Execution-verified: an 11 MiB payload declared as 5 bytes sails past the cap (`B9 … ok=False PhotoMalformedImage` — rejected later by the scrubber, not by the size check). Unreachable through the controller today (`IFormFile.Length` is the true buffered length), but the code comment calls this "defense-in-depth", and a check that trusts the caller is not that. | `Math.Max(request.DeclaredContentLength ?? 0, request.Content.LongLength)`. |
| **L-02** | `ExifScrubber.cs:180-185` keeps an allow-listed PNG chunk by **type alone**, never validating its declared length against the spec and never checking the CRC. Execution-verified: a `pHYs` chunk (spec: exactly 9 bytes) carrying 42 bytes of GPS text is copied straight through — `A5c … payload survives=True`; and `A5d lying CRC : ACCEPTED`. So the PNG allow-list, which is otherwise the safer design, is a metadata smuggling channel. | Enforce the fixed/maximum data length per allow-listed type (`pHYs`=9, `gAMA`=4, `cHRM`=32, `sRGB`=1, `sBIT`≤4, `bKGD`≤6, `tRNS`≤256) and reject on CRC mismatch. |
| **L-03** | `LocalDiskFileStorage.cs:80` compares the resolved path to the root with `StringComparison.OrdinalIgnoreCase`. On a **case-sensitive** filesystem — the Linux containers ADR-0010 targets — that accepts an escape: execution-verified, root `…/Data` + key `../data/escaped.txt` → **ALLOWED**, writing into a genuinely different directory. Every real traversal key is blocked (`..\`, `../`, `a/../../`, absolute Windows and POSIX paths, embedded NUL — all `blocked`), and every current key is server-derived, so this is unreachable today. But the guard exists *precisely* for a future less-trusted caller, and it is weaker than it reads. | Compare `Ordinal` on non-Windows (or always, on normalized full paths). |
| **L-04** | `GetPhotoContentQueryHandler.cs:27` — a `Photo` row whose blob is missing from storage throws `FileNotFoundException` out of the handler. Execution-verified: `B7 row present, file deleted : THROWS FileNotFoundException -> 500`. The client sees only a generic ProblemDetails (`GlobalExceptionHandler` correctly suppresses the message for 500s, so **no path is leaked to the caller**), but "the object is gone" is reported as "the server is broken", and the storage key goes to the logs. Reachable in practice via M-03's ephemeral `/tmp` default. | Catch and map to `PhotoNotFound` (or a distinct `PhotoContentUnavailable`), and log at Warning with the photo id rather than the key. |
| **L-05** | `ProjectPhotosController.cs:111` uses `Response.Headers.Append(...)`. Once a global security-header middleware exists the value becomes `nosniff,nosniff` — execution-verified: `header value after Append: 'nosniff,nosniff' (count=2)`. Per the Fetch spec browsers take `values[0]`, so this is **not currently exploitable and will not become so**; it is a smell that will read as a bug the day the middleware lands. | Indexer assignment, not `Append`. |
| **L-06** | `usePhotoOutbox.ts:48-50` lists the whole device outbox regardless of the route's `projectId`, and `PhotoPage` presents it under the heading "คิวรูปภาพของอุปกรณ์นี้" on a project-scoped route. Execution-verified (P1). Sub-case of H-02, tracked separately because the display filter is an independent one-line fix that reduces exposure even before H-02's ownership model lands. | `store.list(PHOTO_OUTBOX_KIND)` → filter on `payload.projectId === projectId`. |
| **L-07** | A 6-byte "JPEG" — `FF D8 FF FE 00 02` (SOI + an empty COM segment, no image data whatsoever) — is accepted and stored. Execution-verified: `6-byte 'JPEG' (SOI+COM) : accepted=True storedFileSizeBytes=2 contentType=image/jpeg`. The scrubber also accepts "no SOS, no EOI" inputs. Harmless as served (attachment + nosniff), but the API will store non-images, and `Photo.FileSizeBytes` can be 2. | Require at least one SOS (JPEG) / IDAT (PNG) and a plausible minimum post-scrub size before persisting. |
| **L-08** | `npm audit` including dev dependencies reports **2 High** advisories — `brace-expansion` (GHSA-rgw5-rvv9-x895, DoS) and `nanoid <3.3.17` (GHSA-2v37-7h3g-55p8). Both are **build/test-time only**: `npm audit --omit=dev` is **0 vulnerabilities**, so nothing reaches the browser. Sprint 10 reported only the `--omit=dev` figure, so this is newly surfaced scope rather than a regression. NuGet is clean across all 8 projects. | `npm audit fix`; add the full `npm audit` (not only `--omit=dev`) to the ADR-0010 CI scan so the build toolchain is covered too. |

---

## 5. Areas explicitly checked and found sound

All execution-verified unless noted.

- **Tenant isolation on `Photo` (ADR-0002).** `Photo : ITenantOwned`, so `ApplyTenantQueryFilters`
  reaches it by reflection with no per-entity wiring. Cross-tenant read, cross-tenant upload and
  cross-tenant activity tagging all fail closed, and a cross-tenant id is indistinguishable from an
  unknown one.
- **Storage key derivation.** `Photo.cs:133` builds `{tenant:N}/{project:N}/{id:N}.{ext}` inside the
  constructor; `StorageKey` is not a constructor parameter and no client input can reach it. The
  original filename is not captured on the entity at all. `PhotoDto` deliberately omits `StorageKey`.
- **Path traversal.** Every hostile key tested was blocked (§L-03 lists them); only `ok.txt` and
  `sub/./ok.txt` wrote.
- **Malformed-input handling — no hangs, no memory-safety surprises, fails closed.** Twenty hostile
  inputs, every one either rejected with `ImageProcessingException` or accepted harmlessly; **no
  other exception type escaped**, no `OverflowException`, no unbounded allocation, no quadratic
  behaviour:

  ```text
  SOI + FFE1 only (no length)          -> ImageProcessingException: truncated segment length.   [0 ms]
  segment length 0xFFFF beyond EOF     -> ImageProcessingException: segment length exceeds file size. [0 ms]
  segment length 0 (< 2)               -> ImageProcessingException: invalid segment length.     [0 ms]
  non-marker where marker expected     -> ImageProcessingException: expected a marker.          [0 ms]
  10 MiB of 0xFF fill bytes            -> ImageProcessingException: truncated marker.           [6 ms]
  2,000,000 RSTn standalone markers    -> ACCEPTED out=4000002B                                 [9 ms]
  1,500,000 empty APP1 segments        -> ACCEPTED out=2B                                       [3 ms]
  PNG dataLength 0xFFFFFFFF            -> ImageProcessingException: chunk length exceeds file size. [0 ms]
  PNG dataLength 0x7FFFFFFF            -> ImageProcessingException: chunk length exceeds file size. [0 ms]
  PNG no IEND                          -> ImageProcessingException: missing IEND chunk.         [0 ms]
  900,000 zero-length PNG chunks       -> ACCEPTED out=20B                                      [15 ms]
  PNG chunk type with non-ASCII bytes  -> ACCEPTED out=20B                                      [0 ms]
  5 MiB scan copy                      -> in=5242894 out=5242894 ratio=1.000                    [3 ms]
  ```

  Both loops advance the offset monotonically by ≥ 2 on every iteration, the PNG chunk arithmetic is
  computed in `long` before the `checked((int))` narrowing, and the output can never exceed the
  input. The hand-rolled-parser risk this review was asked to probe is **real code, correctly
  bounded** — the defect (H-01) is a logic gap, not a memory-safety one.
- **PNG trailing data is correctly dropped** (`A5b data after IEND : survives=False`) and `eXIf` /
  `tEXt` are correctly stripped — the PNG path is right apart from L-02.
- **Null actor fails closed.** `PhotoActorRequired`, with the `is not { } uploadedByUserId` pattern —
  Sprint 10's L-01 lesson applied correctly on the first attempt rather than after review.
- **Audit completeness.** With the real `AuditSaveChangesInterceptor` attached, an upload writes
  `Photo:Created`. No sensitive field is written in cleartext (`RedactedPropertyNames` unaffected).
- **Size enforcement before buffering.** `ProjectPhotosController.cs:76` rejects on
  `IFormFile.Length` before `CopyToAsync`, and `[RequestSizeLimit(20 MiB)]` bounds the whole body
  above the 10 MiB per-file cap. `PhotoOptions.MaxFileSizeBytes` is read from configuration.
- **Caption bounded three ways:** FluentValidation `MaximumLength(500)`, a `DomainException` in the
  entity, and `nvarchar(500)` in the schema.
- **Frontend XSS surface: none.** Zero `dangerouslySetInnerHTML` / `innerHTML` / `eval` /
  `new Function` anywhere in `web/src`. Captions render as text nodes; thumbnails use
  `URL.createObjectURL` with `revokeObjectURL` on unmount/blob change.
- **Idempotency key discipline.** Minted once at `enqueue`, never regenerated by `markSyncing` /
  `markFailed` / `reconcileInterruptedSyncs` — execution-verified. The client half of M-01 is right.
- **Migration.** `20260810143814_Sprint12_Photo`'s generated T-SQL
  (`artifacts/migrations/20260810_sprint12_photo.sql`) matches the migration exactly: both
  tenant-leading composite indexes per `db-conventions.md` §2 rule 2, `CK_Photos_FileSizeBytes_Positive`,
  `nvarchar(400)` `StorageKey NOT NULL`, and no PII column beyond `Caption`.

---

## 6. Status of inherited open findings (sprint-10.md §10.9) — not re-reported

| ID | Sprint 10 status | Relevance to Sprint 12 |
| :-- | :-- | :-- |
| L-05 (`FallbackPolicy` on `AddAuthorization()`) | Open, third sprint | **Fourth sprint open.** `ProjectPhotosController` carries explicit `[Authorize]` on both actions, so the photo surface is not exposed by this gap — but the gap is now guarding two more endpoints by convention alone. |
| L-06 (no rate limiting; no CSP; EPPlus Polyform) | Open | **Aggravated, not re-reported.** The photo upload is the first endpoint that accepts a 10–20 MiB body from the broad `Site` role, with no rate limit and no per-tenant storage quota. The missing CSP is the second layer M-02 would otherwise lean on. |
| M-02, M-04 (Sprint 10) | Open, need product decisions | Unchanged; outside this review's scope. |
| N-06, N-07, N-08 | Open, Low | Unchanged; approval-routing scope, untouched by Sprint 12. |

Sprints **4–8 and 11 have never been security-reviewed.** Nothing in the adjacent code read during
this review looked acutely dangerous, but that is an observation from incidental reading, not a
review — and it means S15-SEC-01's full tenant-isolation sweep is now carrying seven unreviewed
sprints, not one.

---

## 7. What could not be verified without a running system

1. **No SQL Server.** The `Photos` migration is unapplied; the CHECK constraint, both indexes and the
   `nvarchar(400)`/`datetimeoffset` column types are verified only from the generated T-SQL, never
   executed. Every persistence result above ran on EF Core InMemory (see the 2026-08-10 lesson: treat
   InMemory as evidence about the C# logic only).
2. **No running API.** Not exercised: TLS/HSTS, CORS, cookie handling, response compression versus
   `application/problem+json`, real Kestrel multipart parsing (so `IFormFile.Length`'s exactness —
   which L-01 depends on — is inferred from the framework's implementation, not measured), the
   `[RequestSizeLimit]` rejection path, and concurrent-upload memory behaviour. The serving headers
   *were* driven through the real `FileContentResultExecutor`, which is the same code Kestrel invokes,
   but not over a socket.
3. **The S12-QA-01 Playwright suite (12/12) was not re-run** — it needs a running app. Its result is
   taken as reported, not verified. Note that it is a green suite sitting on H-01 and H-02, both of
   which it structurally cannot see: it exercises one user, one project, one browser profile, and
   asserts on the API response rather than on stored bytes.
4. **No real device corpus.** The MPF / motion-photo shapes in H-01 were synthesized to the documented
   container structure. That the scrubber preserves everything after the first SOS is
   execution-verified; that a *specific* handset emits such a file is expected, not tested.
5. **No S3/cloud adapter exists**, so M-02 is necessarily forward-looking and code-verified only.
6. **No live probing** — no timing analysis, no fuzzing at scale, no concurrency racing.

**H-01, H-02, M-01, L-01, L-02, L-03, L-04, L-05, L-06, L-07** are execution-verified.
**M-02, M-03, M-04** are code-verified. **L-08** is tool-verified.

---

## 8. Required before Sprint 12 can close

1. **Fix H-01** — continue scrubbing past the first SOS (or truncate at the first EOI), and discard
   trailing data. Add the four fixtures named in H-01, including one asserting on **stored bytes**,
   not on the return value of `Strip`.
2. **Fix H-02** — bind outbox records to an owner (user + tenant), refuse to list or flush another
   owner's items, and clear the queue on logout. L-06's project filter should land in the same change.
3. Re-run `dotnet build` (0 warnings) and `dotnet test` **per project**, plus both vulnerability
   scans, and report real numbers.
4. `security-auditor` re-verifies both by execution — specifically by re-reading the bytes on disk
   after a real handler run, and by re-running the two-owner outbox probe.

Recommended in the same pass, because they are small changes in code already being touched:
**L-01** (`Math.Max` on the size check), **L-02** (per-chunk length + CRC validation),
**L-04** (map the missing-blob case to 404), **L-05** (indexer instead of `Append`).
**M-01** should be scheduled against S13-BE-01 explicitly rather than left implicit.
**M-03** and **M-04** need decisions (`devops-engineer` and `po-analyst` respectively), not a rushed
patch.

ADR-0005 requires this review again if the outbox gains a second `kind` (S13-FE-01) — the ownership
model H-02 asks for must be in place *before* weather logs and progress batches join the same queue,
because those carry commercially sensitive data that photos do not.

---

## 9. Re-verification (S12-SEC-01-R) — 2026-08-11

**Reviewer:** `security-auditor` · **Trigger:** §8 item 4 · **Repo state:** unmodified
(`git status --porcelain` = **253** entries before and after; all harnesses in the session
scratchpad, outside the repository).

> **Verdict: H-01 PASS · H-02 PASS. L-01, L-02, L-04, L-05 all hold.**
> **Sprint 12 can close.** Two new findings opened by this pass: **N-01 (Medium)**,
> **N-02 (Low)**, plus **N-03 (Low)**. None is a High; none re-opens a DoD item.
> *(N-01 has since been fixed — see §10.)*

### 9.1 Toolchain, re-run at re-verification time

| Command | Result |
| :-- | :-- |
| `dotnet build backend/CMPlus.sln -c Release` | **0 Warning(s), 0 Error(s)** |
| `dotnet test tests/CMPlus.Domain.Tests` | **Passed! 370/370**, 0 skipped |
| `dotnet test tests/CMPlus.Application.Tests` | **Passed! 650/650**, 0 skipped (was 641) |
| `dotnet test tests/CMPlus.Architecture.Tests` | **Passed! 14/14**, 0 skipped |
| `dotnet test tests/CMPlus.Integration.Tests` | **Passed! 475/475**, 0 skipped (was 472) |
| `dotnet list CMPlus.sln package --vulnerable --include-transitive` | no vulnerable packages, 8/8 |
| `npm run lint` / `npm run build` (`web/`) | clean (chunk-size advisory only) |
| `npx vitest run` (`web/`) | **1156 passed \| 1 skipped** (159 files passed, 1 skipped) |
| `npm audit --omit=dev` | **0 vulnerabilities** |
| `npm audit` (incl. dev) | **2 High** — unchanged, build-time only (L-08) |
| `npx playwright test e2e/photo-offline.spec.ts` | **13 passed (25.1s)** |

**Correction to §7 item 3.** The Playwright suite *is* runnable in this environment. It stubs
`/api/v1/auth/login` and `/api/v1/projects/*/photos` with `page.route`
(`e2e/support/photoOffline.ts:150,176`) and `playwright.config.ts`'s `webServer` starts Vite
itself — no Docker, no SQL Server, no API container required. Its result is now
execution-verified rather than taken as reported. Everything else in §7 still stands.

### 9.2 H-01 — **PASS**

Every probe named in §8 re-run against the real scrubber:

```text
A1  EXIF before SOS         in=58  OK out=22  GPSLat=False IMEI=False Exif=False
A2  EXIF after first SOS    in=58  OK out=22  GPSLat=False IMEI=False Exif=False   <- was True/True/True
A2b EXIF between two scans  in=64  OK out=40  GPSLat=False Exif=False              <- was True
A3  payload after EOI       in=47  OK out=22  <script>=False                       <- was True
A4b XMP after SOS           in=76  OK out=22  GPSLatitude=False adobe.com=False    <- was True
R2.1 MPF secondary JPEG     in=99  OK out=22  SECONDARY=False PRIMARY=False GPSLat=False
R2.2 MP4 trailer after EOI  in=52  OK out=22  ftypmp42=False MOTIONPHOTO=False
```

**B13, re-read off disk after a real `UploadPhotoCommandHandler` run** — the assertion the
original tests got wrong, and the one §8 required:

```text
B13  EXIF AFTER SOS          STORED 22B  GPSLatitude=False IMEI=False <script>=False Exif=False
B13b EXIF between two scans  STORED 40B  GPSLatitude=False Exif=False
B13c trailer after EOI       STORED 22B  GPSLatitude=False <script>=False
B13d MPF secondary after EOI STORED 22B  GPSLatitude=False SECONDARY=False IMEI=False
B13e motion-photo trailer    STORED 22B  ftypmp42=False
B13f EXIF before SOS         STORED 22B  GPSLatitude=False Exif=False
                             (served == disk: True for every case; ct=image/jpeg)
```

Against the original `B13 STORED BYTES (98B): GPSbeforeSOS=False GPSafterSOS=True IMEI=True
script=True`. **DoD item 1.3 ("EXIF ถูกลบ") is now met, on stored bytes.**

**The continue-parsing choice was the right one**, and the reasoning for rejecting the cheap
unconditional truncate-to-EOI is correct as stated: A2b's fixture (EXIF *between* two progressive
scans) sits before EOI, so a blind truncate would have left it — confirmed by construction, `out=40`
for a 64-byte input means the middle APP1 was removed while both scans survived, which a truncate
could not have produced. MPF secondary images and motion-photo trailers are deliberately discarded,
and `ExifScrubber.cs:155-171` states that trade-off in the code.

**Scan-data fidelity through the new `FindEndOfEntropyCodedData` path:**

```text
stuffed FF00 preserved verbatim  : True   in=21 out=21
RSTn inside scan preserved       : True   in=21 out=21
FF fill run before EOI preserved : True   in=19 out=19
legit 3-scan progressive JPEG    : in=299 out=290  JFIF=False
```

**Malformed-input battery — re-established, not assumed.** The parse loop changed materially. No
exception type other than `ImageProcessingException` escaped; no `OverflowException`; no unbounded
allocation; no quadratic behaviour:

```text
SOI + FFE1 only (no length)          -> REJECT truncated segment length            [0 ms]
segment length 0xFFFF beyond EOF     -> REJECT segment length exceeds file size    [0 ms]
segment length 0 (< 2)               -> REJECT invalid segment length              [0 ms]
non-marker where marker expected     -> REJECT expected a marker                   [0 ms]
10 MiB of 0xFF fill                  -> REJECT truncated marker                    [6 ms]
2,000,000 RSTn standalone markers    -> OK  out=4000004                            [9 ms]
1,500,000 empty APP1 segments        -> OK  out=4                                  [3 ms]
300,000 SOS segments (new path)      -> OK  out=1800004                            [7 ms]
8 MiB of FF00 stuffing in one scan   -> OK  out=8388617                            [7 ms]
8 MiB of 0xFF inside a scan          -> OK  out=8388617                            [5 ms]
8 MiB of FF D0 restarts in a scan    -> OK  out=8388617                            [7 ms]
5 MiB single scan copy               -> OK  out=5242889                            [4 ms]
```

#### On the "SOS but no EOI now throws" question — **the right call, with one narrower caveat**

Endorsed, tested against genuine encoder output rather than only hand-rolled fixtures:

```text
real 1x1 JPEG (real encoder)  : OK in=160 out=142  JFIF-gone=True  endsWithEOI=True
real JPEG minus its final EOI : ImageProcessingException: scan data runs past end of file...
real JPEG truncated to 50%    : ImageProcessingException: segment length exceeds file size
```

No conformant encoder omits EOI, and `compression.ts`'s canvas re-encode always produces one, so the
only real-world inputs this catches are **truncated transfers and corrupt gallery files** — which
should be rejected loudly rather than stored as a broken `image/jpeg`. This strictly improves on
L-07's "the API will store non-images".

Caveat for the record: the guarantee is narrower than the phrase suggests. It fires only when the
file ends *inside* the entropy-coded scan. A file whose last bytes are a marker segment is still
accepted and yields EOI-less output (`SOS + scan + APP1, no EOI -> OK out=28`). Not a security gap;
recorded so the fail-closed claim is not overstated.

#### N-01 (Medium, NEW) — `FF D8` was still parsed as a length-bearing segment

`backend/src/CMPlus.Application/Photos/ExifScrubber.cs`

`StripJpeg` special-cased exactly the standalone markers `0x01` (TEM), `0xD0`–`0xD7` (RSTn) and
`0xD9` (EOI). It omitted **`0xD8` (SOI)**, which also has no length field — so on encountering a
second `FF D8` the parser read the following two bytes as a segment length, found `0xD8` was not a
metadata marker, and **wrote the marker plus that attacker-chosen number of bytes straight to the
output**. Execution-verified, then on stored bytes:

```text
FF D8 pseudo-segment smuggling   in=80  OK out=80  GPSLatitude=True IMEI=True <script>=True Exif=True
FF D8 smuggling AFTER a scan     in=98  OK out=98  GPSLatitude=True IMEI=True <script>=True
   output == input verbatim : True
   ImageSignatureValidator.DetectFormat(output) : Jpeg
B13g FF D8 pseudo-segment    STORED 98B  GPSLatitude=True IMEI=True <script>=True Exif=True
```

**Why Medium and not High.** No camera or encoder emits a nested SOI, so unlike H-01 there is no
"no attacker required" path — the non-adversarial half of H-01 is genuinely closed. The uploader
smuggles into their own tenant's photo, harming nobody but themselves, and "arbitrary bytes stored
verbatim" is partly inherent anyway (entropy-coded scan data is copied without decoding, by design).
What N-01 added over that is a **structured, `Exif\0\0`-shaped** blob a lenient metadata reader can
resync onto, where entropy-stream bytes are opaque to every EXIF parser.

**Fixed — see §10.**

### 9.3 H-02 — **PASS**

**P1, the two-owner probe:**

```text
P1 A list()                       : ["defect at column C4 - USER A PRIVATE","user A second project"]
P1 B list()                       : []
P1 B pending()                    : []
P1 B flush()                      : {"synced":0,"failed":0,"skippedUnknownKind":0} uploaded=[]
P1 B reconcileInterruptedSyncs()  : 0
P1 same-userId / other-tenant     : []           <- tenant is part of the match, not just user
P1 A signs back in, list()        : 2
P1 A flush()                      : {"synced":2,...} uploaded=["PROJECT-A/...","PROJECT-B/..."]
```

**P2, retention** (7-day window, device-wide/owner-agnostic):

```text
P2 deleted   : 3        (A-synced-8d, A-synced-exactly-7d, B-synced-30d)
P2 remaining : ["A-failed-90d","A-queued-90d","A-synced-1d","A-synced-badDate"]
```

Boundary is `>=`, a previous user's synced record is reaped even though they never sign back in,
`failed`/`queued` are untouched, and an unparseable `syncedAt` is kept rather than deleted.

**The three things they may not have probed:**

*Forged owner?* No. `enqueue` ignores every caller-supplied field outside `EnqueueOutboxItemInput`:

```text
P1d payload carrying ownerUserId:'user-B', id:'chosen-id', status:'synced'
    -> owner=["user-A","tenant-1"]  id-honoured=false  key-honoured=false  status="queued"
```

*Quarantine on the forced-401 path?* Yes — it is a store subscription, so it fires for **any** caller
of `logout()`, and `apiClient.ts:58` calls exactly that:

```text
Q2 blob before logout : true    Q2 blob after logout : NULL (quarantined)
Q2 metadata survives  : {"projectId":"P","fields":{"caption":"A unsynced"}}
Q2 list() no session  : []      Q2 B signs in, list() : []
Q1 cleared : 2   photo(user-A) blob=NULL · weather-log(user-A) blob=NULL · photo(user-B) blob=13B
```

Reaching a second `kind` is the ADR-0005 precondition for S13-FE-01.

*A window between login and the first `getOwner`?* No — `getOwner` is re-resolved on every operation:

```text
P1e enqueue with no session : OutboxOwnerRequiredError
P1e list()  with no session : []
P1c 5 legacy/malformed owner rows written directly to IndexedDB -> list() for A : []
```

#### N-02 (Low, NEW) — `quarantineOwnerBlobs` can lose a concurrent `markSynced`

`web/src/services/outbox/outboxMaintenance.ts:38-45`. The sweep does one `storage.list()`, then a
`put({...item, blob: null})` per record — a read-modify-write over a stale snapshot with no
transaction. If an in-flight upload's `markSynced` commits inside that window (logout during a
flush — including the forced 401 logout, which happens *because* a request was in flight), the
quarantine's write reverts it:

```text
Q4 after markSynced          : status=synced  serverId=SERVER-ID-123
Q4 after stale quarantine put: status=syncing serverId=null  blob=null
Q4 next login                : revived=1 -> status=failed, pending=1, blob=null
```

Availability/integrity, not confidentiality: a photo the server already holds shows a permanent red
failure, and the retry tells the user to re-shoot a photo that uploaded fine — creating a genuine
duplicate that M-04 leaves undeletable. Natural (unstalled) ordering does not hit this (`Q3` came out
`synced, revived=0`), so the window is small, but real. **Fix:** a `mutate(id, fn)` primitive on
`OutboxStorageAdapter` that reads and writes inside one `readwrite` transaction.

#### N-03 (Low, NEW) — the availability tradeoff is right, but the user is never told

**The tradeoff is correct.** `list()`'s owner filter only scopes the *app's* view; anything with
devtools or file-level profile access reads the raw `Blob` regardless of who is signed in. Dropping
the bytes is the only control that binds at rest, and keeping the metadata so the user can see what
to re-shoot is the right nuance.

**But the user is not warned.** `Sidebar.tsx:90-96`'s `onClick={logout}` is unconditional — no
confirmation, no count, no copy anywhere saying that signing out discards un-synced photo bytes. The
loss surfaces later, as a failed retry after signing back in. **Fix:** gate sign-out on the pending
count and offer "ซิงค์ก่อนออกจากระบบ".

### 9.4 The four recommended Lows — all hold

| ID | Status | Evidence (execution-verified) |
| :-- | :-- | :-- |
| **L-01** | **Fixed.** `Math.Max(DeclaredContentLength ?? 0, Content.LongLength)` | `11 MiB declared as 5 bytes -> PhotoFileTooLarge` (was: sailed past). Also `16B declared as 99 MiB -> PhotoFileTooLarge`. |
| **L-02** | **Fixed.** Per-type length bounds + CRC-32 on every kept chunk | `pHYs` 35B → REJECT (outside 9-9). Lying CRC → REJECT. `IHDR` 12B → REJECT. Valid 9-byte `pHYs` with correct CRC → accepted (no false positive). |
| **L-04** | **Fixed.** | Row present, blob deleted → `IsFailure=True error=PhotoNotFound` (was `THROWS -> 500`). `LogWarning` logs the photo id, never `StorageKey`. |
| **L-05** | **Fixed.** Indexer assignment | `indexer twice -> 'nosniff' count=1`; contrast `indexer+Append -> 'nosniff,nosniff' count=2`. |

#### On the PNG fixture-helper change — **nothing was quietly weakened**

Enabling CRC validation forced `PhotoImageFixtures.PngChunk` to compute a real CRC by default, so
every existing PNG test's *input* changed. Checked three ways:

1. **Validated against reality, not just itself.** The fixture's CRC-32 is a re-implementation of the
   same algorithm as the scrubber's, so a shared mistake would cancel out and both suites would stay
   green. Broken with an independent oracle — a genuine 1×1 PNG from a real encoder:
   `real PNG scrub : OK in=70 out=70`.
2. **The stripped-chunk assertions never depended on it.** Validation runs only inside
   `if (PngKeptChunkTypes.Contains(type))`, so `eXIf`/`tEXt` are dropped whatever their CRC says:
   `eXIf with all-zero CRC -> OK out=66 GPSLat=False`.
3. **The negative case is still negative.** `crcOverride: 0xDEADBEEF` keeps the lying-CRC fixture
   genuinely lying, and it is now rejected.

Residual, beyond what L-02 asked for: `PLTE`, `hIST` and `IDAT` remain length-unbounded, so a
correctly-CRC'd chunk of arbitrary size still carries arbitrary bytes (`PLTE 4KB -> GPSLat=True`,
`hIST 1.9KB -> IMEI=True`). `IDAT` is irreducibly variable; `PLTE` (≤768, multiple of 3) and `hIST`
(exactly 2 × palette entries) could be bounded like the other seven. Low, tracked, not blocking.

### 9.5 What still could not be verified

§7 items 1, 2, 4, 5 and 6 are unchanged — no SQL Server (every persistence result is EF Core
InMemory; the 2026-08-10 lesson still applies), no running API over a socket, no real device corpus,
no cloud adapter, no fuzzing at scale. **§7 item 3 is now closed:** the Playwright suite was run
(13/13). It remains structurally blind to N-01, which lives in stored bytes it never inspects — but
it now covers H-02 directly (logout → different tenant → empty screen and zero HTTP attempts → A
signs back in), and that test passes.

---

## 10. N-01 closure — 2026-08-11

Fixed in the same pass, per §9.2's recommendation that a known mis-framing in a security parser
should not be left to come back to later — which is how H-01 happened.

`ExifScrubber.StripJpeg` now rejects a nested SOI outright rather than letting it fall through to the
length-bearing branch:

```csharp
if (marker == 0xD8) // A second SOI. Reject, rather than fall through.
{
    throw new ImageProcessingException(
        "Malformed JPEG: unexpected SOI marker inside the stream.");
}
```

Rejecting is safe, and cannot catch real encoder output: the only legitimate nested `FF D8` in a JPEG
is an EXIF thumbnail (inside an APP1 that is already stripped whole) or an MPF secondary image (after
EOI, already truncated).

Covered by `ExifScrubberTests.Strip_Jpeg_Rejects_A_Nested_Soi_Rather_Than_Copying_Its_Mis_Framed_Payload_Verbatim`,
against a new `PhotoImageFixtures.JpegWithNestedSoiSmugglingMetadata` fixture carrying the same
`Exif\0\0`-prefixed GPS/IMEI payload the probe used. The test first asserts the fixture genuinely
contains the marker, so it cannot pass vacuously.

Post-fix, re-run by the orchestrator: build **0 warnings**; Domain **370**, Application **651**,
Architecture **14**, Integration **475**.

### 10.1 Updated open-findings ledger for Sprint 12

| ID | Status |
| :-- | :-- |
| H-01 | **Closed**, execution-verified on stored bytes |
| H-02 | **Closed**, execution-verified with the two-owner and retention probes |
| L-01, L-02, L-04, L-05 | **Closed**, execution-verified |
| **N-01** | **Closed** — nested-SOI rejection + fixture (§10) |
| **N-02 (Low)** | Open — quarantine lost update; needs a transactional `mutate` primitive |
| **N-03 (Low)** | Open — no warning before logout destroys un-synced bytes |
| M-01 | Open — schedule explicitly against S13-BE-01 |
| M-02, M-03, M-04 | Open — need ADR / `devops-engineer` / `po-analyst` decisions |
| L-03, L-06, L-07, L-08 | Open, non-blocking. L-06's display filter landed; L-07 unchanged |

**Sprint 12 is closed on S12-SEC-01.**

ADR-0005's precondition for S13-FE-01 is satisfied: the ownership model, the logout quarantine and
the retention sweep are all `kind`-agnostic and were verified against a second (`weather-log`) kind,
not only `photo`.
