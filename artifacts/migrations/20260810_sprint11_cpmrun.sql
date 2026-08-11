BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE TABLE [CpmRuns] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [CalculatedAt] datetimeoffset NOT NULL,
        [DataDate] datetimeoffset NULL,
        [ProjectDurationDays] int NOT NULL,
        [TriggeredByUserId] uniqueidentifier NULL,
        [Trigger] int NOT NULL,
        CONSTRAINT [PK_CpmRuns] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CpmRuns_ProjectDurationDays] CHECK ([ProjectDurationDays] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE TABLE [CpmRunActivities] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CpmRunId] uniqueidentifier NOT NULL,
        [ActivityId] uniqueidentifier NOT NULL,
        [DurationDays] int NOT NULL,
        [EarlyStart] int NOT NULL,
        [EarlyFinish] int NOT NULL,
        [LateStart] int NOT NULL,
        [LateFinish] int NOT NULL,
        [TotalFloat] int NOT NULL,
        [FreeFloat] int NOT NULL,
        [IsCritical] bit NOT NULL,
        CONSTRAINT [PK_CpmRunActivities] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CpmRunActivities_DurationDays] CHECK ([DurationDays] >= 0),
        CONSTRAINT [FK_CpmRunActivities_CpmRuns_CpmRunId] FOREIGN KEY ([CpmRunId]) REFERENCES [CpmRuns] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE TABLE [CpmRunRelations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CpmRunId] uniqueidentifier NOT NULL,
        [PredecessorActivityId] uniqueidentifier NOT NULL,
        [SuccessorActivityId] uniqueidentifier NOT NULL,
        [RelationType] int NOT NULL,
        [LagDays] int NOT NULL,
        CONSTRAINT [PK_CpmRunRelations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CpmRunRelations_CpmRuns_CpmRunId] FOREIGN KEY ([CpmRunId]) REFERENCES [CpmRuns] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE INDEX [IX_CpmRunActivities_CpmRunId] ON [CpmRunActivities] ([CpmRunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE INDEX [IX_CpmRunActivities_TenantId_CpmRunId] ON [CpmRunActivities] ([TenantId], [CpmRunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE INDEX [IX_CpmRunRelations_CpmRunId] ON [CpmRunRelations] ([CpmRunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE INDEX [IX_CpmRunRelations_TenantId_CpmRunId] ON [CpmRunRelations] ([TenantId], [CpmRunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    CREATE INDEX [IX_CpmRuns_TenantId_ProjectId_CalculatedAt] ON [CpmRuns] ([TenantId], [ProjectId], [CalculatedAt] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810083946_Sprint11_CpmRun'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810083946_Sprint11_CpmRun', N'10.0.10');
END;

COMMIT;
GO

