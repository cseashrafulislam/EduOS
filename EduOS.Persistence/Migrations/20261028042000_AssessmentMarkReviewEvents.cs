using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Append-only review decisions; no existing marks or published snapshots are changed.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028042000_AssessmentMarkReviewEvents")]
public sealed class AssessmentMarkReviewEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssessmentMarkReviewEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                AssessmentSubjectId = table.Column<long>(type: "bigint", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Decision = table.Column<int>(type: "int", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                MarkRowVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                table.PrimaryKey("PK_AssessmentMarkReviewEvents", x => x.Id);
                table.UniqueConstraint("AK_AssessmentMarkReviewEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_AssessmentMarkReviewEvents_AssessmentSubjects_TenantId_AssessmentSubjectId",
                    x => new { x.TenantId, x.AssessmentSubjectId }, "AssessmentSubjects",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssessmentMarkReviewEvents_AspNetUsers_ActorUserId",
                    x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssessmentMarkReviewEvents_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_AssessmentMarkReviewEvents_ActorUserId",
            "AssessmentMarkReviewEvents", "ActorUserId");
        migrationBuilder.CreateIndex("IX_AssessmentMarkReviewEvents_TenantId_AssessmentSubjectId_Id",
            "AssessmentMarkReviewEvents", new[] { "TenantId", "AssessmentSubjectId", "Id" });
        migrationBuilder.CreateIndex("IX_AssessmentMarkReviewEvents_TenantId_ClientRequestId",
            "AssessmentMarkReviewEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AssessmentMarkReviewEvents");
}
