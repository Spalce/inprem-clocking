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
    VALUES (N'20261013161410_M3_PlatformUniqueEmail', N'8.0.0');
END;
GO

COMMIT;
GO

