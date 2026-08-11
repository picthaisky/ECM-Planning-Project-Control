BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE TABLE [EotEvaluations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [WindowStart] datetimeoffset NOT NULL,
        [WindowEnd] datetimeoffset NOT NULL,
        [EvaluatedAt] datetimeoffset NOT NULL,
        [EvaluatedByUserId] uniqueidentifier NOT NULL,
        [CriticalityBasis] int NOT NULL,
        [Confidence] int NOT NULL,
        [AsScheduledDurationDays] int NOT NULL,
        [ImpactedDurationDays] int NOT NULL,
        [EotEligibleDays] int NOT NULL,
        [CountableStoppageDayCount] int NOT NULL,
        [DistinctCountableDateCount] int NOT NULL,
        [UnattributedStoppageDayCount] int NOT NULL,
        [ConcurrencyAssessed] bit NOT NULL,
        [EntitlementBasisAssessed] bit NOT NULL,
        [PolicySnapshotJson] nvarchar(max) NOT NULL,
        [LatestNoticeDate] datetimeoffset NULL,
        [NoticeWindowExpired] bit NULL,
        CONSTRAINT [PK_EotEvaluations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EotEvaluations_Counts] CHECK ([CountableStoppageDayCount] >= 0 AND [DistinctCountableDateCount] >= 0 AND [UnattributedStoppageDayCount] >= 0),
        CONSTRAINT [CK_EotEvaluations_Durations] CHECK ([AsScheduledDurationDays] >= 0 AND [ImpactedDurationDays] >= 0 AND [EotEligibleDays] >= 0),
        CONSTRAINT [CK_EotEvaluations_Window] CHECK ([WindowEnd] >= [WindowStart])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE TABLE [ProjectEotPolicies] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [PartialDayPolicy] int NOT NULL,
        [FullDayHours] decimal(4,2) NOT NULL,
        [MinHoursLostForCountableDay] decimal(4,2) NOT NULL,
        [MinRainfallMmForCountableDay] decimal(6,2) NULL,
        [CountUnmeasuredRainfallWhenThresholdSet] bit NOT NULL,
        [NoticePeriodDays] int NULL,
        CONSTRAINT [PK_ProjectEotPolicies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProjectEotPolicies_FullDayHours] CHECK ([FullDayHours] > 0 AND [FullDayHours] <= 24),
        CONSTRAINT [CK_ProjectEotPolicies_MinHoursLostForCountableDay] CHECK ([MinHoursLostForCountableDay] >= 0 AND [MinHoursLostForCountableDay] <= 24),
        CONSTRAINT [CK_ProjectEotPolicies_MinRainfallMmForCountableDay] CHECK ([MinRainfallMmForCountableDay] IS NULL OR [MinRainfallMmForCountableDay] >= 0),
        CONSTRAINT [CK_ProjectEotPolicies_NoticePeriodDays] CHECK ([NoticePeriodDays] IS NULL OR [NoticePeriodDays] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE TABLE [EotEvaluationDrivers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EotEvaluationId] uniqueidentifier NOT NULL,
        [CpmRunId] uniqueidentifier NOT NULL,
        [ActivityId] uniqueidentifier NOT NULL,
        [ActivityCode] nvarchar(50) NOT NULL,
        [ActivityName] nvarchar(250) NOT NULL,
        [StoppageDays] int NOT NULL,
        [TotalFloatAtRun] int NOT NULL,
        [WasCriticalAtRun] bit NOT NULL,
        [IsOnImpactedCriticalPath] bit NOT NULL,
        [IndicativeEotDays] int NOT NULL,
        [MarginalEotDays] int NOT NULL,
        [RemainingFloatAfter] int NOT NULL,
        [UnclaimedFractionalHours] decimal(6,2) NULL,
        CONSTRAINT [PK_EotEvaluationDrivers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EotEvaluationDrivers_Counts] CHECK ([StoppageDays] >= 0 AND [IndicativeEotDays] >= 0 AND [MarginalEotDays] >= 0 AND [RemainingFloatAfter] >= 0),
        CONSTRAINT [FK_EotEvaluationDrivers_EotEvaluations_EotEvaluationId] FOREIGN KEY ([EotEvaluationId]) REFERENCES [EotEvaluations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE TABLE [EotEvaluationRuns] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EotEvaluationId] uniqueidentifier NOT NULL,
        [CpmRunId] uniqueidentifier NOT NULL,
        [WindowFrom] datetimeoffset NOT NULL,
        [WindowTo] datetimeoffset NOT NULL,
        [AsScheduledDurationDays] int NOT NULL,
        [ImpactedDurationDays] int NOT NULL,
        [DeltaDays] int NOT NULL,
        CONSTRAINT [PK_EotEvaluationRuns] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EotEvaluationRuns_Window] CHECK ([WindowTo] >= [WindowFrom]),
        CONSTRAINT [FK_EotEvaluationRuns_EotEvaluations_EotEvaluationId] FOREIGN KEY ([EotEvaluationId]) REFERENCES [EotEvaluations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE TABLE [EotEvaluationSources] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EotEvaluationId] uniqueidentifier NOT NULL,
        [DailyWeatherLogId] uniqueidentifier NOT NULL,
        [CountableDays] decimal(5,2) NOT NULL,
        [ExclusionReason] int NULL,
        CONSTRAINT [PK_EotEvaluationSources] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EotEvaluationSources_CountableDays] CHECK ([CountableDays] >= 0),
        CONSTRAINT [FK_EotEvaluationSources_EotEvaluations_EotEvaluationId] FOREIGN KEY ([EotEvaluationId]) REFERENCES [EotEvaluations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationDrivers_EotEvaluationId] ON [EotEvaluationDrivers] ([EotEvaluationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationDrivers_TenantId_EotEvaluationId] ON [EotEvaluationDrivers] ([TenantId], [EotEvaluationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationRuns_EotEvaluationId] ON [EotEvaluationRuns] ([EotEvaluationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationRuns_TenantId_CpmRunId] ON [EotEvaluationRuns] ([TenantId], [CpmRunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationRuns_TenantId_EotEvaluationId] ON [EotEvaluationRuns] ([TenantId], [EotEvaluationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluations_TenantId_ProjectId_EvaluatedAt] ON [EotEvaluations] ([TenantId], [ProjectId], [EvaluatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationSources_EotEvaluationId] ON [EotEvaluationSources] ([EotEvaluationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationSources_TenantId_DailyWeatherLogId] ON [EotEvaluationSources] ([TenantId], [DailyWeatherLogId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE INDEX [IX_EotEvaluationSources_TenantId_EotEvaluationId] ON [EotEvaluationSources] ([TenantId], [EotEvaluationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProjectEotPolicies_TenantId_ProjectId] ON [ProjectEotPolicies] ([TenantId], [ProjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810092411_Sprint11_EotEvaluation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810092411_Sprint11_EotEvaluation', N'10.0.10');
END;

COMMIT;
GO

