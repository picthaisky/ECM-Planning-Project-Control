BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810143814_Sprint12_Photo'
)
BEGIN
    CREATE TABLE [Photos] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [ActivityId] uniqueidentifier NULL,
        [Caption] nvarchar(500) NULL,
        [Format] int NOT NULL,
        [FileSizeBytes] bigint NOT NULL,
        [StorageKey] nvarchar(400) NOT NULL,
        [UploadedByUserId] uniqueidentifier NOT NULL,
        [UploadedAt] datetimeoffset NOT NULL,
        [CapturedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Photos] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Photos_FileSizeBytes_Positive] CHECK ([FileSizeBytes] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810143814_Sprint12_Photo'
)
BEGIN
    CREATE INDEX [IX_Photos_TenantId_ProjectId_ActivityId] ON [Photos] ([TenantId], [ProjectId], [ActivityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810143814_Sprint12_Photo'
)
BEGIN
    CREATE INDEX [IX_Photos_TenantId_ProjectId_UploadedAt] ON [Photos] ([TenantId], [ProjectId], [UploadedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810143814_Sprint12_Photo'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810143814_Sprint12_Photo', N'10.0.10');
END;

COMMIT;
GO

