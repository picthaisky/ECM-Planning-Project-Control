-- Pre-flight dedup check for migration 20260811134718_Sprint15_ApprovalPolicy_SplitSingleActiveIndex
-- ============================================================================================
-- ADR-0021 / database-engineer, 2026-08-11. Run this against the TARGET database BEFORE applying
-- 20260811_sprint15_approvalpolicy_split_singleactive_index.sql (or `dotnet ef database update`).
--
-- Why this is needed (state it plainly, not just "run this script"):
-- The migration's first CREATE UNIQUE INDEX is
--   CREATE UNIQUE INDEX [IX_ApprovalPolicies_TenantId_DocumentType] ON [ApprovalPolicies]
--     ([TenantId], [DocumentType]) WHERE [IsActive] = 1 AND [ProjectId] IS NULL;
-- SQL Server validates uniqueness across ALL EXISTING rows at CREATE INDEX time, not just future
-- writes. If this database already contains the exact corruption ADR-0021 describes - two (or
-- more) ApprovalPolicies rows sharing (TenantId, DocumentType), both IsActive = 1, both
-- ProjectId IS NULL - the CREATE UNIQUE INDEX statement FAILS (Msg 1505, "The CREATE UNIQUE INDEX
-- statement terminated because a duplicate key was found"), the whole idempotent script's
-- transaction rolls back (BEGIN TRANSACTION ... COMMIT wraps the whole file), and NOTHING in the
-- script applies - not even the unrelated-looking statements bundled in the same idempotent run.
--
-- That failure is CORRECT behaviour, not a bug in the migration: it is the database refusing to
-- silently pick a winner among two active policy versions on your behalf, which is exactly the
-- nondeterminism this fix exists to close. But it means the migration cannot be a fire-and-forget
-- step in the deployment runbook - operators must know BEFORE running it whether this database is
-- one of the ones actually carrying the corruption, and have a resolution path ready if so.
--
-- What this script does: reports rows, does not fix anything. Fixing is a data decision (which of
-- the two active versions should really be active?) that needs a human with context on the tenant,
-- not a script guess. Mirrors the GROUP BY ... HAVING COUNT(*) > 1 shape used for the N-04
-- (PaymentCertificateApprovalStep) IPC unique-index pre-flight in Sprint 10.
-- ============================================================================================

SELECT
    p.TenantId,
    p.DocumentType,
    COUNT(*)                                    AS ActiveVersionCount,
    STRING_AGG(CONVERT(nvarchar(36), p.Id), ', ')      AS ConflictingApprovalPolicyIds,
    STRING_AGG(CONVERT(nvarchar(10), p.Version), ', ') AS ConflictingVersions
FROM [ApprovalPolicies] AS p
WHERE p.IsActive = 1
  AND p.ProjectId IS NULL
GROUP BY p.TenantId, p.DocumentType
HAVING COUNT(*) > 1
ORDER BY p.TenantId, p.DocumentType;

-- Interpreting the result:
--   0 rows returned  -> safe to apply the migration as-is; no pre-migration remediation needed.
--   >=1 row returned -> DO NOT apply the migration yet. For each (TenantId, DocumentType) reported,
--                       a human (Tenant Admin context, or the on-call engineer with product input)
--                       must decide which single ApprovalPolicyId should remain IsActive = 1 and
--                       deactivate the other(s), e.g.:
--
--   UPDATE [ApprovalPolicies]
--   SET IsActive = 0, EffectiveTo = SYSDATETIMEOFFSET()
--   WHERE Id = @losingApprovalPolicyId;   -- the version NOT chosen to remain active
--
--                       This is a one-row-at-a-time, reviewed remediation - never a bulk "keep the
--                       highest Version" auto-resolution, because Version alone does not tell you
--                       which one a live in-flight document may have already pinned via
--                       ApprovalPolicyId (deactivating the wrong one does not corrupt in-flight
--                       routing - documents pin a specific ApprovalPolicyId, per ADR-0021's own
--                       consequences section - but it does mean a human should still confirm intent
--                       rather than the script guessing). Re-run this pre-flight check after
--                       remediation and confirm zero rows before proceeding to the migration.
--
-- The project-scoped group (ProjectId IS NOT NULL) is NOT checked here because it was already
-- protected by the original single index (ADR-0008) - a real, non-null ProjectId makes ANSI NULL
-- uniqueness semantics behave normally, so that group could never have accumulated this corruption
-- in the first place. Confirmable directly if desired:
--
-- SELECT p.TenantId, p.ProjectId, p.DocumentType, COUNT(*) AS ActiveVersionCount
-- FROM [ApprovalPolicies] AS p
-- WHERE p.IsActive = 1 AND p.ProjectId IS NOT NULL
-- GROUP BY p.TenantId, p.ProjectId, p.DocumentType
-- HAVING COUNT(*) > 1;
-- -- Expected: always 0 rows, on every database, before and after this migration.
