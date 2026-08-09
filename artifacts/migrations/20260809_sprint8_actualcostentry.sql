BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    ALTER TABLE [EvmPeriodSnapshots] DROP CONSTRAINT [CK_EvmPeriodSnapshots_Ac];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE TABLE [ActualCostEntries] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [WbsNodeId] uniqueidentifier NULL,
        [ActivityId] uniqueidentifier NULL,
        [CostCategory] int NOT NULL,
        [EntryType] int NOT NULL,
        [Source] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [IncurredDate] datetimeoffset NOT NULL,
        [PostedAt] datetimeoffset NOT NULL,
        [PostedByUserId] uniqueidentifier NOT NULL,
        [ReversesEntryId] uniqueidentifier NULL,
        [DocumentReference] nvarchar(64) NULL,
        [CostCode] nvarchar(32) NULL,
        [VendorName] nvarchar(200) NULL,
        [Note] nvarchar(500) NULL,
        [FileImportJobId] uniqueidentifier NULL,
        [PaidDate] datetimeoffset NULL,
        [Quantity] decimal(18,4) NULL,
        [UnitOfMeasure] nvarchar(16) NULL,
        CONSTRAINT [PK_ActualCostEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ActualCostEntries_Amount_NotZero] CHECK ([Amount] <> 0),
        CONSTRAINT [CK_ActualCostEntries_Quantity] CHECK ([Quantity] IS NULL OR [Quantity] >= 0),
        CONSTRAINT [FK_ActualCostEntries_Activities_ActivityId] FOREIGN KEY ([ActivityId]) REFERENCES [Activities] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ActualCostEntries_ActualCostEntries_ReversesEntryId] FOREIGN KEY ([ReversesEntryId]) REFERENCES [ActualCostEntries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ActualCostEntries_FileImportJobs_FileImportJobId] FOREIGN KEY ([FileImportJobId]) REFERENCES [FileImportJobs] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ActualCostEntries_WBSNodes_WbsNodeId] FOREIGN KEY ([WbsNodeId]) REFERENCES [WBSNodes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE INDEX [IX_ActualCostEntries_ActivityId] ON [ActualCostEntries] ([ActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE INDEX [IX_ActualCostEntries_FileImportJobId] ON [ActualCostEntries] ([FileImportJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE INDEX [IX_ActualCostEntries_ReversesEntryId] ON [ActualCostEntries] ([ReversesEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ActualCostEntries_TenantId_ActivityId_IncurredDate] ON [ActualCostEntries] ([TenantId], [ActivityId], [IncurredDate]) WHERE [ActivityId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE INDEX [IX_ActualCostEntries_TenantId_ProjectId_CostCategory_IncurredDate] ON [ActualCostEntries] ([TenantId], [ProjectId], [CostCategory], [IncurredDate]) INCLUDE ([Amount]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE INDEX [IX_ActualCostEntries_TenantId_ProjectId_IncurredDate] ON [ActualCostEntries] ([TenantId], [ProjectId], [IncurredDate]) INCLUDE ([Amount]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE INDEX [IX_ActualCostEntries_TenantId_WbsNodeId_IncurredDate] ON [ActualCostEntries] ([TenantId], [WbsNodeId], [IncurredDate]) INCLUDE ([Amount]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    CREATE INDEX [IX_ActualCostEntries_WbsNodeId] ON [ActualCostEntries] ([WbsNodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809025258_Sprint8_ActualCostEntry'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260809025258_Sprint8_ActualCostEntry', N'10.0.10');
END;

COMMIT;
GO

