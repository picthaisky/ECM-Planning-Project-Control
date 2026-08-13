# Database Backup & Restore Runbook — S16-DO-03

The recovery procedure for the CM+ Project Control SQL Server database. This runbook is
**provider-agnostic** — it uses native SQL Server backup/restore, which behaves the same whether the
instance runs on AWS RDS/EC2, Azure SQL MI/VM, or the staging container. The provider-specific piece
is only *where the backup files live* (durable object storage off the DB host) and *who schedules
the automated backups*; both are noted where they matter.

> **Read first — the multi-tenant blast radius.** CM+ is single-database multi-tenant (one DB, every
> row carries `TenantId`; ADR-0002). A database restore therefore rolls back **every tenant** to the
> backup point — there is no per-tenant point-in-time restore. Treat any full restore as a
> whole-system event and communicate accordingly. Per-tenant data-correction is an application-level
> concern (audit log + compensating writes), never a DB restore.

---

## 1. Recovery objectives

| Metric | Target (staging → confirm/raise for prod) | How it is met |
| --- | --- | --- |
| **RPO** (max data loss) | ≤ 15 min | FULL recovery model + transaction-log backups every 15 min |
| **RTO** (max downtime to recover) | ≤ 60 min | latest FULL + DIFF + log chain; drill-measured below |
| **Backup retention** | 35 days rolling (staging); prod per contract/legal | lifecycle policy on the backup store |
| **Backup location** | durable store **off** the DB host (blob/S3/managed) | `vars.DB_BACKUP_TARGET` (see cd.yml) |

The database **must** run under the **FULL** recovery model in staging/production (not SIMPLE) —
otherwise transaction-log backups (and therefore point-in-time recovery to the RPO above) are
impossible. Verify:

```sql
SELECT name, recovery_model_desc FROM sys.databases WHERE name = 'CMPlusDb';
-- recovery_model_desc must be FULL
```

## 2. Backup schedule

| Type | Cadence | Command (native) |
| --- | --- | --- |
| **FULL** | nightly | `BACKUP DATABASE [CMPlusDb] TO URL/DISK = '…/CMPlusDb_FULL_<ts>.bak' WITH INIT, CHECKSUM, COMPRESSION;` |
| **DIFFERENTIAL** | every 6 h | `BACKUP DATABASE [CMPlusDb] … WITH DIFFERENTIAL, CHECKSUM, COMPRESSION;` |
| **LOG** | every 15 min | `BACKUP LOG [CMPlusDb] TO URL/DISK = '…/CMPlusDb_LOG_<ts>.trn' WITH CHECKSUM, COMPRESSION;` |

- `CHECKSUM` on every backup so corruption is caught at backup time, not discovered at restore time.
- On a managed offering (RDS / Azure SQL) the automated-backup + PITR feature satisfies this table —
  use it instead of hand-rolled jobs, and set its retention to match §1. This runbook's manual
  commands are the fallback / self-managed path and the basis of the drill in §5.

## 3. Pre-migration backup (ties to cd.yml `migrate`)

Every production migration is preceded by an on-demand **FULL** backup — this is the `migrate` job's
"Take pre-migration backup" step (`.github/workflows/cd.yml`) and the (a)-branch of that file's
database-rollback plan. Do **not** approve the `production-migrations` environment gate until a fresh
restorable FULL backup exists.

```sql
BACKUP DATABASE [CMPlusDb]
  TO URL = '<vars.DB_BACKUP_TARGET>/CMPlusDb_PREMIGRATION_<sha>_<ts>.bak'
  WITH INIT, CHECKSUM, COMPRESSION, NAME = 'pre-migration <sha>';
```

Record it in the drill log (§5) with the promoted image SHA, so a bad migration maps to exactly the
backup that precedes it.

## 4. Restore procedures

### 4.1 Full restore (latest good state)

