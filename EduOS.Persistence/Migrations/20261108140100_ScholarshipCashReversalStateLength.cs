using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Allow the full ReversalRequested workflow state without truncating a posted payment.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261108140100_ScholarshipCashReversalStateLength")]
public sealed class ScholarshipCashReversalStateLength : Migration
{
    private const string StateCheck =
        "[State] IN ('Submitted','Posted','Rejected','ReversalRequested','Reversed')";
    private const string ActiveIndex = "IX_ScholarshipDisbursements_TenantId_ScholarshipApplicationId";
    private const string StateCheckName = "CK_ScholarshipDisbursements_State";

    protected override void Up(MigrationBuilder m)
    {
        m.DropCheckConstraint(StateCheckName, "ScholarshipDisbursements");
        m.DropIndex(ActiveIndex, "ScholarshipDisbursements");
        m.AlterColumn<string>(name: "State", table: "ScholarshipDisbursements",
            type: "nvarchar(24)", maxLength: 24, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(16)", oldMaxLength: 16);
        m.CreateIndex(ActiveIndex, "ScholarshipDisbursements",
            new[] { "TenantId", "ScholarshipApplicationId" }, unique: true,
            filter: "[State] <> N'Rejected' AND [IsDeleted] = 0");
        m.AddCheckConstraint(StateCheckName, "ScholarshipDisbursements", StateCheck);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("IF EXISTS (SELECT 1 FROM dbo.ScholarshipDisbursements WHERE LEN([State]) > 16) " +
            "THROW 51032, 'Cannot shrink scholarship state while reversal requests exist.', 1;");
        m.DropCheckConstraint(StateCheckName, "ScholarshipDisbursements");
        m.DropIndex(ActiveIndex, "ScholarshipDisbursements");
        m.AlterColumn<string>(name: "State", table: "ScholarshipDisbursements",
            type: "nvarchar(16)", maxLength: 16, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(24)", oldMaxLength: 24);
        m.CreateIndex(ActiveIndex, "ScholarshipDisbursements",
            new[] { "TenantId", "ScholarshipApplicationId" }, unique: true,
            filter: "[State] <> N'Rejected' AND [IsDeleted] = 0");
        m.AddCheckConstraint(StateCheckName, "ScholarshipDisbursements", StateCheck);
    }
}
