BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020001530_M1o_ProviderAlertPreferences'
)
BEGIN
    CREATE TABLE [platform].[ProviderAlertPreferences] (
        [UserId] nvarchar(450) NOT NULL,
        [NewSignups] bit NOT NULL,
        [BillingProblems] nvarchar(20) NOT NULL,
        [SystemProblems] bit NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProviderAlertPreferences] PRIMARY KEY ([UserId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020001530_M1o_ProviderAlertPreferences'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020001530_M1o_ProviderAlertPreferences', N'8.0.32');
END;
GO

COMMIT;
GO

