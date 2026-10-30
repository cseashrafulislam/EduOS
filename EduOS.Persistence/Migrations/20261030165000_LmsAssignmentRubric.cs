using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive per-assignment rubric criteria and immutable awarded-score evidence.
/// Existing assignments and scalar grades remain valid without a rubric.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030165000_LmsAssignmentRubric")]
public partial class LmsAssignmentRubric : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssignmentRubricCriteria",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                MaxMarks = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignmentRubricCriteria", x => x.Id);
                table.UniqueConstraint("AK_AssignmentRubricCriteria_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_AssignmentRubricCriteria_Assignments_TenantId_AssignmentId",
                    x => new { x.TenantId, x.AssignmentId }, "Assignments", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssignmentRubricCriteria_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_AssignmentRubricCriteria_MaxMarks", "[MaxMarks] > 0 AND [MaxMarks] <= 100000");
                table.CheckConstraint("CK_AssignmentRubricCriteria_Order", "[DisplayOrder] >= 1 AND [DisplayOrder] <= 20");
            });

        migrationBuilder.CreateTable(
            name: "AssignmentRubricScores",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AssignmentSubmissionId = table.Column<long>(type: "bigint", nullable: false),
                AssignmentRubricCriterionId = table.Column<long>(type: "bigint", nullable: false),
                AwardedMarks = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignmentRubricScores", x => x.Id);
                table.UniqueConstraint("AK_AssignmentRubricScores_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_AssignmentRubricScores_AssignmentSubmissions_TenantId_AssignmentSubmissionId",
                    x => new { x.TenantId, x.AssignmentSubmissionId }, "AssignmentSubmissions", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssignmentRubricScores_AssignmentRubricCriteria_TenantId_AssignmentRubricCriterionId",
                    x => new { x.TenantId, x.AssignmentRubricCriterionId }, "AssignmentRubricCriteria", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AssignmentRubricScores_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_AssignmentRubricScores_AwardedMarks", "[AwardedMarks] >= 0");
            });

        migrationBuilder.CreateIndex("IX_AssignmentRubricCriteria_TenantId_AssignmentId",
            "AssignmentRubricCriteria", new[] { "TenantId", "AssignmentId" });
        migrationBuilder.CreateIndex("IX_AssignmentRubricCriteria_TenantId_AssignmentId_DisplayOrder",
            "AssignmentRubricCriteria", new[] { "TenantId", "AssignmentId", "DisplayOrder" },
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AssignmentRubricCriteria_TenantId_PublicId",
            "AssignmentRubricCriteria", new[] { "TenantId", "PublicId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AssignmentRubricScores_TenantId_AssignmentSubmissionId",
            "AssignmentRubricScores", new[] { "TenantId", "AssignmentSubmissionId" });
        migrationBuilder.CreateIndex("IX_AssignmentRubricScores_TenantId_AssignmentRubricCriterionId",
            "AssignmentRubricScores", new[] { "TenantId", "AssignmentRubricCriterionId" });
        migrationBuilder.CreateIndex("IX_AssignmentRubricScores_TenantId_AssignmentSubmissionId_AssignmentRubricCriterionId",
            "AssignmentRubricScores", new[] { "TenantId", "AssignmentSubmissionId", "AssignmentRubricCriterionId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AssignmentRubricScores");
        migrationBuilder.DropTable("AssignmentRubricCriteria");
    }
}
