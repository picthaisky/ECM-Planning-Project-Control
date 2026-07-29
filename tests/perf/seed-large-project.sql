/* =============================================================================================
   tests/perf/seed-large-project.sql

   S4-DB-02 (docs/10. Sprint 4, database-engineer row) - reusable, deterministic large-dataset
   generator for WBS-tree / CPM / Gantt performance testing.

   Lives at the TOP-LEVEL tests/ folder (sibling of backend/ and web/), not backend/tests/,
   because it is cross-stack tooling: qa-engineer's S4-QA-01 k6 perf test
   (tests/perf/wbs-tree.k6.js, hits the real GET /api/v1/projects/{id}/wbs-tree API) and
   Sprint 6's Gantt frontend perf test both consume the same project this script produces
   (docs/specs/master-plan/design.md, cross-stack tests/ convention).

   WHAT IT CREATES (one project, real referentially-valid rows, no mocks):
     - 5,000  WBSNodes   : 1 root + 10 phases + 100 zones (10/phase) + 4,889 leaf work packages.
                           A genuine 4-level hierarchy - not a degenerate flat list and not a
                           single deep chain - mirroring the shape backend-developer's own
                           Sprint 4 sanity fixture used (root + branches + leaves), one level
                           deeper.
     - 10,000 Activities : attached only to leaf WBSNodes, 2 or 3 per leaf (222 leaves get 3,
                           the remaining 4,667 get 2 -> 222*3 + 4667*2 = 10,000 exactly).
     - 15,000 ActivityRelations : a small-world CPM network - a sequential "backbone" link
                           between each activity and its immediate predecessor-in-generation-
                           order (9,999 relations covering every activity but the first) plus
                           5,001 longer-range cross-zone/cross-phase dependencies (distance 2-80
                           in generation order). RelationType mix approximates a realistic P6
                           schedule (~85% FS / 8% SS / 5% FF / 2% SF); LagDays is mostly 0 with
                           occasional +1..+10 (procurement/curing lag) or -1..-5 (lead). Every
                           relation has PredecessorSeq < SuccessorSeq by construction, so the
                           network is guaranteed acyclic (a DAG) - useful, not just incidental,
                           for Sprint 5's CPM engine to later run against this same dataset.

   WHERE IT ATTACHES (why this depends on S1-DB-03, per docs/10 §6's dependency column):
   This script does NOT create a new orphan Tenant. It looks up the existing dev/CI tenant
   "Siam Construction Co., Ltd." created by DevDataSeeder (S1-DB-03) and adds one more Project
   to it, alongside that seeder's own "Riverside Condominium Tower B" (RCT-B) project. This is
   deliberate: S1-DB-03 already seeds one user per role (pm@siam-construction.dev etc.,
   password Dev@CMPlus2026!) who can obtain a real JWT and hit the real API - a brand-new
   standalone tenant would have no login path and qa-engineer's k6 test could never
   authenticate against it. Run DevDataSeeder (or the equivalent dev/CI seed step) at least once
   against the target database before this script.

   DETERMINISM ("byte-identical output every run", docs/10 §6 S4-DB-02 DoD):
   Every id, date offset, duration, cost, relation type and lag is derived from
   SHA2_256(@Seed + ':' + <label> + ':' + <row index>) - never NEWID(), RAND(), or GETDATE().
   Given the same @Seed (below), the same row always gets the same id/attributes on every run,
   on any machine. This was verified by running this script twice against a fresh container and
   comparing CHECKSUM_AGG(BINARY_CHECKSUM(*)) per table before/after a second run - see the
   S4-DB-02 report for the actual numbers.

   IDEMPOTENCY (docs/db-conventions.md §5 - seed data must be idempotent):
   - Tenant: never created by this script (see above) - only looked up; fails loudly if missing.
   - Project: get-or-create by the natural key (TenantId, Code) - never recreated once it exists,
     so its Id is stable across runs even if this script's hash formula ever changes later.
   - WBSNodes/Activities/ActivityRelations ("the volume tables"): fully DELETEd and rebuilt for
     this specific ProjectId on every run. This is deliberate, not accidental - it lets the
     dataset be reset to a known state (e.g. after ad hoc test pollution) without a container
     rebuild, while still being byte-identical every time given the deterministic derivation
     above. The DELETE is scoped strictly to this ProjectId; it never touches another
     tenant/project's rows.

   SCOPE NOTE (what this generator deliberately does NOT do):
   - No ActivityProgressLog rows. Activity.ProgressPercentage stays 0.00 with both
     LatestProgressPeriodEndDate/LatestProgressRecordedAt NULL - i.e. "never progressed", which
     is the only state consistent with ADR-0009's rule that the progress cache is NEVER written
     without a backing log row. A future EVM-focused perf-data extension must add both the log
     rows and the cache update together, never the cache alone.
   - No CPM-computed fields (IsCritical/TotalFloat/FreeFloat) - these are Sprint 5 CPM engine
     output, not seed data; they stay false/NULL here.
   - PlannedStart/PlannedFinish are independently sampled per activity (spread across the
     contract period, correlated with generation order) purely for volume/Gantt-rendering
     realism - they are NOT run through the CPM forward/backward pass. Sprint 5's CPM engine
     recomputes ES/EF/LS/LF from the ActivityRelation network when it runs against this data;
     until then these are illustrative planned dates only.

   USAGE:
     sqlcmd -S localhost,1433 -U sa -P "<sa password>" -C -N -d CMPlusDb -b ^
       -i tests/perf/seed-large-project.sql

   Prints the resolved TenantId/ProjectId and final row counts at the end - qa-engineer's k6
   test and the Gantt perf test both need the ProjectId to target
   GET /api/v1/projects/{projectId}/wbs-tree.
   ============================================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ---------------------------------------------------------------------------------------------
-- 0. Fixed parameters - change @Seed to force a fresh derivation of every id/attribute (this
--    would also change every generated Guid, so treat it as a version marker, not a knob to
--    tweak casually).
-- ---------------------------------------------------------------------------------------------
DECLARE @Seed                nvarchar(100)    = N'CMPLUS-S4-DB-02-v1';
DECLARE @DevTenantName       nvarchar(250)    = N'Siam Construction Co., Ltd.'; -- from DevSeedData (S1-DB-03)
DECLARE @ProjectCode         nvarchar(50)     = N'PERF-5K10K15K';
DECLARE @ProjectName         nvarchar(250)    = N'Performance Test Mega-Project (5,000 WBS / 10,000 Activities)';
DECLARE @Owner               nvarchar(250)    = N'CM+ QA & Performance Engineering';
DECLARE @ContractStart       datetimeoffset(7) = '2025-01-01T00:00:00+07:00';
DECLARE @ContractFinish      datetimeoffset(7) = '2027-12-31T00:00:00+07:00';
DECLARE @DataDate            datetimeoffset(7) = '2026-07-01T00:00:00+07:00';
DECLARE @BAC                 decimal(18,2)    = 3000000000.00;
DECLARE @RetentionRate       decimal(5,2)     = 5.00;
DECLARE @AdvanceRate         decimal(5,2)     = 10.00;

DECLARE @TotalContractDays   int = DATEDIFF(day, @ContractStart, @ContractFinish);

-- ---------------------------------------------------------------------------------------------
-- 1. Resolve the dev tenant (never created here - see header). Fail loudly if S1-DB-03 has not
--    run against this database yet, rather than silently creating an unreachable tenant.
-- ---------------------------------------------------------------------------------------------
DECLARE @TenantId uniqueidentifier = (SELECT Id FROM dbo.Tenants WHERE Name = @DevTenantName);

IF @TenantId IS NULL
BEGIN
    RAISERROR(N'[seed-large-project] Tenant "%s" not found. Run the S1-DB-03 dev seeder (DevDataSeeder.SeedAsync) against this database first, then re-run this script.', 16, 1, @DevTenantName);
    RETURN;
END

PRINT N'[seed-large-project] using TenantId = ' + CONVERT(nvarchar(36), @TenantId) + N' (' + @DevTenantName + N')';

-- ---------------------------------------------------------------------------------------------
-- 2. Project - get-or-create by (TenantId, Code). Deterministic Id derived from @Seed alone
--    (independent of @TenantId) so the same @Seed always yields the same Project Id regardless
--    of which database/tenant-lookup-order it lands in; if the row already exists, its actual
--    stored Id wins (see header: never re-derive the identity of an already-existing row).
-- ---------------------------------------------------------------------------------------------
DECLARE @ProjectId uniqueidentifier =
    CONVERT(uniqueidentifier, CONVERT(varbinary(16), HASHBYTES('SHA2_256', CONCAT(@Seed, N':PROJECT:0'))));

IF NOT EXISTS (SELECT 1 FROM dbo.Projects WHERE TenantId = @TenantId AND Code = @ProjectCode)
BEGIN
    INSERT INTO dbo.Projects
        (Id, TenantId, Name, Code, Owner, ContractStart, ContractFinish, BAC, RetentionRate, AdvanceRate,
         DataDate, EacVariantDefault, ContractValue, RetentionRelease1Percentage, AdvanceRecoveryMethod)
    VALUES
        (@ProjectId, @TenantId, @ProjectName, @ProjectCode, @Owner, @ContractStart, @ContractFinish, @BAC,
         @RetentionRate, @AdvanceRate, @DataDate, 1 /* EacVariant.CpiBased */, @BAC,
         CAST(50.00 AS decimal(5,2)), 1 /* AdvanceRecoveryMethod.ProRata */);
    PRINT N'[seed-large-project] created Project ' + @ProjectCode;
