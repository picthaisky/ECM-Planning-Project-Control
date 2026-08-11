BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811100442_Sprint14_Baseline'
)
BEGIN
    CREATE TABLE [Baselines] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [IsActive] bit NOT NULL,
        [CapturedAt] datetimeoffset NOT NULL,
        [CapturedByUserId] uniqueidentifier NOT NULL,
        [Bac] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_Baselines] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811100442_Sprint14_Baseline'
)
BEGIN
    CREATE TABLE [BaselineActivitySnapshots] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [BaselineId] uniqueidentifier NOT NULL,
        [ActivityId] uniqueidentifier NOT NULL,
        [PlannedStart] datetimeoffset NOT NULL,
        [PlannedFinish] datetimeoffset NOT NULL,
        [DurationDays] int NOT NULL,
        [BudgetCost] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_BaselineActivitySnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_BaselineActivitySnapshots_DurationDays] CHECK ([DurationDays] >= 0),
        CONSTRAINT [FK_BaselineActivitySnapshots_Baselines_BaselineId] FOREIGN KEY ([BaselineId]) REFERENCES [Baselines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811100442_Sprint14_Baseline'
)
BEGIN
    CREATE INDEX [IX_BaselineActivitySnapshots_BaselineId] ON [BaselineActivitySnapshots] ([BaselineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811100442_Sprint14_Baseline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BaselineActivitySnapshots_TenantId_BaselineId_ActivityId] ON [BaselineActivitySnapshots] ([TenantId], [BaselineId], [ActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811100442_Sprint14_Baseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Baselines_TenantId_ProjectId_Active] ON [Baselines] ([TenantId], [ProjectId]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811100442_Sprint14_Baseline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811100442_Sprint14_Baseline', N'10.0.10');
END;

COMMIT;
GO

