BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811134718_Sprint15_ApprovalPolicy_SplitSingleActiveIndex'
)
BEGIN
    DROP INDEX [IX_ApprovalPolicies_TenantId_ProjectId_DocumentType] ON [ApprovalPolicies];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811134718_Sprint15_ApprovalPolicy_SplitSingleActiveIndex'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ApprovalPolicies_TenantId_DocumentType] ON [ApprovalPolicies] ([TenantId], [DocumentType]) WHERE [IsActive] = 1 AND [ProjectId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811134718_Sprint15_ApprovalPolicy_SplitSingleActiveIndex'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ApprovalPolicies_TenantId_ProjectId_DocumentType] ON [ApprovalPolicies] ([TenantId], [ProjectId], [DocumentType]) WHERE [IsActive] = 1 AND [ProjectId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811134718_Sprint15_ApprovalPolicy_SplitSingleActiveIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811134718_Sprint15_ApprovalPolicy_SplitSingleActiveIndex', N'10.0.10');
END;

COMMIT;
GO

