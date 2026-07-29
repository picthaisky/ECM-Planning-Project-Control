BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729095835_Sprint4_WbsNodes_CoveringIndex'
)
BEGIN
    DROP INDEX [IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId] ON [WBSNodes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729095835_Sprint4_WbsNodes_CoveringIndex'
)
BEGIN
    CREATE INDEX [IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId] ON [WBSNodes] ([TenantId], [ProjectId], [ParentWbsNodeId]) INCLUDE ([Code], [Title], [WeightPercentage]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729095835_Sprint4_WbsNodes_CoveringIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260729095835_Sprint4_WbsNodes_CoveringIndex', N'10.0.10');
END;

COMMIT;
GO

