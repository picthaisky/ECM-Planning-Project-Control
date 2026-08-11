BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger'
)
BEGIN
    CREATE TABLE [PaymentCertificates] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [MilestoneNo] int NOT NULL,
        [Description] nvarchar(500) NULL,
        [MilestoneValue] decimal(18,2) NOT NULL,
        [PreviousCumulativeApprovePct] decimal(5,2) NOT NULL,
        [ApprovePct] decimal(5,2) NOT NULL,
        [ClaimPct] decimal(5,2) NULL,
        [ActualProgressPct] decimal(5,2) NULL,
        [GrossCertifiedAmount] decimal(18,2) NOT NULL,
        [RetentionAmount] decimal(18,2) NOT NULL,
        [AdvanceRecoveryAmount] decimal(18,2) NOT NULL,
        [NetPayment] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [RevisionNo] int NOT NULL,
        [CurrentStepNo] int NOT NULL,
        [TotalSteps] int NOT NULL,
        [ApprovalPolicyId] uniqueidentifier NULL,
        [ApprovalPolicyVersion] int NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [SubmittedByUserId] uniqueidentifier NULL,
        [SubmittedAt] datetimeoffset NULL,
        [CertifiedAt] datetimeoffset NULL,
        [PaidAt] datetimeoffset NULL,
        [PaymentReference] nvarchar(64) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_PaymentCertificates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PaymentCertificates_ApprovePct] CHECK ([ApprovePct] >= 0 AND [ApprovePct] <= 100),
        CONSTRAINT [CK_PaymentCertificates_GrossCertifiedAmount] CHECK ([GrossCertifiedAmount] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger'
)
BEGIN
    CREATE TABLE [ProjectFinanceLedgers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [PaymentCertificateId] uniqueidentifier NULL,
        [Category] int NOT NULL,
        [EntryType] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [EffectiveDate] datetimeoffset NOT NULL,
        [Reference] nvarchar(64) NULL,
        [Note] nvarchar(500) NULL,
        CONSTRAINT [PK_ProjectFinanceLedgers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProjectFinanceLedgers_Amount_NotZero] CHECK ([Amount] <> 0),
        CONSTRAINT [FK_ProjectFinanceLedgers_PaymentCertificates_PaymentCertificateId] FOREIGN KEY ([PaymentCertificateId]) REFERENCES [PaymentCertificates] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger'
)
BEGIN
    CREATE INDEX [IX_PaymentCertificates_TenantId_ProjectId_MilestoneNo] ON [PaymentCertificates] ([TenantId], [ProjectId], [MilestoneNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger'
)
BEGIN
    CREATE INDEX [IX_PaymentCertificates_TenantId_ProjectId_Status] ON [PaymentCertificates] ([TenantId], [ProjectId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger'
)
BEGIN
    CREATE INDEX [IX_ProjectFinanceLedgers_PaymentCertificateId] ON [ProjectFinanceLedgers] ([PaymentCertificateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger'
)
BEGIN
    CREATE INDEX [IX_ProjectFinanceLedgers_TenantId_ProjectId_Category] ON [ProjectFinanceLedgers] ([TenantId], [ProjectId], [Category]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260809063509_Sprint9_PaymentCertificate_ProjectFinanceLedger', N'10.0.10');
END;

COMMIT;
GO

