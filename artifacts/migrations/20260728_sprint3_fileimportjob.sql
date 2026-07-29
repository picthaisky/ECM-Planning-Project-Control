BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728160434_Sprint3_FileImportJob'
)
BEGIN
    CREATE TABLE [FileImportJobs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(250) NOT NULL,
        [Format] int NOT NULL,
        [Status] int NOT NULL,
        [RowsImported] int NOT NULL DEFAULT 0,
        [ErrorJson] nvarchar(4000) NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [FinishedAt] datetimeoffset NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_FileImportJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_FileImportJobs_RowsImported] CHECK ([RowsImported] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728160434_Sprint3_FileImportJob'
)
BEGIN
    CREATE INDEX [IX_FileImportJobs_TenantId_ProjectId_StartedAt] ON [FileImportJobs] ([TenantId], [ProjectId], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728160434_Sprint3_FileImportJob'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728160434_Sprint3_FileImportJob', N'10.0.10');
END;

COMMIT;
GO

