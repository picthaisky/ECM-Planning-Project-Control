BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810105012_Sprint11_DB01_IndexTuning'
)
BEGIN
    DROP INDEX [IX_DailyWeatherLogs_TenantId_ProjectId_LogDate] ON [DailyWeatherLogs];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810105012_Sprint11_DB01_IndexTuning'
)
BEGIN
    CREATE INDEX [IX_DailyWeatherLogs_TenantId_ProjectId_LogDate_RecordedAt] ON [DailyWeatherLogs] ([TenantId], [ProjectId], [LogDate] DESC, [RecordedAt] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810105012_Sprint11_DB01_IndexTuning'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810105012_Sprint11_DB01_IndexTuning', N'10.0.10');
END;

COMMIT;
GO

