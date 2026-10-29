using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive student/subject-specific retake and improvement approval. Historical
/// assessments default to Regular; no existing mark or published result is rewritten.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029220000_AssessmentAttemptAuthorizations")]
public sealed class AssessmentAttemptAuthorizations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("AttemptMode", "Assessments", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddCheckConstraint("CK_Assessment_AttemptMode", "Assessments", "[AttemptMode] IN (0,1,2)");
        migrationBuilder.CreateTable("AssessmentAttemptRegistrations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                AssessmentSubjectId = table.Column<long>(type: "bigint", nullable: false),
                StudentSubjectRegistrationId = table.Column<long>(type: "bigint", nullable: false),
                PriorResultPublicationId = table.Column<long>(type: "bigint", nullable: false),
                Decision = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                DecidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                table.PrimaryKey("PK_AssessmentAttemptRegistrations", x => x.Id);
                table.CheckConstraint("CK_AssessmentAttemptRegistration_Decision", "[Decision] BETWEEN 1 AND 3");
                table.ForeignKey("FK_AssessmentAttemptRegistrations_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssessmentAttemptRegistrations_AssessmentSubjects_TenantId_AssessmentSubjectId",
                    x => new { x.TenantId, x.AssessmentSubjectId }, "AssessmentSubjects", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssessmentAttemptRegistrations_StudentSubjectRegistrations_TenantId_StudentSubjectRegistrationId",
                    x => new { x.TenantId, x.StudentSubjectRegistrationId }, "StudentSubjectRegistrations",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssessmentAttemptRegistrations_ResultPublications_TenantId_PriorResultPublicationId",
                    x => new { x.TenantId, x.PriorResultPublicationId }, "ResultPublications",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssessmentAttemptRegistrations_AspNetUsers_DecidedByUserId",
                    x => x.DecidedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_AssessmentAttemptRegistrations_DecidedByUserId",
            "AssessmentAttemptRegistrations", "DecidedByUserId");
        migrationBuilder.CreateIndex("IX_AssessmentAttemptRegistrations_TenantId_AssessmentSubjectId_StudentSubjectRegistrationId",
            "AssessmentAttemptRegistrations", new[] { "TenantId", "AssessmentSubjectId", "StudentSubjectRegistrationId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AssessmentAttemptRegistrations_TenantId_StudentSubjectRegistrationId",
            "AssessmentAttemptRegistrations", new[] { "TenantId", "StudentSubjectRegistrationId" });
        migrationBuilder.CreateIndex("IX_AssessmentAttemptRegistrations_TenantId_PriorResultPublicationId",
            "AssessmentAttemptRegistrations", new[] { "TenantId", "PriorResultPublicationId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AssessmentAttemptRegistrations");
        migrationBuilder.DropCheckConstraint("CK_Assessment_AttemptMode", "Assessments");
        migrationBuilder.DropColumn("AttemptMode", "Assessments");
    }
}
