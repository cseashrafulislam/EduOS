using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912234500_NormalizeFeeReminderTemplateRelation")]
public partial class NormalizeFeeReminderTemplateRelation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string table = "FeeReminders";
        const string column = "TemplateId";
        const string shadowColumn = "TemplateId1";
        const string parentTable = "MessageTemplates";
        const string indexName = "IX_FeeReminders_TemplateId";
        const string fkName = "FK_FeeReminders_MessageTemplates_TemplateId";

        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL AND COL_LENGTH('dbo.{table}','{column}') IS NOT NULL
BEGIN
    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadowColumn}');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadowColumn}') AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.{table}','{shadowColumn}') IS NOT NULL
    BEGIN
        IF EXISTS(
            SELECT 1 FROM [dbo].[{table}]
            WHERE [{shadowColumn}] IS NOT NULL AND [{column}] IS NOT NULL AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadowColumn}])
        ) THROW 51000, 'Cannot normalize FeeReminders.TemplateId because canonical and shadow references conflict.', 1;

        ALTER TABLE [dbo].[{table}] ADD [{column}Normalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[{table}] SET [{column}Normalized]=COALESCE(CONVERT(bigint,[{shadowColumn}]),CONVERT(bigint,[{column}]));');
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{shadowColumn}];
        EXEC sp_rename N'dbo.{table}.{column}Normalized', N'{column}', 'COLUMN';
    END
    ELSE
        ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NULL;

    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize FeeReminders.TemplateId because MessageTemplates does not exist.', 1;

    IF EXISTS(
        SELECT 1 FROM [dbo].[{table}] r
        WHERE r.[{column}] IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}])
    ) THROW 51000, 'Cannot normalize FeeReminders.TemplateId because orphaned references exist.', 1;

    IF COL_LENGTH('dbo.{table}','TenantId') IS NOT NULL AND COL_LENGTH('dbo.{parentTable}','TenantId') IS NOT NULL AND EXISTS(
        SELECT 1 FROM [dbo].[{table}] r
        JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}]
        WHERE r.[TenantId]<>p.[TenantId]
    ) THROW 51000, 'Cannot normalize FeeReminders.TemplateId because cross-tenant references exist.', 1;

    ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[{table}]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
