IF OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'platform') IS NULL EXEC(N'CREATE SCHEMA [platform];');
    CREATE TABLE [platform].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013080438_M1a_DataProtectionKeys'
)
BEGIN
    IF SCHEMA_ID(N'platform') IS NULL EXEC(N'CREATE SCHEMA [platform];');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013080438_M1a_DataProtectionKeys'
)
BEGIN
    CREATE TABLE [platform].[DataProtectionKeys] (
        [Id] int NOT NULL IDENTITY,
        [FriendlyName] nvarchar(max) NULL,
        [Xml] nvarchar(max) NULL,
        CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013080438_M1a_DataProtectionKeys'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013080438_M1a_DataProtectionKeys', N'8.0.0');
END;
GO

COMMIT;
GO

