BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    CREATE TABLE [platform].[TenantMemberships] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [TenantId] int NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [InvitedByUserId] nvarchar(450) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_TenantMemberships] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    CREATE INDEX [IX_TenantMemberships_TenantId] ON [platform].[TenantMemberships] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantMemberships_UserId_TenantId] ON [platform].[TenantMemberships] ([UserId], [TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    ALTER TABLE [platform].[TenantMemberships] ADD CONSTRAINT [FK_TenantMemberships_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE;
    ALTER TABLE [platform].[TenantMemberships] ADD CONSTRAINT [FK_TenantMemberships_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    INSERT INTO [platform].[TenantMemberships] ([Id], [UserId], [TenantId], [Role], [Status], [InvitedByUserId], [CreatedUtc])
    SELECT NEWID(), u.[Id], u.[TenantId],
           CASE WHEN COUNT(*) OVER (PARTITION BY u.[TenantId]) = 1 THEN N'Owner' ELSE N'Admin' END,
           N'Active', NULL, SYSUTCDATETIME()
    FROM [dbo].[AspNetUsers] u
    WHERE u.[TenantId] IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM [platform].[TenantMemberships] m WHERE m.[UserId] = u.[Id] AND m.[TenantId] = u.[TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013083152_M2_TenantMemberships', N'8.0.0');
END;
GO

COMMIT;
GO

