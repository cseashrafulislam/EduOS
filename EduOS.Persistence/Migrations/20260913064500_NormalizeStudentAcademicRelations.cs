using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913064500_NormalizeStudentAcademicRelations")]
public partial class NormalizeStudentAcademicRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeRelation(migrationBuilder, "ClassId", "Classes", false);
        NormalizeRelation(migrationBuilder, "SectionId", "Sections", false);
        NormalizeRelation(migrationBuilder, "GroupId", "Groups", true);
        NormalizeRelation(migrationBuilder, "AcademicYearId", "AcademicYears", false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void NormalizeRelation(MigrationBuilder migrationBuilder, string column, string parentTable, bool optional)
    {
        var shadow = column + "1";
        var normalized = column + "Normalized";
        var indexName = $"IX_Students_{column}";
        var fkName = $"FK_Students_{parentTable}_{column}";
        var nullability = optional ? "NULL" : "NOT NULL";
        var requiredGuard = optional ? string.Empty : $@"
        IF EXISTS(SELECT 1 FROM [dbo].[Students] WHERE [{normalized}] IS NULL)
            THROW 51000, 'Cannot normalize Students.{column} because required references are null.', 1;";
        var orphanPredicate = optional ? $"r.[{column}] IS NOT NULL AND " : string.Empty;

        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[Students]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize Students.{column} because {parentTable} does not exist.', 1;
    IF COL_LENGTH('dbo.Students','{column}') IS NULL
        THROW 51000, 'Cannot normalize Students.{column} because the canonical column does not exist.', 1;

    DECLARE @fk_Students_{column} nvarchar(max)=N'';
    SELECT @fk_Students_{column} += N'ALTER TABLE [dbo].[Students] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Students]') AND c.name IN (N'{column}',N'{shadow}');
    IF LEN(@fk_Students_{column})>0 EXEC sp_executesql @fk_Students_{column};

    DECLARE @ix_Students_{column} nvarchar(max)=N'';
    SELECT @ix_Students_{column} += N'DROP INDEX ['+i.name+N'] ON [dbo].[Students];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Students]') AND c.name IN (N'{column}',N'{shadow}')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix_Students_{column})>0 EXEC sp_executesql @ix_Students_{column};

    IF COL_LENGTH('dbo.Students','{shadow}') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Students] WHERE [{shadow}] IS NOT NULL AND [{column}] IS NOT NULL AND CONVERT(bigint,[{column}])<>0 AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadow}]))
            THROW 51000, 'Cannot normalize Students.{column} because canonical and shadow references conflict.', 1;
        IF COL_LENGTH('dbo.Students','{normalized}') IS NOT NULL
            ALTER TABLE [dbo].[Students] DROP COLUMN [{normalized}];
        ALTER TABLE [dbo].[Students] ADD [{normalized}] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Students] SET [{normalized}]=COALESCE(CONVERT(bigint,[{shadow}]),NULLIF(CONVERT(bigint,[{column}]),0));');{requiredGuard}
        ALTER TABLE [dbo].[Students] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[Students] DROP COLUMN [{shadow}];
        EXEC sp_rename N'dbo.Students.{normalized}', N'{column}', 'COLUMN';
        ALTER TABLE [dbo].[Students] ALTER COLUMN [{column}] BIGINT {nullability};
    END
    ELSE
        ALTER TABLE [dbo].[Students] ALTER COLUMN [{column}] BIGINT {nullability};

    IF EXISTS(SELECT 1 FROM [dbo].[Students] r WHERE {orphanPredicate}NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}]))
        THROW 51000, 'Cannot normalize Students.{column} because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Students] r JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Students.{column} because cross-tenant references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[Students]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[Students]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[Students]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[Students] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
