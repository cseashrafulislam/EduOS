using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913194500_NormalizeTrialAccountRelations")]
public partial class NormalizeTrialAccountRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[TrialAccounts]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[Tenants]', N'U') IS NULL
        THROW 51000, 'Cannot normalize TrialAccounts.TenantId because Tenants does not exist.', 1;
    IF COL_LENGTH('dbo.TrialAccounts','TenantId') IS NULL
        THROW 51000, 'Cannot normalize TrialAccounts.TenantId because the canonical column does not exist.', 1;

    DECLARE @tenantFk nvarchar(max)=N'';
    SELECT @tenantFk += N'ALTER TABLE [dbo].[TrialAccounts] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[TrialAccounts]') AND c.name IN (N'TenantId',N'TenantId1');
    IF LEN(@tenantFk)>0 EXEC sp_executesql @tenantFk;

    DECLARE @tenantIx nvarchar(max)=N'';
    SELECT @tenantIx += N'DROP INDEX ['+i.name+N'] ON [dbo].[TrialAccounts];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[TrialAccounts]') AND c.name IN (N'TenantId',N'TenantId1')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@tenantIx)>0 EXEC sp_executesql @tenantIx;

    IF COL_LENGTH('dbo.TrialAccounts','TenantId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[TrialAccounts] WHERE [TenantId1] IS NOT NULL AND [TenantId] IS NOT NULL AND CONVERT(bigint,[TenantId])<>0 AND CONVERT(bigint,[TenantId])<>CONVERT(bigint,[TenantId1]))
            THROW 51000, 'Cannot normalize TrialAccounts.TenantId because canonical and shadow references conflict.', 1;
        IF COL_LENGTH('dbo.TrialAccounts','TenantIdNormalized') IS NOT NULL
            ALTER TABLE [dbo].[TrialAccounts] DROP COLUMN [TenantIdNormalized];
        ALTER TABLE [dbo].[TrialAccounts] ADD [TenantIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[TrialAccounts] SET [TenantIdNormalized]=COALESCE(CONVERT(bigint,[TenantId1]),NULLIF(CONVERT(bigint,[TenantId]),0));');
        IF EXISTS(SELECT 1 FROM [dbo].[TrialAccounts] WHERE [TenantIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize TrialAccounts.TenantId because required references are null.', 1;
        ALTER TABLE [dbo].[TrialAccounts] DROP COLUMN [TenantId];
        ALTER TABLE [dbo].[TrialAccounts] DROP COLUMN [TenantId1];
        EXEC sp_rename N'dbo.TrialAccounts.TenantIdNormalized', N'TenantId', 'COLUMN';
        ALTER TABLE [dbo].[TrialAccounts] ALTER COLUMN [TenantId] BIGINT NOT NULL;
    END
    ELSE
        ALTER TABLE [dbo].[TrialAccounts] ALTER COLUMN [TenantId] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[TrialAccounts] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Tenants] t WHERE t.[Id]=r.[TenantId]))
        THROW 51000, 'Cannot normalize TrialAccounts.TenantId because orphaned tenant references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[TrialAccounts]') AND name=N'IX_TrialAccounts_TenantId')
        CREATE INDEX [IX_TrialAccounts_TenantId] ON [dbo].[TrialAccounts]([TenantId]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[TrialAccounts]') AND name=N'FK_TrialAccounts_Tenants_TenantId')
        ALTER TABLE [dbo].[TrialAccounts] WITH CHECK ADD CONSTRAINT [FK_TrialAccounts_Tenants_TenantId] FOREIGN KEY([TenantId]) REFERENCES [dbo].[Tenants]([Id]) ON DELETE NO ACTION;

    IF COL_LENGTH('dbo.TrialAccounts','ConvertedToPlanId') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[TrialAccounts] ALTER COLUMN [ConvertedToPlanId] BIGINT NULL;
        IF OBJECT_ID(N'[dbo].[SubscriptionPlans]', N'U') IS NULL
            THROW 51000, 'Cannot normalize TrialAccounts.ConvertedToPlanId because SubscriptionPlans does not exist.', 1;
        IF EXISTS(SELECT 1 FROM [dbo].[TrialAccounts] r WHERE r.[ConvertedToPlanId] IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [dbo].[SubscriptionPlans] p WHERE p.[Id]=r.[ConvertedToPlanId]))
            THROW 51000, 'Cannot normalize TrialAccounts.ConvertedToPlanId because orphaned plan references exist.', 1;
        IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[TrialAccounts]') AND name=N'IX_TrialAccounts_ConvertedToPlanId')
            CREATE INDEX [IX_TrialAccounts_ConvertedToPlanId] ON [dbo].[TrialAccounts]([ConvertedToPlanId]);
        IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[TrialAccounts]') AND name=N'FK_TrialAccounts_SubscriptionPlans_ConvertedToPlanId')
            ALTER TABLE [dbo].[TrialAccounts] WITH CHECK ADD CONSTRAINT [FK_TrialAccounts_SubscriptionPlans_ConvertedToPlanId] FOREIGN KEY([ConvertedToPlanId]) REFERENCES [dbo].[SubscriptionPlans]([Id]) ON DELETE NO ACTION;
    END
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