END
ELSE
BEGIN
    SET @ProjectId = (SELECT Id FROM dbo.Projects WHERE TenantId = @TenantId AND Code = @ProjectCode);
    PRINT N'[seed-large-project] Project ' + @ProjectCode + N' already exists, reusing Id = ' + CONVERT(nvarchar(36), @ProjectId);
END

-- ---------------------------------------------------------------------------------------------
-- 3. Wipe this project's volume tables (idempotent regenerate - see header). Children first
--    (ActivityRelations -> Activities -> WBSNodes) to respect FKs; scoped strictly to
--    @ProjectId so no other tenant/project row is ever touched.
-- ---------------------------------------------------------------------------------------------
BEGIN TRAN;

DELETE ar
FROM dbo.ActivityRelations ar
WHERE ar.PredecessorActivityId IN (SELECT a.Id FROM dbo.Activities a JOIN dbo.WBSNodes w ON w.Id = a.WbsNodeId WHERE w.ProjectId = @ProjectId)
   OR ar.SuccessorActivityId   IN (SELECT a.Id FROM dbo.Activities a JOIN dbo.WBSNodes w ON w.Id = a.WbsNodeId WHERE w.ProjectId = @ProjectId);

DELETE a
FROM dbo.Activities a
JOIN dbo.WBSNodes w ON w.Id = a.WbsNodeId
WHERE w.ProjectId = @ProjectId;

