using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive immutable rescrutiny application/decision ledger. Existing publications,
/// marks and historical result snapshots are untouched. Rollback requires archiving
/// newly entered synthetic/pilot cases before dropping this independent table.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028180000_ResultRescrutinyEvents")]
public sealed class ResultRescrutinyEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ResultRescrutinyEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CaseReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ResultPublicationId = table.Column<long>(type: "bigint", nullable: false),
                StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                PublicationVersionNo = table.Column<int>(type: "int", nullable: false),
                Action = table.Column<int>(type: "int", nullable: false),
                IsTerminal = table.Column<bool>(type: "bit", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ResultRescrutinyEvents", x => x.Id);
                table.UniqueConstraint("AK_ResultRescrutinyEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_ResultRescrutinyEvents_Action",
                    "([IsTerminal] = 0 AND [Action] = 1) OR ([IsTerminal] = 1 AND [Action] IN (2,3))");
                table.ForeignKey("FK_ResultRescrutinyEvents_ResultPublications_TenantId_ResultPublicationId",
                    x => new { x.TenantId, x.ResultPublicationId }, "ResultPublications",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ResultRescrutinyEvents_StudentEnrollments_TenantId_StudentEnrollmentId",
                    x => new { x.TenantId, x.StudentEnrollmentId }, "StudentEnrollments",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ResultRescrutinyEvents_AspNetUsers_ActorUserId",
                    x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ResultRescrutinyEvents_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ResultRescrutinyEvents_ActorUserId",
            "ResultRescrutinyEvents", "ActorUserId");
        migrationBuilder.CreateIndex("IX_ResultRescrutinyEvents_TenantId_ResultPublicationId",
            "ResultRescrutinyEvents", new[] { "TenantId", "ResultPublicationId" });
        migrationBuilder.CreateIndex("IX_ResultRescrutinyEvents_TenantId_StudentEnrollmentId",
            "ResultRescrutinyEvents", new[] { "TenantId", "StudentEnrollmentId" });
        migrationBuilder.CreateIndex("IX_ResultRescrutinyEvents_TenantId_CaseReference_IsTerminal",
            "ResultRescrutinyEvents", new[] { "TenantId", "CaseReference", "IsTerminal" }, unique: true);
        migrationBuilder.CreateIndex("IX_ResultRescrutinyEvents_TenantId_ClientRequestId",
            "ResultRescrutinyEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ResultRescrutinyEvents_TenantId_ResultPublicationId_StudentEnrollmentId",
            "ResultRescrutinyEvents",
            new[] { "TenantId", "ResultPublicationId", "StudentEnrollmentId" },
            unique: true, filter: "[IsTerminal] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("ResultRescrutinyEvents");
}
