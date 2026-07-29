BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE TABLE [ApprovalActions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [DocumentType] int NOT NULL,
        [DocumentId] uniqueidentifier NOT NULL,
        [RevisionNo] int NOT NULL,
        [StepNo] int NOT NULL,
        [ActorUserId] uniqueidentifier NOT NULL,
        [ActorRoleAtTime] int NOT NULL,
        [Action] int NOT NULL,
        [Comment] nvarchar(2000) NULL,
        [ActedAt] datetimeoffset NOT NULL,
        [ApprovalPolicyId] uniqueidentifier NOT NULL,
        [ApprovalPolicyVersion] int NOT NULL,
        CONSTRAINT [PK_ApprovalActions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE TABLE [ApprovalPolicies] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NULL,
        [DocumentType] int NOT NULL,
        [Version] int NOT NULL,
        [IsActive] bit NOT NULL,
        [EffectiveFrom] datetimeoffset NOT NULL,
        [EffectiveTo] datetimeoffset NULL,
        [AllowSelfApproval] bit NOT NULL,
        [CumulativeVoEscalationPct] decimal(5,2) NULL,
        [CumulativeVoEscalationRole] int NULL,
        CONSTRAINT [PK_ApprovalPolicies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApprovalPolicies_CumulativeVoEscalationPct] CHECK ([CumulativeVoEscalationPct] IS NULL OR [CumulativeVoEscalationPct] BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EntityName] nvarchar(200) NOT NULL,
        [EntityId] uniqueidentifier NOT NULL,
        [Action] int NOT NULL,
        [UserId] uniqueidentifier NULL,
        [BeforeJson] nvarchar(max) NULL,
        [AfterJson] nvarchar(max) NULL,
        [Timestamp] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE TABLE [ApprovalPolicyRules] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ApprovalPolicyId] uniqueidentifier NOT NULL,
        [StepNo] int NOT NULL,
        [MinAmount] decimal(18,2) NOT NULL,
        [MaxAmount] decimal(18,2) NULL,
        [RequiredRole] int NOT NULL,
        [RequiredUserId] uniqueidentifier NULL,
        [QuorumCount] int NOT NULL,
        CONSTRAINT [PK_ApprovalPolicyRules] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApprovalPolicyRules_MaxAmount] CHECK ([MaxAmount] IS NULL OR [MaxAmount] > [MinAmount]),
        CONSTRAINT [CK_ApprovalPolicyRules_MinAmount] CHECK ([MinAmount] >= 0),
        CONSTRAINT [CK_ApprovalPolicyRules_QuorumCount] CHECK ([QuorumCount] >= 1),
        CONSTRAINT [CK_ApprovalPolicyRules_StepNo] CHECK ([StepNo] >= 1),
        CONSTRAINT [FK_ApprovalPolicyRules_ApprovalPolicies_ApprovalPolicyId] FOREIGN KEY ([ApprovalPolicyId]) REFERENCES [ApprovalPolicies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE INDEX [IX_ApprovalActions_TenantId_DocumentType_DocumentId_RevisionNo_StepNo] ON [ApprovalActions] ([TenantId], [DocumentType], [DocumentId], [RevisionNo], [StepNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ApprovalPolicies_TenantId_ProjectId_DocumentType] ON [ApprovalPolicies] ([TenantId], [ProjectId], [DocumentType]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE INDEX [IX_ApprovalPolicyRules_ApprovalPolicyId] ON [ApprovalPolicyRules] ([ApprovalPolicyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE INDEX [IX_ApprovalPolicyRules_TenantId_ApprovalPolicyId_StepNo] ON [ApprovalPolicyRules] ([TenantId], [ApprovalPolicyId], [StepNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId_EntityName_EntityId] ON [AuditLogs] ([TenantId], [EntityName], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId_Timestamp] ON [AuditLogs] ([TenantId], [Timestamp]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728074928_Sprint2_Auth_Audit_ApprovalPolicy', N'10.0.10');
END;

COMMIT;
GO

