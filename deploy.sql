IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [FirstName] nvarchar(max) NULL,
        [MiddleName] nvarchar(max) NULL,
        [LastName] nvarchar(max) NULL,
        [Type] nvarchar(max) NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [Setting] (
        [Id] int NOT NULL IDENTITY,
        [Action] bit NOT NULL,
        [Duration] int NOT NULL,
        [Name] nvarchar(max) NULL,
        CONSTRAINT [PK_Setting] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [Staffs] (
        [StaffId] bigint NOT NULL IDENTITY,
        [EmailAddress] nvarchar(100) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [ZipCode] nvarchar(max) NOT NULL,
        [Gender] nvarchar(1) NOT NULL,
        [Type] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(20) NOT NULL,
        [Address] nvarchar(255) NULL,
        [CreatedAt] datetime2 NULL,
        CONSTRAINT [PK_Staffs] PRIMARY KEY ([StaffId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [Volunteers] (
        [VolunteerId] bigint NOT NULL IDENTITY,
        [EmailAddress] nvarchar(100) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [ZipCode] nvarchar(max) NOT NULL,
        [Gender] nvarchar(1) NOT NULL,
        [Type] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(20) NOT NULL,
        [Address] nvarchar(255) NULL,
        [CreatedAt] datetime2 NULL,
        CONSTRAINT [PK_Volunteers] PRIMARY KEY ([VolunteerId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(128) NOT NULL,
        [ProviderKey] nvarchar(128) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(128) NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [ClockingsStaff] (
        [ClockingStaffId] bigint NOT NULL IDENTITY,
        [StafId] bigint NOT NULL,
        [FullName] nvarchar(max) NULL,
        [ClockInTime] datetime2 NULL,
        [ClockOutTime] datetime2 NULL,
        [LeaveOnBreakTime] datetime2 NULL,
        [ReturnOnBreakTime] datetime2 NULL,
        [WorkingHours] time NULL,
        [CreatedAt] datetime2 NULL,
        CONSTRAINT [PK_ClockingsStaff] PRIMARY KEY ([ClockingStaffId]),
        CONSTRAINT [FK_ClockingsStaff_Staffs_StafId] FOREIGN KEY ([StafId]) REFERENCES [Staffs] ([StaffId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE TABLE [Clockings] (
        [ClockingId] bigint NOT NULL IDENTITY,
        [VoluntId] bigint NOT NULL,
        [FullName] nvarchar(max) NULL,
        [ClockInTime] datetime2 NULL,
        [ClockOutTime] datetime2 NULL,
        [LeaveOnBreakTime] datetime2 NULL,
        [ReturnOnBreakTime] datetime2 NULL,
        [WorkingHours] time NULL,
        [CreatedAt] datetime2 NULL,
        CONSTRAINT [PK_Clockings] PRIMARY KEY ([ClockingId]),
        CONSTRAINT [FK_Clockings_Volunteers_VoluntId] FOREIGN KEY ([VoluntId]) REFERENCES [Volunteers] ([VolunteerId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Clockings_VoluntId] ON [Clockings] ([VoluntId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClockingsStaff_StafId] ON [ClockingsStaff] ([StafId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909115551_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909115551_InitialCreate', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155246_AddVolunteerCategoryFields'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [ContactPerson] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155246_AddVolunteerCategoryFields'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [InstitutionName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155246_AddVolunteerCategoryFields'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [MandateType] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155246_AddVolunteerCategoryFields'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [PlaceOfWork] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155246_AddVolunteerCategoryFields'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [VolunteerCategory] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155246_AddVolunteerCategoryFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925155246_AddVolunteerCategoryFields', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155854_UseAppRoleForIdentity'
)
BEGIN
    ALTER TABLE [AspNetRoles] ADD [Description] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925155854_UseAppRoleForIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925155854_UseAppRoleForIdentity', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN
    DROP INDEX [IX_ClockingsStaff_StafId] ON [ClockingsStaff];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN
    DROP INDEX [IX_Clockings_VoluntId] ON [Clockings];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD [ClockDate] date NOT NULL DEFAULT '0001-01-01';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN
    ALTER TABLE [Clockings] ADD [ClockDate] date NOT NULL DEFAULT '0001-01-01';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN

                    UPDATE ClockingsStaff
                    SET ClockDate = CAST(CreatedAt AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS DATE)
                    WHERE CreatedAt IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN

                    UPDATE Clockings
                    SET ClockDate = CAST(CreatedAt AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS DATE)
                    WHERE CreatedAt IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClockingsStaff_StafId_ClockDate] ON [ClockingsStaff] ([StafId], [ClockDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clockings_VoluntId_ClockDate] ON [Clockings] ([VoluntId], [ClockDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925172433_AddClockDateUniquePerDay'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925172433_AddClockDateUniquePerDay', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928094128_AddUniqueEmailIndexes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Volunteers_EmailAddress] ON [Volunteers] ([EmailAddress]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928094128_AddUniqueEmailIndexes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Staffs_EmailAddress] ON [Staffs] ([EmailAddress]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928094128_AddUniqueEmailIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260928094128_AddUniqueEmailIndexes', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Staffs] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Setting] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Clockings] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Volunteers_TenantId] ON [Volunteers] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Staffs_TenantId] ON [Staffs] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Setting_TenantId] ON [Setting] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_ClockingsStaff_TenantId] ON [ClockingsStaff] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Clockings_TenantId] ON [Clockings] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD CONSTRAINT [FK_AspNetUsers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Clockings] ADD CONSTRAINT [FK_Clockings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD CONSTRAINT [FK_ClockingsStaff_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Setting] ADD CONSTRAINT [FK_Setting_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Staffs] ADD CONSTRAINT [FK_Staffs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Volunteers] ADD CONSTRAINT [FK_Volunteers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053332_MultiTenancy_Phase1a_BackfillTenantData'
)
BEGIN

                    INSERT INTO [Tenants] ([Name], [TimeZoneId], [IsActive], [CreatedAt])
                    VALUES (N'Inprem Holistic Community Resource Center', N'America/New_York', 1, SYSUTCDATETIME());

                    DECLARE @TenantId INT = SCOPE_IDENTITY();

                    UPDATE [Staffs] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [Volunteers] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [ClockingsStaff] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [Clockings] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [Setting] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [AspNetUsers] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053332_MultiTenancy_Phase1a_BackfillTenantData'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929053332_MultiTenancy_Phase1a_BackfillTenantData', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Volunteers_EmailAddress] ON [Volunteers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Volunteers_TenantId] ON [Volunteers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Staffs_EmailAddress] ON [Staffs];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Staffs_TenantId] ON [Staffs];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Setting_TenantId] ON [Setting];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_ClockingsStaff_StafId_ClockDate] ON [ClockingsStaff];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_ClockingsStaff_TenantId] ON [ClockingsStaff];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Clockings_TenantId] ON [Clockings];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Clockings_VoluntId_ClockDate] ON [Clockings];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Volunteers]') AND [c].[name] = N'TenantId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Volunteers] DROP CONSTRAINT [' + @var0 + '];');
    EXEC(N'UPDATE [Volunteers] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Volunteers] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Volunteers] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Staffs]') AND [c].[name] = N'TenantId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Staffs] DROP CONSTRAINT [' + @var1 + '];');
    EXEC(N'UPDATE [Staffs] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Staffs] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Staffs] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Setting]') AND [c].[name] = N'TenantId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Setting] DROP CONSTRAINT [' + @var2 + '];');
    EXEC(N'UPDATE [Setting] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Setting] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Setting] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ClockingsStaff]') AND [c].[name] = N'TenantId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [ClockingsStaff] DROP CONSTRAINT [' + @var3 + '];');
    EXEC(N'UPDATE [ClockingsStaff] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [ClockingsStaff] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [ClockingsStaff] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Clockings]') AND [c].[name] = N'TenantId');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Clockings] DROP CONSTRAINT [' + @var4 + '];');
    EXEC(N'UPDATE [Clockings] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Clockings] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Clockings] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Volunteers_TenantId_EmailAddress] ON [Volunteers] ([TenantId], [EmailAddress]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Staffs_TenantId_EmailAddress] ON [Staffs] ([TenantId], [EmailAddress]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Setting_TenantId] ON [Setting] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE INDEX [IX_ClockingsStaff_StafId] ON [ClockingsStaff] ([StafId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClockingsStaff_TenantId_StafId_ClockDate] ON [ClockingsStaff] ([TenantId], [StafId], [ClockDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clockings_TenantId_VoluntId_ClockDate] ON [Clockings] ([TenantId], [VoluntId], [ClockDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE INDEX [IX_Clockings_VoluntId] ON [Clockings] ([VoluntId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929053552_MultiTenancy_Phase1b_TightenSchema', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ContactInfo] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN

                    UPDATE Tenants
                    SET Address = N'5757 Karl Road, Columbus, OH 43229',
                        ContactInfo = N'614-516-1812 | Inpremcommunitycenter@yahoo.com'
                    WHERE Name = N'Inprem Holistic Community Resource Center';
                
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE TABLE [Subscriptions] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [BillingCycle] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CurrentPeriodStart] datetime2 NOT NULL,
        [CurrentPeriodEnd] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [PaymentGateway] nvarchar(30) NULL,
        [ExternalSubscriptionId] nvarchar(100) NULL,
        CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Subscriptions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [SubscriptionId] int NOT NULL,
        [InvoiceNumber] nvarchar(30) NOT NULL,
        [PeriodStart] datetime2 NOT NULL,
        [PeriodEnd] datetime2 NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IssuedDate] datetime2 NOT NULL,
        [DueDate] datetime2 NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [PaidDate] datetime2 NULL,
        [PaymentMethod] nvarchar(100) NULL,
        [PaymentReference] nvarchar(100) NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invoices_Subscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [Subscriptions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Invoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE INDEX [IX_Invoices_SubscriptionId] ON [Invoices] ([SubscriptionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE INDEX [IX_Invoices_TenantId] ON [Invoices] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Subscriptions_TenantId] ON [Subscriptions] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001111045_MultiTenancy_Phase6a_AddBillingTables', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111135_MultiTenancy_Phase6b_SeedSubscriptions'
)
BEGIN

                    INSERT INTO [Subscriptions] ([TenantId], [Amount], [Currency], [BillingCycle], [Status], [CurrentPeriodStart], [CurrentPeriodEnd], [CreatedAt], [PaymentGateway], [ExternalSubscriptionId])
                    SELECT t.[Id], 0, N'USD', N'Monthly', N'Active', SYSUTCDATETIME(), DATEADD(YEAR, 1, SYSUTCDATETIME()), SYSUTCDATETIME(), NULL, NULL
                    FROM [Tenants] t
                    WHERE NOT EXISTS (SELECT 1 FROM [Subscriptions] s WHERE s.[TenantId] = t.[Id]);
                
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111135_MultiTenancy_Phase6b_SeedSubscriptions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001111135_MultiTenancy_Phase6b_SeedSubscriptions', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001133327_AddInvoicePeriodUniqueIndex'
)
BEGIN
    DROP INDEX [IX_Invoices_SubscriptionId] ON [Invoices];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001133327_AddInvoicePeriodUniqueIndex'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [ActiveInvoicePerPeriodIndex] ON [Invoices] ([SubscriptionId], [PeriodStart]) WHERE [Status] <> ''Void''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001133327_AddInvoicePeriodUniqueIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001133327_AddInvoicePeriodUniqueIndex', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    DROP INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    DROP INDEX [UserNameIndex] ON [AspNetUsers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [SuperAdminNormalizedUserNameIndex] ON [AspNetUsers] ([TenantId], [NormalizedUserName]) WHERE [TenantId] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [TenantNormalizedUserNameIndex] ON [AspNetUsers] ([TenantId], [NormalizedUserName]) WHERE [TenantId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    CREATE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001135332_TenantScopedAdminUsernames', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002182520_RemoveLogoutSettingsFeature'
)
BEGIN
    DROP TABLE [Setting];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002182520_RemoveLogoutSettingsFeature'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002182520_RemoveLogoutSettingsFeature', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [ContactPersonEmail] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [ContactPersonPhone] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [ContactPersonPosition] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261003091001_AddVolunteerContactPersonDetails', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail'
)
BEGIN

    DECLARE @shared int = (SELECT COUNT(*) FROM (
        SELECT NormalizedEmail FROM [AspNetUsers] WHERE NormalizedEmail IS NOT NULL
        GROUP BY NormalizedEmail HAVING COUNT(*) > 1) d);
    IF @shared > 0
        THROW 50003, N'M3_PlatformUniqueEmail stopped: some emails are used by more than one account. Resolve them first (see scripts/inspection/04-data-quality.sql, result 2).', 1;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail'
)
BEGIN
    DROP INDEX [EmailIndex] ON [AspNetUsers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013161410_M3_PlatformUniqueEmail', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN

    DECLARE @crossed int =
        (SELECT COUNT(*) FROM [Clockings] c JOIN [Volunteers] v ON v.VolunteerId = c.VoluntId WHERE c.TenantId <> v.TenantId)
      + (SELECT COUNT(*) FROM [ClockingsStaff] cs JOIN [Staffs] s ON s.StaffId = cs.StafId WHERE cs.TenantId <> s.TenantId);
    IF @crossed > 0
        THROW 50004, N'M4_CompositeTenantKeys stopped: some sessions belong to another organization than their person. Resolve them first (see scripts/inspection/04-data-quality.sql, result 4).', 1;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Clockings] DROP CONSTRAINT [FK_Clockings_Volunteers_VoluntId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [ClockingsStaff] DROP CONSTRAINT [FK_ClockingsStaff_Staffs_StafId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    DROP INDEX [IX_ClockingsStaff_StafId] ON [ClockingsStaff];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    DROP INDEX [IX_Clockings_VoluntId] ON [Clockings];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Volunteers] ADD CONSTRAINT [AK_Volunteers_TenantId_VolunteerId] UNIQUE ([TenantId], [VolunteerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Staffs] ADD CONSTRAINT [AK_Staffs_TenantId_StaffId] UNIQUE ([TenantId], [StaffId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Clockings] ADD CONSTRAINT [FK_Clockings_Volunteers_TenantId_VoluntId] FOREIGN KEY ([TenantId], [VoluntId]) REFERENCES [Volunteers] ([TenantId], [VolunteerId]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD CONSTRAINT [FK_ClockingsStaff_Staffs_TenantId_StafId] FOREIGN KEY ([TenantId], [StafId]) REFERENCES [Staffs] ([TenantId], [StaffId]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013175341_M4_CompositeTenantKeys', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029163952_AddProviderPasswordChange'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [RequiresPasswordChange] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029163952_AddProviderPasswordChange'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029163952_AddProviderPasswordChange', N'8.0.32');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104092629_SplitTenantContactFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Email] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104092629_SplitTenantContactFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Phone] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104092629_SplitTenantContactFields'
)
BEGIN

                    UPDATE Tenants
                    SET Phone = CASE WHEN CHARINDEX(N' | ', ContactInfo) > 0 THEN LEFT(ContactInfo, CHARINDEX(N' | ', ContactInfo) - 1) ELSE ContactInfo END,
                        Email = CASE WHEN CHARINDEX(N' | ', ContactInfo) > 0 THEN SUBSTRING(ContactInfo, CHARINDEX(N' | ', ContactInfo) + 3, 255) END;
                
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104092629_SplitTenantContactFields'
)
BEGIN
    DECLARE @var5 sysname;
    SELECT @var5 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'ContactInfo');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT [' + @var5 + '];');
    ALTER TABLE [Tenants] DROP COLUMN [ContactInfo];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104092629_SplitTenantContactFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104092629_SplitTenantContactFields', N'8.0.32');
END;
GO

COMMIT;
GO