DELETE FROM dbo.WBSNodes WHERE ProjectId = @ProjectId;

PRINT N'[seed-large-project] wiped existing volume rows for Project ' + @ProjectCode;

-- ---------------------------------------------------------------------------------------------
-- 4. #Tally - a plain 1..20,000 numbers table, built set-based (no loops), reused throughout.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Tally') IS NOT NULL DROP TABLE #Tally;
;WITH E1(N) AS (SELECT N FROM (VALUES (1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) AS X(N)),
      E2(N) AS (SELECT 1 FROM E1 a CROSS JOIN E1 b),
      E4(N) AS (SELECT 1 FROM E2 a CROSS JOIN E2 b),
      E6(N) AS (SELECT TOP (20000) 1 FROM E4 a CROSS JOIN E2 b)
SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N
INTO #Tally
FROM E6;

-- ---------------------------------------------------------------------------------------------
-- 5. WBS hierarchy: #Phases (10, level 1) -> #Zones (100, level 2, 10/phase) ->
--    #Leaves (4,889, level 3). Weight percentages are even splits within each parent (sum to
--    100.00 under every parent) - realistic enough for a perf dataset; WeightPercentage is not
--    otherwise cross-validated anywhere in the codebase today.
-- ---------------------------------------------------------------------------------------------
DECLARE @RootId uniqueidentifier =
    CONVERT(uniqueidentifier, CONVERT(varbinary(16), HASHBYTES('SHA2_256', CONCAT(@Seed, N':ROOT:0'))));

