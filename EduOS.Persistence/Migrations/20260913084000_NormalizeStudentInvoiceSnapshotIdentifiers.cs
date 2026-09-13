using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913084000_NormalizeStudentInvoiceSnapshotIdentifiers")]
public partial class NormalizeStudentInvoiceSnapshotIdentifiers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[StudentInvoices]', N'U') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[StudentInvoices] ALTER COLUMN [AcademicYearId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[StudentInvoices] ALTER COLUMN [ClassId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[StudentInvoices] ALTER COLUMN [SectionId] BIGINT NOT NULL;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical billing identifiers may exceed INT after widening; narrowing is intentionally irreversible.
    }
}
