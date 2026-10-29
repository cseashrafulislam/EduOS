using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive immutable amendment ledger. Rollback drops this table only, without
/// modifying previously published student-result versions or working marks.
/// Before rollback, export any real correction events to preserve their history.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029150000_ResultCorrectionEvents")]
public sealed class ResultCorrectionEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ResultCorrectionEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CaseReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                PreviousPublicationId = table.Column<long>(type: "bigint", nullable: false),
                NewPublicationId = table.Column<long>(type: "bigint", nullable: false),
                StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                PreviousVersionNo = table.Column<int>(type: "int", nullable: false),
                NewVersionNo = table.Column<int>(type: "int", nullable: false),
                CorrectedSubjectCount = table.Column<int>(type: "int", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                table.PrimaryKey("PK_ResultCorrectionEvents", x => x.Id);
                table.UniqueConstraint("AK_ResultCorrectionEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_ResultCorrectionEvents_Version", "[PreviousVersionNo] > 0 AND [NewVersionNo] = [PreviousVersionNo] + 1");
                table.ForeignKey("FK_ResultCorrectionEvents_ResultPublications_TenantId_PreviousPublicationId",
                    x => new { x.TenantId, x.PreviousPublicationId }, "ResultPublications",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ResultCorrectionEvents_ResultPublications_TenantId_NewPublicationId",
                    x => new { x.TenantId, x.NewPublicationId }, "ResultPublications",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ResultCorrectionEvents_StudentEnrollments_TenantId_StudentEnrollmentId",
                    x => new { x.TenantId, x.StudentEnrollmentId }, "StudentEnrollments",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ResultCorrectionEvents_AspNetUsers_ActorUserId",
                    x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ResultCorrectionEvents_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ResultCorrectionEvents_ActorUserId", "ResultCorrectionEvents", "ActorUserId");
        migrationBuilder.CreateIndex("IX_ResultCorrectionEvents_TenantId_CaseReference", "ResultCorrectionEvents",
            new[] { "TenantId", "CaseReference" }, unique: true);
        migrationBuilder.CreateIndex("IX_ResultCorrectionEvents_TenantId_ClientRequestId", "ResultCorrectionEvents",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ResultCorrectionEvents_TenantId_PreviousPublicationId", "ResultCorrectionEvents",
            new[] { "TenantId", "PreviousPublicationId" });
        migrationBuilder.CreateIndex("IX_ResultCorrectionEvents_TenantId_NewPublicationId", "ResultCorrectionEvents",
            new[] { "TenantId", "NewPublicationId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ResultCorrectionEvents_TenantId_StudentEnrollmentId", "ResultCorrectionEvents",
            new[] { "TenantId", "StudentEnrollmentId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("ResultCorrectionEvents");
}
