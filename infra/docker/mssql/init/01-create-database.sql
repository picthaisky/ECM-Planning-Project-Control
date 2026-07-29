-- infra/docker/mssql/init/01-create-database.sql
-- Idempotent: create the CM+ application database if it does not already exist.
-- Collation: SQL_Latin1_General_CP1_CI_AS (case-insensitive, accent-sensitive) — the
-- SQL Server default. Thai text is stored via NVARCHAR (Unicode code points), so no
-- Thai-specific collation is required; only comparison/sort semantics are affected.
--
-- Variable $(DbName) is passed in by entrypoint.sh via `sqlcmd -v DbName=...`.
IF DB_ID(N'$(DbName)') IS NULL
BEGIN
    PRINT N'[init] creating database [$(DbName)]';
    DECLARE @sql nvarchar(max) =
        N'CREATE DATABASE ' + QUOTENAME(N'$(DbName)') + N' COLLATE SQL_Latin1_General_CP1_CI_AS;';
    EXEC(@sql);
END
ELSE
BEGIN
    PRINT N'[init] database [$(DbName)] already exists, skipping';
END
GO
