BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    CREATE TABLE [platform].[Plans] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Features] nvarchar(2000) NULL,
        [MaxPeople] int NULL,
        [TrialDays] int NOT NULL,
        [IsPublic] bit NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Plans] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    CREATE TABLE [platform].[PlanPrices] (
        [Id] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Cycle] nvarchar(20) NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] char(3) NOT NULL,
        [PayPalPlanId] nvarchar(50) NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PlanPrices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlanPrices_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [platform].[Plans] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    CREATE INDEX [IX_PlanPrices_PlanId] ON [platform].[PlanPrices] ([PlanId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Plans_Code] ON [platform].[Plans] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014204227_M1c_Plans', N'8.0.32');
END;
GO

COMMIT;
GO

