BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    CREATE TABLE [platform].[ApiTokens] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [TokenHash] varchar(64) NOT NULL,
        [SignupId] uniqueidentifier NULL,
        [TenantId] int NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [RevokedUtc] datetime2 NULL,
        CONSTRAINT [PK_ApiTokens] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    CREATE TABLE [platform].[IdempotencyRecords] (
        [Scope] nvarchar(200) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [RequestHash] varchar(64) NOT NULL,
        [StatusCode] int NULL,
        [ContentType] nvarchar(100) NULL,
        [ResponseBody] nvarchar(max) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Scope], [Key])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    CREATE INDEX [IX_ApiTokens_ExpiresUtc] ON [platform].[ApiTokens] ([ExpiresUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    CREATE INDEX [IX_ApiTokens_SignupId] ON [platform].[ApiTokens] ([SignupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApiTokens_TokenHash] ON [platform].[ApiTokens] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_ExpiresUtc] ON [platform].[IdempotencyRecords] ([ExpiresUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015175927_M1h_PublicApi', N'8.0.32');
END;
GO

COMMIT;
GO

