BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE TABLE [DailyWeatherLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [LogDate] datetimeoffset NOT NULL,
        [Condition] int NOT NULL,
        [ConditionNote] nvarchar(200) NULL,
        [RainfallMm] decimal(6,2) NULL,
        [Impact] int NOT NULL,
        [ImpactNote] nvarchar(500) NULL,
        [HoursLost] decimal(4,2) NULL,
        [RecordedByUserId] uniqueidentifier NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        [EntryKind] int NOT NULL,
        [CorrectsWeatherLogId] uniqueidentifier NULL,
        [CorrectionReason] nvarchar(500) NULL,
        CONSTRAINT [PK_DailyWeatherLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DailyWeatherLogs_HoursLost] CHECK ([HoursLost] IS NULL OR [HoursLost] BETWEEN 0 AND 24),
        CONSTRAINT [CK_DailyWeatherLogs_RainfallMm] CHECK ([RainfallMm] IS NULL OR [RainfallMm] >= 0),
        CONSTRAINT [FK_DailyWeatherLogs_DailyWeatherLogs_CorrectsWeatherLogId] FOREIGN KEY ([CorrectsWeatherLogId]) REFERENCES [DailyWeatherLogs] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE TABLE [IssueLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Detail] nvarchar(2000) NULL,
        [Owner] nvarchar(200) NULL,
        [DueDate] datetimeoffset NULL,
        [Status] int NOT NULL,
        [StartedAt] datetimeoffset NULL,
        [ClosedAt] datetimeoffset NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_IssueLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_IssueLogs_ClosedAt_Matches_Status] CHECK (([Status] = 3 AND [ClosedAt] IS NOT NULL) OR ([Status] <> 3 AND [ClosedAt] IS NULL)),
        CONSTRAINT [CK_IssueLogs_StartedAt_Before_ClosedAt] CHECK ([StartedAt] IS NULL OR [ClosedAt] IS NULL OR [StartedAt] <= [ClosedAt]),
        CONSTRAINT [CK_IssueLogs_StartedAt_Matches_Status] CHECK (([Status] IN (2, 3) AND [StartedAt] IS NOT NULL) OR ([Status] = 1 AND [StartedAt] IS NULL))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE TABLE [DailyWeatherLogActivities] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [DailyWeatherLogId] uniqueidentifier NOT NULL,
        [ActivityId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_DailyWeatherLogActivities] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DailyWeatherLogActivities_Activities_ActivityId] FOREIGN KEY ([ActivityId]) REFERENCES [Activities] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DailyWeatherLogActivities_DailyWeatherLogs_DailyWeatherLogId] FOREIGN KEY ([DailyWeatherLogId]) REFERENCES [DailyWeatherLogs] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE INDEX [IX_DailyWeatherLogActivities_ActivityId] ON [DailyWeatherLogActivities] ([ActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE INDEX [IX_DailyWeatherLogActivities_DailyWeatherLogId] ON [DailyWeatherLogActivities] ([DailyWeatherLogId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE INDEX [IX_DailyWeatherLogActivities_TenantId_ActivityId] ON [DailyWeatherLogActivities] ([TenantId], [ActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DailyWeatherLogActivities_TenantId_DailyWeatherLogId_ActivityId] ON [DailyWeatherLogActivities] ([TenantId], [DailyWeatherLogId], [ActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE INDEX [IX_DailyWeatherLogs_CorrectsWeatherLogId] ON [DailyWeatherLogs] ([CorrectsWeatherLogId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DailyWeatherLogs_TenantId_CorrectsWeatherLogId] ON [DailyWeatherLogs] ([TenantId], [CorrectsWeatherLogId]) WHERE [CorrectsWeatherLogId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE INDEX [IX_DailyWeatherLogs_TenantId_ProjectId_LogDate] ON [DailyWeatherLogs] ([TenantId], [ProjectId], [LogDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE INDEX [IX_IssueLogs_TenantId_ProjectId_CreatedAt] ON [IssueLogs] ([TenantId], [ProjectId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    CREATE INDEX [IX_IssueLogs_TenantId_ProjectId_Status] ON [IssueLogs] ([TenantId], [ProjectId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810075645_Sprint11_DailyWeatherLog_IssueLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810075645_Sprint11_DailyWeatherLog_IssueLog', N'10.0.10');
END;

COMMIT;
GO

