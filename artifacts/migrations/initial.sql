IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [Activities] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [WbsNodeId] uniqueidentifier NOT NULL,
        [ActivityCode] nvarchar(50) NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [PlannedStart] datetimeoffset NOT NULL,
        [PlannedFinish] datetimeoffset NOT NULL,
        [ActualStart] datetimeoffset NULL,
        [ActualFinish] datetimeoffset NULL,
        [DurationDays] int NOT NULL,
        [BudgetCost] decimal(18,2) NOT NULL,
        [ProgressPercentage] decimal(5,2) NOT NULL,
        [LatestProgressPeriodEndDate] datetimeoffset NULL,
        [LatestProgressRecordedAt] datetimeoffset NULL,
        [IsCritical] bit NOT NULL,
        [TotalFloat] int NULL,
        [FreeFloat] int NULL,
        CONSTRAINT [PK_Activities] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Activities_BudgetCost] CHECK ([BudgetCost] >= 0),
        CONSTRAINT [CK_Activities_DurationDays] CHECK ([DurationDays] >= 0),
        CONSTRAINT [CK_Activities_ProgressPercentage] CHECK ([ProgressPercentage] BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [ActivityRelations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PredecessorActivityId] uniqueidentifier NOT NULL,
        [SuccessorActivityId] uniqueidentifier NOT NULL,
        [RelationType] int NOT NULL,
        [LagDays] int NOT NULL,
        CONSTRAINT [PK_ActivityRelations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [Calendars] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [WorkingDays] int NOT NULL,
        [IsDefault] bit NOT NULL,
        CONSTRAINT [PK_Calendars] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [Projects] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Owner] nvarchar(250) NOT NULL,
        [ContractStart] datetimeoffset NOT NULL,
        [ContractFinish] datetimeoffset NOT NULL,
        [BAC] decimal(18,2) NOT NULL,
        [RetentionRate] decimal(5,2) NULL,
        [AdvanceRate] decimal(5,2) NULL,
        [DataDate] datetimeoffset NOT NULL,
        [EacVariantDefault] int NOT NULL,
        [EacCustomPerformanceFactor] decimal(9,4) NULL,
        [EacManualEtc] decimal(18,2) NULL,
        [ContractValue] decimal(18,2) NOT NULL,
        [RetentionCapPercentage] decimal(5,2) NULL,
        [RetentionRelease1Percentage] decimal(5,2) NOT NULL,
        [DefectsLiabilityMonths] int NULL,
        [AdvanceAmountPaid] decimal(18,2) NULL,
        [AdvanceRecoveryMethod] int NOT NULL,
        [AdvanceRecoveryStartPct] decimal(5,2) NULL,
        [AdvanceRecoveryRatePct] decimal(5,2) NULL,
        [AdvanceRecoveryEndPct] decimal(5,2) NULL,
        CONSTRAINT [PK_Projects] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Projects_AdvanceAmountPaid] CHECK ([AdvanceAmountPaid] >= 0),
        CONSTRAINT [CK_Projects_AdvanceRate] CHECK ([AdvanceRate] BETWEEN 0 AND 100),
        CONSTRAINT [CK_Projects_AdvanceRecoveryEndPct] CHECK ([AdvanceRecoveryEndPct] BETWEEN 0 AND 100),
        CONSTRAINT [CK_Projects_AdvanceRecoveryRatePct] CHECK ([AdvanceRecoveryRatePct] BETWEEN 0 AND 100),
        CONSTRAINT [CK_Projects_AdvanceRecoveryStartPct] CHECK ([AdvanceRecoveryStartPct] BETWEEN 0 AND 100),
        CONSTRAINT [CK_Projects_BAC] CHECK ([BAC] >= 0),
        CONSTRAINT [CK_Projects_ContractValue] CHECK ([ContractValue] >= 0),
        CONSTRAINT [CK_Projects_EacCustomPerformanceFactor] CHECK ([EacCustomPerformanceFactor] > 0),
        CONSTRAINT [CK_Projects_EacManualEtc] CHECK ([EacManualEtc] >= 0),
        CONSTRAINT [CK_Projects_RetentionCapPercentage] CHECK ([RetentionCapPercentage] BETWEEN 0 AND 100),
        CONSTRAINT [CK_Projects_RetentionRate] CHECK ([RetentionRate] BETWEEN 0 AND 100),
        CONSTRAINT [CK_Projects_RetentionRelease1Percentage] CHECK ([RetentionRelease1Percentage] BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [Status] int NOT NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Email] nvarchar(250) NOT NULL,
        [Role] int NOT NULL,
        [PasswordHash] nvarchar(250) NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [WBSNodes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [ParentWbsNodeId] uniqueidentifier NULL,
        [Code] nvarchar(50) NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [WeightPercentage] decimal(5,2) NOT NULL,
        CONSTRAINT [PK_WBSNodes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_WBSNodes_WeightPercentage] CHECK ([WeightPercentage] BETWEEN 0 AND 100),
        CONSTRAINT [FK_WBSNodes_WBSNodes_ParentWbsNodeId] FOREIGN KEY ([ParentWbsNodeId]) REFERENCES [WBSNodes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [ActivityProgressLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ActivityId] uniqueidentifier NOT NULL,
        [PeriodEndDate] datetimeoffset NOT NULL,
        [ProgressPercentage] decimal(5,2) NOT NULL,
        [ActualQuantity] decimal(18,2) NULL,
        [RecordedByUserId] uniqueidentifier NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        [Source] int NOT NULL,
        CONSTRAINT [PK_ActivityProgressLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ActivityProgressLogs_ActualQuantity] CHECK ([ActualQuantity] >= 0),
        CONSTRAINT [CK_ActivityProgressLogs_ProgressPercentage] CHECK ([ProgressPercentage] BETWEEN 0 AND 100),
        CONSTRAINT [FK_ActivityProgressLogs_Activities_ActivityId] FOREIGN KEY ([ActivityId]) REFERENCES [Activities] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE TABLE [CalendarExceptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CalendarId] uniqueidentifier NOT NULL,
        [Date] datetimeoffset NOT NULL,
        [IsWorkingDay] bit NOT NULL,
        [Description] nvarchar(250) NULL,
        CONSTRAINT [PK_CalendarExceptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CalendarExceptions_Calendars_CalendarId] FOREIGN KEY ([CalendarId]) REFERENCES [Calendars] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Activities_TenantId_WbsNodeId] ON [Activities] ([TenantId], [WbsNodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityProgressLogs_ActivityId] ON [ActivityProgressLogs] ([ActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityProgressLogs_TenantId_ActivityId_PeriodEndDate] ON [ActivityProgressLogs] ([TenantId], [ActivityId], [PeriodEndDate] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityProgressLogs_TenantId_PeriodEndDate] ON [ActivityProgressLogs] ([TenantId], [PeriodEndDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityRelations_TenantId_PredecessorActivityId] ON [ActivityRelations] ([TenantId], [PredecessorActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivityRelations_TenantId_SuccessorActivityId] ON [ActivityRelations] ([TenantId], [SuccessorActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CalendarExceptions_CalendarId] ON [CalendarExceptions] ([CalendarId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CalendarExceptions_TenantId_CalendarId] ON [CalendarExceptions] ([TenantId], [CalendarId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Calendars_TenantId_ProjectId] ON [Calendars] ([TenantId], [ProjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Projects_TenantId_Code] ON [Projects] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_TenantId_Email] ON [Users] ([TenantId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WBSNodes_ParentWbsNodeId] ON [WBSNodes] ([ParentWbsNodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId] ON [WBSNodes] ([TenantId], [ProjectId], [ParentWbsNodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728023528_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728023528_InitialCreate', N'10.0.10');
END;

COMMIT;
GO

