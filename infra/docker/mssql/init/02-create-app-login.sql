-- infra/docker/mssql/init/02-create-app-login.sql
-- Idempotent: create a dedicated, least-privilege SQL login + database user for the
-- application (EF Core connection string target), distinct from `sa`. `sa` is reserved
-- for local admin/troubleshooting only and must never appear in an application
-- connection string (defense in depth, no code change required to enforce it here).
--
-- Convention decision (not dictated by design.md/CLAUDE.md): the app user is granted
-- `db_owner` on its own database only. This is deliberately simple for Phase 0/local
-- dev/staging (one app, one database, EF Core owns schema via migrations). If a
-- production hardening pass later wants to split "migration identity" (schema DDL)
-- from "runtime identity" (DML only, no DDL), that is a follow-up decision for
-- devops-engineer/security-auditor once real deployment topology is chosen (ADR-0010
-- gate) — flagged here so it isn't silently assumed away.
--
-- Skips entirely if MSSQL_APP_PASSWORD was not supplied (see entrypoint.sh), so a
-- misconfigured environment fails loudly (no app login) rather than creating one with
-- a weak/blank password.
IF N'$(AppPassword)' = N''
BEGIN
    PRINT N'[init] AppPassword not supplied, skipping app login/user creation';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppUser)')
    BEGIN
        PRINT N'[init] creating login [$(AppUser)]';
        -- CHECK_POLICY = ON (P0-SEC-01): enforces password-complexity validation at creation
        -- time as defense-in-depth on top of the operator supplying MSSQL_APP_PASSWORD. This
        -- container has no Windows domain lockout subsystem to trigger a DoS risk from the
        -- policy check, so there is no real availability trade-off in enabling it here.
        DECLARE @loginSql nvarchar(max) =
            N'CREATE LOGIN ' + QUOTENAME(N'$(AppUser)') +
            N' WITH PASSWORD = ''' + REPLACE(N'$(AppPassword)', N'''', N'''''') + N''', CHECK_POLICY = ON;';
        EXEC(@loginSql);
    END
    ELSE
    BEGIN
        PRINT N'[init] login [$(AppUser)] already exists, skipping';
    END
END
GO

USE [$(DbName)];
GO

IF N'$(AppPassword)' <> N''
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppUser)')
    BEGIN
        PRINT N'[init] creating database user [$(AppUser)] in [$(DbName)]';
        DECLARE @userSql nvarchar(max) =
            N'CREATE USER ' + QUOTENAME(N'$(AppUser)') + N' FOR LOGIN ' + QUOTENAME(N'$(AppUser)') + N';';
        EXEC(@userSql);
        EXEC sp_addrolemember N'db_owner', N'$(AppUser)';
    END
    ELSE
    BEGIN
        PRINT N'[init] database user [$(AppUser)] already exists in [$(DbName)], skipping';
    END
END
GO
