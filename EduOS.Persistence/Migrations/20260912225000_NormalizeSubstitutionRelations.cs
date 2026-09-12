using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912225000_NormalizeSubstitutionRelations")]
public partial class NormalizeSubstitutionRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeRequiredRelation(migrationBuilder, "OriginalTeacherId", "OriginalTeacherId1", "Employee");
        NormalizeRequiredRelation(migrationBuilder, "SubstituteTeacherId", "SubstituteTeacherId1", "Employee");
        NormalizeRequiredRelation(migrationBuilder, "ClassId", "ClassId1", "Classes");
        NormalizeRequiredRelation(migrationBuilder, "SubjectId", "SubjectId1", "Subjects");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void NormalizeRequiredRelation(MigrationBuilder migrationBuilder, string column, string shadowColumn, string parentTable)
    {
        var indexName = $"IX_Substitutions_{column}";
        var fkName = $"FK_Substitutions_{parentTable}_{column}";
        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[Substitutions]', N'U') IS NOT NULL AND COL_LENGTH('dbo.Substitutions','{column}') IS NOT NULL
BEGIN
    DECLARE @fk_{column} nvarchar(max)=N'';
    SELECT @fk_{column} += N'ALTER TABLE [dbo].[Substitutions] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Substitutions]') AND c.name IN (N'{column}',N'{shadowColumn}');
    IF LEN(@fk_{column})>0 EXEC sp_executesql @fk_{column};

    DECLARE @ix_{column} nvarchar(max)=N'';
    SELECT @ix_{column} += N'DROP INDEX ['+i.name+N'] ON [dbo].[Substitutions];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Substitutions]') AND c.name IN (N'{column}',N'{shadowColumn}') AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix_{column})>0 EXEC sp_executesql @ix_{column};

    IF COL_LENGTH('dbo.Substitutions','{shadowColumn}') IS NOT NULL
    BEGIN
        IF EXISTS(
            SELECT 1 FROM [dbo].[Substitutions]
            WHERE [{shadowColumn}] IS NOT NULL AND [{column}]<>0 AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadowColumn}])
        ) THROW 51000, 'Cannot normalize Substitutions.{column} because canonical and shadow references conflict.', 1;

        ALTER TABLE [dbo].[Substitutions] ADD [{column}Normalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Substitutions] SET [{column}Normalized]=CASE WHEN [{shadowColumn}] IS NOT NULL AND [{column}]=0 THEN CONVERT(bigint,[{shadowColumn}]) ELSE CONVERT(bigint,[{column}]) END;');
        ALTER TABLE [dbo].[Substitutions] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[Substitutions] DROP COLUMN [{shadowColumn}];
        EXEC sp_rename N'dbo.Substitutions.{column}Normalized', N'{column}', 'COLUMN';
    END
    ELSE
        ALTER TABLE [dbo].[Substitutions] ALTER COLUMN [{column}] BIGINT NOT NULL;

    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize Substitutions.{column} because parent table {parentTable} does not exist.', 1;

    IF EXISTS(
        SELECT 1 FROM [dbo].[Substitutions] r
        WHERE r.[{column}] IS NULL OR r.[{column}]=0 OR NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}])
    ) THROW 51000, 'Cannot normalize Substitutions.{column} because orphaned references exist.', 1;

    IF COL_LENGTH('dbo.Substitutions','TenantId') IS NOT NULL AND COL_LENGTH('dbo.{parentTable}','TenantId') IS NOT NULL AND EXISTS(
        SELECT 1 FROM [dbo].[Substitutions] r
        JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}]
        WHERE r.[TenantId]<>p.[TenantId]
    ) THROW 51000, 'Cannot normalize Substitutions.{column} because cross-tenant references exist.', 1;

    ALTER TABLE [dbo].[Substitutions] ALTER COLUMN [{column}] BIGINT NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[Substitutions]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[Substitutions]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[Substitutions]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[Substitutions] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
