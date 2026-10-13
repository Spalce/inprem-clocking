BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE TABLE [platform].[AuditEntries] (
        [Id] bigint NOT NULL IDENTITY,
        [OccurredUtc] datetime2 NOT NULL,
        [Actor] nvarchar(300) NOT NULL,
        [Action] nvarchar(100) NOT NULL,
        [Target] nvarchar(450) NULL,
        [TenantId] int NULL,
        [Details] nvarchar(2000) NULL,
        CONSTRAINT [PK_AuditEntries] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE TABLE [platform].[ProviderInvitations] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Purpose] nvarchar(30) NOT NULL,
        [TokenHash] varchar(64) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [ConsumedUtc] datetime2 NULL,
        [RevokedUtc] datetime2 NULL,
        [CreatedBy] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_ProviderInvitations] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE TABLE [platform].[ProviderUsers] (
        [UserId] nvarchar(450) NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProviderUsers] PRIMARY KEY ([UserId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE TABLE [platform].[RecoveryRecords] (
        [Id] uniqueidentifier NOT NULL,
        [Level] nvarchar(30) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [TargetUserId] nvarchar(450) NOT NULL,
        [TargetEmail] nvarchar(256) NOT NULL,
        [NewEmail] nvarchar(256) NULL,
        [Operator] nvarchar(300) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [FirstApproverEmail] nvarchar(256) NULL,
        [FirstApprovalTokenHash] varchar(64) NULL,
        [FirstApprovedUtc] datetime2 NULL,
        [FirstWaivedDelay] bit NOT NULL,
        [SecondApproverEmail] nvarchar(256) NULL,
        [SecondApprovalTokenHash] varchar(64) NULL,
        [SecondApprovedUtc] datetime2 NULL,
        [SecondWaivedDelay] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ApprovalExpiresUtc] datetime2 NOT NULL,
        [NotBeforeUtc] datetime2 NULL,
        [CompletedUtc] datetime2 NULL,
        CONSTRAINT [PK_RecoveryRecords] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE TABLE [platform].[SecurityAttempts] (
        [Id] bigint NOT NULL IDENTITY,
        [Kind] nvarchar(30) NOT NULL,
        [Succeeded] bit NOT NULL,
        [Source] nvarchar(300) NOT NULL,
        [OccurredUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SecurityAttempts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE TABLE [platform].[UsedCodes] (
        [CodeHash] varchar(64) NOT NULL,
        [Purpose] nvarchar(30) NOT NULL,
        [UsedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_UsedCodes] PRIMARY KEY ([CodeHash])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_OccurredUtc] ON [platform].[AuditEntries] ([OccurredUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProviderInvitations_TokenHash] ON [platform].[ProviderInvitations] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE INDEX [IX_ProviderInvitations_UserId] ON [platform].[ProviderInvitations] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RecoveryRecords_FirstApprovalTokenHash] ON [platform].[RecoveryRecords] ([FirstApprovalTokenHash]) WHERE [FirstApprovalTokenHash] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RecoveryRecords_SecondApprovalTokenHash] ON [platform].[RecoveryRecords] ([SecondApprovalTokenHash]) WHERE [SecondApprovalTokenHash] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE INDEX [IX_RecoveryRecords_TargetUserId_CreatedUtc] ON [platform].[RecoveryRecords] ([TargetUserId], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    CREATE INDEX [IX_SecurityAttempts_Kind_OccurredUtc] ON [platform].[SecurityAttempts] ([Kind], [OccurredUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    ALTER TABLE [platform].[ProviderUsers] ADD CONSTRAINT [FK_ProviderUsers_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]);
    ALTER TABLE [platform].[ProviderInvitations] ADD CONSTRAINT [FK_ProviderInvitations_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    EXEC(N'CREATE TRIGGER [platform].[TR_AuditEntries_AppendOnly] ON [platform].[AuditEntries]
    INSTEAD OF UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        THROW 50010, ''platform.AuditEntries is append-only: entries cannot be changed or deleted.'', 1;
    END');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013185539_M1b_ProviderSecurity', N'8.0.0');
END;
GO

COMMIT;
GO

