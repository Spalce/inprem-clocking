BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE TABLE [platform].[ConsentRecords] (
        [Id] uniqueidentifier NOT NULL,
        [Phone] varchar(16) NOT NULL,
        [Action] nvarchar(20) NOT NULL,
        [TenantId] int NULL,
        [PersonRef] nvarchar(450) NULL,
        [Source] varchar(50) NOT NULL,
        [Wording] nvarchar(1000) NULL,
        [RecordedUtc] datetime2 NOT NULL,
        [RecordedBy] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_ConsentRecords] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE TABLE [platform].[Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NULL,
        [RecipientType] nvarchar(30) NOT NULL,
        [RecipientRef] nvarchar(450) NOT NULL,
        [Channel] nvarchar(10) NOT NULL,
        [Destination] nvarchar(256) NOT NULL,
        [TemplateKey] varchar(100) NOT NULL,
        [Subject] nvarchar(200) NULL,
        [Body] nvarchar(4000) NOT NULL,
        [Urgency] nvarchar(10) NOT NULL,
        [EventKey] nvarchar(200) NULL,
        [Status] nvarchar(20) NOT NULL,
        [SkipReason] varchar(50) NULL,
        [ExternalId] varchar(64) NULL,
        [Cost] decimal(10,5) NULL,
        [EstimatedCost] decimal(10,5) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [NotBeforeUtc] datetime2 NOT NULL,
        [SentUtc] datetime2 NULL,
        [FinishedUtc] datetime2 NULL,
        [Attempts] int NOT NULL,
        [Error] nvarchar(500) NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE TABLE [platform].[NotificationTemplates] (
        [Key] varchar(100) NOT NULL,
        [Channel] nvarchar(10) NOT NULL,
        [Subject] nvarchar(200) NULL,
        [Body] nvarchar(2000) NOT NULL,
        [Enabled] bit NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_NotificationTemplates] PRIMARY KEY ([Key], [Channel])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE TABLE [platform].[TenantMessagingSettings] (
        [TenantId] int NOT NULL,
        [SmsEnabled] bit NOT NULL,
        [MonthlySmsLimit] int NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_TenantMessagingSettings] PRIMARY KEY ([TenantId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE INDEX [IX_ConsentRecords_Phone_RecordedUtc] ON [platform].[ConsentRecords] ([Phone], [RecordedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE INDEX [IX_ConsentRecords_TenantId] ON [platform].[ConsentRecords] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_Channel_CreatedUtc] ON [platform].[Notifications] ([Channel], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Notifications_EventKey_RecipientRef_Channel] ON [platform].[Notifications] ([EventKey], [RecipientRef], [Channel]) WHERE [EventKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Notifications_ExternalId] ON [platform].[Notifications] ([ExternalId]) WHERE [ExternalId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_Status_NotBeforeUtc] ON [platform].[Notifications] ([Status], [NotBeforeUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_TenantId_Channel_CreatedUtc] ON [platform].[Notifications] ([TenantId], [Channel], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN

    ALTER TABLE [platform].[Notifications] ADD CONSTRAINT [FK_Notifications_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
    ALTER TABLE [platform].[TenantMessagingSettings] ADD CONSTRAINT [FK_TenantMessagingSettings_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019224539_M1m_Notifications', N'8.0.32');
END;
GO

COMMIT;
GO