```sql
-- 1. Restore the most recent FULL, leaving the DB in RESTORING state for the chain.
RESTORE DATABASE [CMPlusDb] FROM URL = '…/CMPlusDb_FULL_<ts>.bak'
  WITH NORECOVERY, REPLACE, CHECKSUM;

-- 2. Restore the most recent DIFFERENTIAL taken after that FULL (skip if none).
RESTORE DATABASE [CMPlusDb] FROM URL = '…/CMPlusDb_DIFF_<ts>.bak'
  WITH NORECOVERY, CHECKSUM;

-- 3. Restore each LOG backup in order, after the DIFF, up to the target.
RESTORE LOG [CMPlusDb] FROM URL = '…/CMPlusDb_LOG_<ts>.trn' WITH NORECOVERY, CHECKSUM;
-- … repeat for each subsequent log …

-- 4. Bring the DB online.
RESTORE DATABASE [CMPlusDb] WITH RECOVERY;
```

### 4.2 Point-in-time restore (e.g. recover to just before a bad migration/deploy)

Same as §4.1, but the **final** log restore stops at the instant:

```sql
RESTORE LOG [CMPlusDb] FROM URL = '…/CMPlusDb_LOG_<ts>.trn'
  WITH RECOVERY, STOPAT = '2026-08-12T14:32:00';
```

Choose `STOPAT` a few seconds **before** the offending change. For a bad migration, restoring §3's
pre-migration FULL is simpler and exact — prefer it when available.

### 4.3 Post-restore verification (always)

```sql
-- Integrity:
DBCC CHECKDB ('CMPlusDb') WITH NO_INFOMSGS;

-- Migration history is consistent with the app image being run (no partial/extra migration):
SELECT MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId DESC;
-- The newest row must match the last migration in the image's build (24 as of Sprint 15,
-- Sprint15_ApprovalPolicy_SplitSingleActiveIndex) — NOT a newer one from a rolled-back migration.

-- Tenant isolation smoke (no cross-tenant bleed introduced by the restore):
SELECT TenantId, COUNT(*) FROM Projects GROUP BY TenantId;
```

Then run the application smoke test (`web/e2e/smoke.spec.ts`, S16-QA-02) and confirm `/health/ready`
is 200 before returning traffic.

### 4.4 App/DB version alignment

After a DB rollback the running app image and the schema must agree. If you restored to *before* a
migration, you must also roll the **app** back to an image SHA whose migration set matches (re-run
`cd.yml` with the last-good SHA — the app-rollback path). A newer app against an older schema is an
outage; never leave them mismatched.

## 5. Restore drill log (DoD: real restore on staging, timed)

The S16-DO-03 DoD requires an **actual** restore from backup on staging, with the elapsed time
recorded. Run §4.1 end-to-end on staging and fill this in (this is the row that turns the runbook
from written to *proven* — it needs the running staging DB, so it is completed during S16-DO-01):

| Date | Operator | Scenario | Backup set used | Start | End | Elapsed | RTO met? | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| _pending staging_ | | full restore §4.1 | | | | | | |
| _pending staging_ | | point-in-time §4.2 | | | | | | |
| _pending staging_ | | pre-migration restore §3 | | | | | | |

## 6. Backup health checks (catch a silent failure before you need it)

- **Weekly test restore** of the latest FULL to a throwaway instance + `DBCC CHECKDB` — an untested
  backup is a hope, not a backup.
- Alert if no successful backup in the last **FULL: 26 h / LOG: 30 min** window.
- Alert if the newest backup file size deviates > 40 % from the trailing median (a truncated or empty
  backup).
- Confirm backups are stored **off the DB host** and encrypted at rest; losing the host must not lose
  the backups.

---

*Prepared under S16-DO-03. Paired with the `migrate` job in `.github/workflows/cd.yml` (pre-migration
backup + forward-only migration rollback) and the pre-flight checks in
`docs/runbooks/staging-deploy-checklist.md`.*
