BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809003847_Sprint7_EvmPeriodSnapshot'
)
BEGIN
    CREATE TABLE [EvmPeriodSnapshots] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [DataDate] datetimeoffset NOT NULL,
        [Bac] decimal(18,2) NOT NULL,
        [Pv] decimal(18,2) NOT NULL,
        [Ev] decimal(18,2) NOT NULL,
        [Ac] decimal(18,2) NOT NULL,
        [EacVariant] int NOT NULL,
        [PerformanceFactor] decimal(9,6) NULL,
        [Eac] decimal(18,2) NULL,
        [Etc] decimal(18,2) NULL,
        [Vac] decimal(18,2) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EvmPeriodSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EvmPeriodSnapshots_Ac] CHECK ([Ac] >= 0),
        CONSTRAINT [CK_EvmPeriodSnapshots_Bac] CHECK ([Bac] >= 0),
        CONSTRAINT [CK_EvmPeriodSnapshots_Ev] CHECK ([Ev] >= 0),
        CONSTRAINT [CK_EvmPeriodSnapshots_Pv] CHECK ([Pv] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809003847_Sprint7_EvmPeriodSnapshot'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EvmPeriodSnapshots_TenantId_ProjectId_DataDate] ON [EvmPeriodSnapshots] ([TenantId], [ProjectId], [DataDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809003847_Sprint7_EvmPeriodSnapshot'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260809003847_Sprint7_EvmPeriodSnapshot', N'10.0.10');
END;

COMMIT;
GO

