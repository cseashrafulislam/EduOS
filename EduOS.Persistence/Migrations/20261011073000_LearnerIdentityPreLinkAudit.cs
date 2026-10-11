using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

public partial class LearnerIdentityPreLinkAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Preserve every historical audit row. A Person reference does not yet exist
        // when identifier protection fails during initial learner registration.
        migrationBuilder.DropForeignKey(
            name: "FK_LearnerIdentityAccessLogs_Persons_PersonId",
            table: "LearnerIdentityAccessLogs");
        migrationBuilder.DropIndex(
            name: "IX_LearnerIdentityAccessLogs_PersonId",
            table: "LearnerIdentityAccessLogs");
        migrationBuilder.DropIndex(
            name: "IX_LearnerIdentityAccessLogs_TenantId_PersonId_AccessedAt",
            table: "LearnerIdentityAccessLogs");
        migrationBuilder.AlterColumn<long>(
            name: "PersonId", table: "LearnerIdentityAccessLogs", type: "bigint",
            nullable: true, oldClrType: typeof(long), oldType: "bigint");
        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_PersonId",
            table: "LearnerIdentityAccessLogs",
            column: "PersonId");
        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_TenantId_PersonId_AccessedAt",
            table: "LearnerIdentityAccessLogs",
            columns: new[] { "TenantId", "PersonId", "AccessedAt" });
        migrationBuilder.AddForeignKey(
            name: "FK_LearnerIdentityAccessLogs_Persons_PersonId",
            table: "LearnerIdentityAccessLogs",
            column: "PersonId", principalTable: "Persons",
            principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM dbo.LearnerIdentityAccessLogs WHERE PersonId IS NULL)
                THROW 51061, 'Pre-link learner audit rows exist; rolling back the nullable PersonId column would lose audit data.', 1;
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_LearnerIdentityAccessLogs_Persons_PersonId",
            table: "LearnerIdentityAccessLogs");
        migrationBuilder.DropIndex(
            name: "IX_LearnerIdentityAccessLogs_PersonId",
            table: "LearnerIdentityAccessLogs");
        migrationBuilder.DropIndex(
            name: "IX_LearnerIdentityAccessLogs_TenantId_PersonId_AccessedAt",
            table: "LearnerIdentityAccessLogs");
        migrationBuilder.AlterColumn<long>(
            name: "PersonId", table: "LearnerIdentityAccessLogs", type: "bigint",
            nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_PersonId",
            table: "LearnerIdentityAccessLogs",
            column: "PersonId");
        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_TenantId_PersonId_AccessedAt",
            table: "LearnerIdentityAccessLogs",
            columns: new[] { "TenantId", "PersonId", "AccessedAt" });
        migrationBuilder.AddForeignKey(
            name: "FK_LearnerIdentityAccessLogs_Persons_PersonId",
            table: "LearnerIdentityAccessLogs",
            column: "PersonId", principalTable: "Persons",
            principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }
}