IF OBJECT_ID('tempdb..#Phases') IS NOT NULL DROP TABLE #Phases;
SELECT
    p.PhaseIdx,
    CONVERT(uniqueidentifier, CONVERT(varbinary(16), HASHBYTES('SHA2_256', CONCAT(@Seed, N':PHASE:', CAST(p.PhaseIdx AS nvarchar(20)))))) AS Id,
    FORMAT(p.PhaseIdx + 1, N'00') AS Code,
    p.Title
INTO #Phases
FROM (VALUES
    (0, N'Preliminary & Site Mobilization'),
    (1, N'Earthworks & Piling'),
    (2, N'Substructure'),
    (3, N'Superstructure - Structural Frame'),
    (4, N'Building Envelope & Roofing'),
    (5, N'MEP Rough-in'),
    (6, N'Interior Finishes'),
    (7, N'MEP Fit-out & Commissioning'),
    (8, N'External Works & Landscaping'),
    (9, N'Testing, Commissioning & Handover')
) AS p(PhaseIdx, Title);

IF OBJECT_ID('tempdb..#Zones') IS NOT NULL DROP TABLE #Zones;
SELECT
    z.ZoneIdx, z.PhaseIdx, z.LocalSeq, z.Id,
    ph.Code + N'.' + FORMAT(z.LocalSeq, N'00') AS Code,
    N'Zone ' + FORMAT(z.LocalSeq, N'00')       AS Title
INTO #Zones
FROM (
    SELECT
        t.N - 1                AS ZoneIdx,
        (t.N - 1) / 10          AS PhaseIdx,
        (t.N - 1) % 10 + 1      AS LocalSeq,
        CONVERT(uniqueidentifier, CONVERT(varbinary(16), HASHBYTES('SHA2_256', CONCAT(@Seed, N':ZONE:', CAST(t.N - 1 AS nvarchar(20)))))) AS Id
    FROM #Tally t
    WHERE t.N <= 100
) z
JOIN #Phases ph ON ph.PhaseIdx = z.PhaseIdx;

IF OBJECT_ID('tempdb..#Leaves') IS NOT NULL DROP TABLE #Leaves;
;WITH L AS (
    SELECT
        t.N - 1        AS LeafIdx,
        (t.N - 1) % 100 AS ZoneIdx
    FROM #Tally t
    WHERE t.N <= 4889
),
L2 AS (
    SELECT
        L.LeafIdx, L.ZoneIdx,
        ROW_NUMBER() OVER (PARTITION BY L.ZoneIdx ORDER BY L.LeafIdx) AS LocalSeq,
        COUNT(*)     OVER (PARTITION BY L.ZoneIdx)                    AS SiblingCount
    FROM L
)
SELECT
    L2.LeafIdx, L2.ZoneIdx, L2.LocalSeq,
    z.Id AS ZoneId,
    z.Code + N'.' + FORMAT(L2.LocalSeq, N'000')   AS Code,
    N'Work Package ' + FORMAT(L2.LeafIdx + 1, N'0000') AS Title,
    CAST(ROUND(100.0 / L2.SiblingCount, 2) AS decimal(5,2)) AS Weight,
    CONVERT(uniqueidentifier, CONVERT(varbinary(16), HASHBYTES('SHA2_256', CONCAT(@Seed, N':LEAF:', CAST(L2.LeafIdx AS nvarchar(20)))))) AS Id
INTO #Leaves
FROM L2
JOIN #Zones z ON z.ZoneIdx = L2.ZoneIdx;

