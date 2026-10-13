-- Verification for app migration M3_PlatformUniqueEmail. READ-ONLY.
-- After the up script: Applied = 1, EmailIndexUnique = 1, SharedEmails = 0.
SET NOCOUNT ON;
SELECT
    (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE MigrationId = N'20261013161410_M3_PlatformUniqueEmail') AS Applied,
    (SELECT CAST(is_unique AS int) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = N'EmailIndex') AS EmailIndexUnique,
    (SELECT COUNT(*) FROM (SELECT NormalizedEmail FROM [dbo].[AspNetUsers] WHERE NormalizedEmail IS NOT NULL
        GROUP BY NormalizedEmail HAVING COUNT(*) > 1) d) AS SharedEmails;
GO
