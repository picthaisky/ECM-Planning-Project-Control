BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811065222_Sprint13_Idempotency'
)
BEGIN
    CREATE TABLE [IdempotencyKeys] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Key] nvarchar(200) NOT NULL,
        [RequestMethod] nvarchar(10) NOT NULL,
        [RequestPath] nvarchar(500) NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [Status] int NOT NULL,
        [RequestedByUserId] uniqueidentifier NOT NULL,
        [ResponseStatusCode] int NULL,
        [ResponseContentType] nvarchar(200) NULL,
        [ResponseBody] nvarchar(max) NULL,
        [ResponseNotReplayable] bit NOT NULL,
        [ReservedAt] datetimeoffset NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        CONSTRAINT [PK_IdempotencyKeys] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_IdempotencyKeys_ResponseBody_Length] CHECK ([ResponseBody] IS NULL OR LEN([ResponseBody]) <= 65536)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811065222_Sprint13_Idempotency'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyKeys_TenantId_Key] ON [IdempotencyKeys] ([TenantId], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811065222_Sprint13_Idempotency'
)
BEGIN
    CREATE INDEX [IX_IdempotencyKeys_TenantId_Status_CompletedAt] ON [IdempotencyKeys] ([TenantId], [Status], [CompletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811065222_Sprint13_Idempotency'
)
BEGIN
    CREATE INDEX [IX_IdempotencyKeys_TenantId_Status_ReservedAt] ON [IdempotencyKeys] ([TenantId], [Status], [ReservedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811065222_Sprint13_Idempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811065222_Sprint13_Idempotency', N'10.0.10');
END;

COMMIT;
GO