-- Sanity: exactly 4,889 leaves (1 + 10 + 100 + 4889 = 5,000 total WBSNodes).
IF (SELECT COUNT(*) FROM #Leaves) <> 4889
BEGIN
    ROLLBACK TRAN;
    RAISERROR(N'[seed-large-project] internal error: expected 4889 leaves, got %d', 16, 1, @@ROWCOUNT);
    RETURN;
END

INSERT INTO dbo.WBSNodes (Id, TenantId, ProjectId, ParentWbsNodeId, Code, Title, WeightPercentage)
SELECT @RootId, @TenantId, @ProjectId, NULL, N'0', @ProjectName + N' - WBS Root', CAST(100.00 AS decimal(5,2))
UNION ALL
SELECT ph.Id, @TenantId, @ProjectId, @RootId, ph.Code, ph.Title, CAST(10.00 AS decimal(5,2))
FROM #Phases ph
UNION ALL
SELECT z.Id, @TenantId, @ProjectId, ph.Id, z.Code, z.Title, CAST(10.00 AS decimal(5,2))
FROM #Zones z
JOIN #Phases ph ON ph.PhaseIdx = z.PhaseIdx
UNION ALL
SELECT lf.Id, @TenantId, @ProjectId, lf.ZoneId, lf.Code, lf.Title, lf.Weight
FROM #Leaves lf;

PRINT N'[seed-large-project] inserted ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N' WBSNodes';

-- ---------------------------------------------------------------------------------------------
-- 6. Activities: 2 or 3 per leaf (222 leaves x 3, 4,667 leaves x 2 = 10,000). Which 222 leaves
--    get 3 is chosen by a deterministic hash-based shuffle (not the first 222 by index), so the
--    extra activity is spread pseudo-randomly across the whole tree rather than clustering in
--    one phase.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#LeafActCount') IS NOT NULL DROP TABLE #LeafActCount;
;WITH Ranked AS (
    SELECT
        LeafIdx,
        ROW_NUMBER() OVER (ORDER BY
            CONVERT(bigint, CONVERT(varbinary(8), HASHBYTES('SHA2_256', CONCAT(@Seed, N':LEAF3FLAG:', CAST(LeafIdx AS nvarchar(20)))))) & 0x7FFFFFFFFFFFFFFF
        ) AS ShuffleRank
    FROM #Leaves
)
SELECT LeafIdx, CASE WHEN ShuffleRank <= 222 THEN 3 ELSE 2 END AS ActCount
INTO #LeafActCount
FROM Ranked;

IF OBJECT_ID('tempdb..#ActivitySlots') IS NOT NULL DROP TABLE #ActivitySlots;
;WITH Slots AS (
    SELECT lf.LeafIdx, lf.Id AS LeafId, lf.Code AS LeafCode, n.LocalActSeq
    FROM #Leaves lf
    JOIN #LeafActCount lac ON lac.LeafIdx = lf.LeafIdx
    JOIN (VALUES (1), (2), (3)) AS n(LocalActSeq) ON n.LocalActSeq <= lac.ActCount
)
SELECT
    ROW_NUMBER() OVER (ORDER BY LeafIdx, LocalActSeq) AS Seq,
    LeafIdx, LeafId, LeafCode, LocalActSeq
INTO #ActivitySlots
FROM Slots;

IF (SELECT COUNT(*) FROM #ActivitySlots) <> 10000
BEGIN
    ROLLBACK TRAN;
    RAISERROR(N'[seed-large-project] internal error: expected 10000 activity slots, got %d', 16, 1, @@ROWCOUNT);
    RETURN;
END

-- One SHA2_256 digest per activity, sliced into four independent 8-byte pseudo-random channels
-- (name pool index / duration / start-date jitter / budget) rather than four separate
-- HASHBYTES calls - same determinism, a quarter of the hashing work.
IF OBJECT_ID('tempdb..#Activities') IS NOT NULL DROP TABLE #Activities;
SELECT
    s.Seq,
    s.LeafId,
    CONVERT(uniqueidentifier, CONVERT(varbinary(16), HASHBYTES('SHA2_256', CONCAT(@Seed, N':ACT:', CAST(s.Seq AS nvarchar(20)))))) AS Id,
    N'A-' + s.LeafCode + N'-' + CAST(s.LocalActSeq AS nvarchar(10)) AS ActivityCode,
    namepool.Name AS Name,
    dur.DurationDays,
    ofs.StartOffsetDays,
    cost.BudgetCost
INTO #Activities
FROM #ActivitySlots s
CROSS APPLY (SELECT HASHBYTES('SHA2_256', CONCAT(@Seed, N':ACTATTR:', CAST(s.Seq AS nvarchar(20)))) AS H) hh
CROSS APPLY (SELECT
    (CONVERT(bigint, CONVERT(varbinary(8), SUBSTRING(hh.H, 1, 8)))  & 0x7FFFFFFFFFFFFFFF) AS R1,
    (CONVERT(bigint, CONVERT(varbinary(8), SUBSTRING(hh.H, 9, 8)))  & 0x7FFFFFFFFFFFFFFF) AS R2,
    (CONVERT(bigint, CONVERT(varbinary(8), SUBSTRING(hh.H, 17, 8))) & 0x7FFFFFFFFFFFFFFF) AS R3,
    (CONVERT(bigint, CONVERT(varbinary(8), SUBSTRING(hh.H, 25, 8))) & 0x7FFFFFFFFFFFFFFF) AS R4
) rr
CROSS APPLY (SELECT rr.R1 % 20 AS NameIdx) ni
CROSS APPLY (SELECT Name FROM (VALUES
    (0,  N'Site Clearance & Setting Out'),   (1,  N'Excavation'),
    (2,  N'Piling Works'),                   (3,  N'Formwork Installation'),
    (4,  N'Reinforcement Fixing'),           (5,  N'Concrete Pouring'),
    (6,  N'Curing & Formwork Strike'),       (7,  N'Steel Erection'),
    (8,  N'Precast Installation'),           (9,  N'Masonry & Blockwork'),
    (10, N'Waterproofing'),                  (11, N'Roof Covering Installation'),
    (12, N'Electrical Conduit & Wiring'),    (13, N'Plumbing Rough-in'),
    (14, N'HVAC Ductwork Installation'),     (15, N'Plastering & Skim Coating'),
    (16, N'Painting & Wall Finishes'),       (17, N'Flooring Installation'),
    (18, N'Ceiling Installation'),           (19, N'MEP Testing & Commissioning')
) AS np(Idx, Name) WHERE np.Idx = ni.NameIdx) namepool
CROSS APPLY (SELECT 3 + (rr.R2 % 28) AS DurationDays) dur
CROSS APPLY (SELECT (rr.R3 % 21) - 10 AS Jitter) jt
CROSS APPLY (SELECT
    CASE
        WHEN (FLOOR((s.Seq - 1) * 1.0 / 10000 * (@TotalContractDays - 30)) + jt.Jitter) < 0
            THEN 0
        WHEN (FLOOR((s.Seq - 1) * 1.0 / 10000 * (@TotalContractDays - 30)) + jt.Jitter) > (@TotalContractDays - dur.DurationDays)
            THEN (@TotalContractDays - dur.DurationDays)
        ELSE
            CAST(FLOOR((s.Seq - 1) * 1.0 / 10000 * (@TotalContractDays - 30)) + jt.Jitter AS int)
    END AS StartOffsetDays
) ofs
CROSS APPLY (SELECT CAST(10000 + (rr.R4 % 2901) * 100 AS decimal(18,2)) AS BudgetCost) cost;

INSERT INTO dbo.Activities
    (Id, TenantId, WbsNodeId, ActivityCode, Name, PlannedStart, PlannedFinish, DurationDays,
     BudgetCost, ProgressPercentage, IsCritical)
SELECT
    Id, @TenantId, LeafId, ActivityCode, Name,
    DATEADD(day, StartOffsetDays, @ContractStart)               AS PlannedStart,
    DATEADD(day, StartOffsetDays + DurationDays, @ContractStart) AS PlannedFinish,
    DurationDays, BudgetCost, CAST(0.00 AS decimal(5,2)), CAST(0 AS bit)
FROM #Activities;

PRINT N'[seed-large-project] inserted ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N' Activities';

-- ---------------------------------------------------------------------------------------------
-- 7. ActivityRelations: 9,999 backbone links (every activity but the first, by generation Seq,
--    links to its immediate predecessor) + 5,001 longer-range cross-links = 15,000 exactly.
--    PredecessorSeq < SuccessorSeq always -> guaranteed acyclic.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#RelDefs') IS NOT NULL DROP TABLE #RelDefs;
SELECT SuccSeq, PredSeq, Kind
INTO #RelDefs
FROM (
    SELECT s.Seq AS SuccSeq, s.Seq - 1 AS PredSeq, N'BACKBONE' AS Kind
    FROM #ActivitySlots s
    WHERE s.Seq BETWEEN 2 AND 10000

    UNION ALL

    SELECT s.Seq AS SuccSeq, s.Seq - dist.Distance AS PredSeq, N'EXTRA' AS Kind
    FROM #ActivitySlots s
    CROSS APPLY (SELECT CASE WHEN (s.Seq - 1) > 80 THEN 80 ELSE (s.Seq - 1) END AS MaxDist) md
    CROSS APPLY (SELECT 2 + (
        (CONVERT(bigint, CONVERT(varbinary(8), HASHBYTES('SHA2_256', CONCAT(@Seed, N':RELDIST:', CAST(s.Seq AS nvarchar(20)))))) & 0x7FFFFFFFFFFFFFFF)
        % (md.MaxDist - 1)
    ) AS Distance) dist
    WHERE s.Seq BETWEEN 3 AND 5003
) rd;

IF (SELECT COUNT(*) FROM #RelDefs) <> 15000
BEGIN
    ROLLBACK TRAN;
    RAISERROR(N'[seed-large-project] internal error: expected 15000 relation defs, got %d', 16, 1, @@ROWCOUNT);
    RETURN;
END

IF OBJECT_ID('tempdb..#RelationRows') IS NOT NULL DROP TABLE #RelationRows;
SELECT
    CONVERT(uniqueidentifier, CONVERT(varbinary(16), HASHBYTES('SHA2_256', CONCAT(@Seed, N':RELID:', rd.Kind, N':', CAST(rd.SuccSeq AS nvarchar(20)))))) AS Id,
    predAct.Id AS PredecessorActivityId,
    succAct.Id AS SuccessorActivityId,
    CASE WHEN tr.TypeRoll < 85 THEN 1 WHEN tr.TypeRoll < 93 THEN 2 WHEN tr.TypeRoll < 98 THEN 3 ELSE 4 END AS RelationType,
    CASE WHEN lg.LagRoll < 70 THEN 0 WHEN lg.LagRoll < 95 THEN 1 + (lg.LagValHash % 10) ELSE -1 - (lg.LagValHash % 5) END AS LagDays
INTO #RelationRows
FROM #RelDefs rd
JOIN #Activities predAct ON predAct.Seq = rd.PredSeq
JOIN #Activities succAct ON succAct.Seq = rd.SuccSeq
CROSS APPLY (SELECT
    (CONVERT(bigint, CONVERT(varbinary(8), HASHBYTES('SHA2_256', CONCAT(@Seed, N':RELTYPE:', rd.Kind, N':', CAST(rd.SuccSeq AS nvarchar(20)))))) & 0x7FFFFFFFFFFFFFFF) % 100 AS TypeRoll
) tr
CROSS APPLY (SELECT
    (CONVERT(bigint, CONVERT(varbinary(8), HASHBYTES('SHA2_256', CONCAT(@Seed, N':RELLAG:', rd.Kind, N':', CAST(rd.SuccSeq AS nvarchar(20)))))) & 0x7FFFFFFFFFFFFFFF) % 100 AS LagRoll,
    (CONVERT(bigint, CONVERT(varbinary(8), HASHBYTES('SHA2_256', CONCAT(@Seed, N':RELLAGVAL:', rd.Kind, N':', CAST(rd.SuccSeq AS nvarchar(20)))))) & 0x7FFFFFFFFFFFFFFF) AS LagValHash
) lg;

INSERT INTO dbo.ActivityRelations (Id, TenantId, PredecessorActivityId, SuccessorActivityId, RelationType, LagDays)
SELECT Id, @TenantId, PredecessorActivityId, SuccessorActivityId, RelationType, LagDays
FROM #RelationRows;

PRINT N'[seed-large-project] inserted ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N' ActivityRelations';

COMMIT TRAN;

-- ---------------------------------------------------------------------------------------------
-- 8. Final verification - printed so any caller (human, CI, k6 setup step) can confirm shape
--    without a second round trip.
-- ---------------------------------------------------------------------------------------------
SELECT
    @TenantId AS TenantId,
    @ProjectId AS ProjectId,
    @ProjectCode AS ProjectCode,
    (SELECT COUNT(*) FROM dbo.WBSNodes WHERE ProjectId = @ProjectId) AS WbsNodeCount,
    (SELECT COUNT(*) FROM dbo.Activities a JOIN dbo.WBSNodes w ON w.Id = a.WbsNodeId WHERE w.ProjectId = @ProjectId) AS ActivityCount,
    (SELECT COUNT(*) FROM dbo.ActivityRelations ar
        WHERE ar.SuccessorActivityId IN (SELECT a.Id FROM dbo.Activities a JOIN dbo.WBSNodes w ON w.Id = a.WbsNodeId WHERE w.ProjectId = @ProjectId)
    ) AS ActivityRelationCount;
