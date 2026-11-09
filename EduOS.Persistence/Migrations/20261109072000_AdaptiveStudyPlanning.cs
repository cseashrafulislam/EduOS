using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Canonical LMS adaptive metadata, teacher targets and immutable plan snapshots.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109072000_AdaptiveStudyPlanning")]
public sealed class AdaptiveStudyPlanning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Explanation", "QuizQuestions", type: "nvarchar(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<long>("CourseSectionId", "Quizzes", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("SkillCode", "Quizzes", type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<int>("PracticeDifficulty", "Quizzes", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.CreateIndex("IX_Quizzes_TenantId_CourseSectionId", "Quizzes", new[] { "TenantId", "CourseSectionId" });
        migrationBuilder.CreateIndex("IX_Quizzes_TenantId_CourseId_CourseSectionId_SkillCode", "Quizzes",
            new[] { "TenantId", "CourseId", "CourseSectionId", "SkillCode" });
        migrationBuilder.AddForeignKey(name: "FK_Quizzes_CourseSections_TenantId_CourseSectionId", table: "Quizzes",
            columns: new[] { "TenantId", "CourseSectionId" }, principalTable: "CourseSections",
            principalColumns: new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddCheckConstraint("CK_Quizzes_PracticeDifficulty", "Quizzes", "[PracticeDifficulty] BETWEEN 1 AND 3");

        migrationBuilder.CreateTable(name: "AdaptiveStudyGoals", columns: table => new
        {
            Id = table.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>("bigint", nullable: false),
            CourseId = table.Column<long>("bigint", nullable: false),
            StudentId = table.Column<long>("bigint", nullable: false),
            CourseSectionId = table.Column<long>("bigint", nullable: false),
            SkillCode = table.Column<string>("nvarchar(64)", maxLength: 64, nullable: false),
            TargetPercent = table.Column<decimal>("decimal(5,2)", nullable: false),
            TeacherNote = table.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            SetByUserId = table.Column<long>("bigint", nullable: false),
            CreatedAt = table.Column<DateTime>("datetime2", nullable: false),
            UpdatedAt = table.Column<DateTime>("datetime2", nullable: true),
            CreatedBy = table.Column<long>("bigint", nullable: true),
            UpdatedBy = table.Column<long>("bigint", nullable: true),
            IsDeleted = table.Column<bool>("bit", nullable: false),
            DeletedAt = table.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = table.Column<long>("bigint", nullable: true),
            RowVersion = table.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AdaptiveStudyGoals", x => x.Id);
            table.UniqueConstraint("AK_AdaptiveStudyGoals_TenantId_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_AdaptiveStudyGoals_Target", "[TargetPercent] >= 0 AND [TargetPercent] <= 100");
            table.ForeignKey("FK_AdaptiveStudyGoals_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AdaptiveStudyGoals_Courses_TenantId_CourseId", x => new { x.TenantId, x.CourseId },
                "Courses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AdaptiveStudyGoals_Students_TenantId_StudentId", x => new { x.TenantId, x.StudentId },
                "Students", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AdaptiveStudyGoals_CourseSections_TenantId_CourseSectionId", x => new { x.TenantId, x.CourseSectionId },
                "CourseSections", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_AdaptiveStudyGoals_TenantId_StudentId", "AdaptiveStudyGoals", new[] { "TenantId", "StudentId" });
        migrationBuilder.CreateIndex("IX_AdaptiveStudyGoals_TenantId_CourseSectionId", "AdaptiveStudyGoals", new[] { "TenantId", "CourseSectionId" });
        migrationBuilder.CreateIndex("IX_AdaptiveStudyGoals_TenantId_CourseId_StudentId_CourseSectionId_SkillCode", "AdaptiveStudyGoals",
            new[] { "TenantId", "CourseId", "StudentId", "CourseSectionId", "SkillCode" }, unique: true, filter: "[IsDeleted] = 0");

        migrationBuilder.CreateTable(name: "AdaptiveStudyPlanHistories", columns: table => new
        {
            Id = table.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>("bigint", nullable: false),
            ClientRequestId = table.Column<Guid>("uniqueidentifier", nullable: false),
            CourseEnrollmentId = table.Column<long>("bigint", nullable: false),
            StudentId = table.Column<long>("bigint", nullable: false),
            AlgorithmVersion = table.Column<int>("int", nullable: false),
            GeneratedAt = table.Column<DateTime>("datetime2", nullable: false),
            PlanJson = table.Column<string>("nvarchar(max)", nullable: false),
            CreatedAt = table.Column<DateTime>("datetime2", nullable: false),
            UpdatedAt = table.Column<DateTime>("datetime2", nullable: true),
            CreatedBy = table.Column<long>("bigint", nullable: true),
            UpdatedBy = table.Column<long>("bigint", nullable: true),
            IsDeleted = table.Column<bool>("bit", nullable: false),
            DeletedAt = table.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = table.Column<long>("bigint", nullable: true),
            RowVersion = table.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AdaptiveStudyPlanHistories", x => x.Id);
            table.UniqueConstraint("AK_AdaptiveStudyPlanHistories_TenantId_Id", x => new { x.TenantId, x.Id });
            table.ForeignKey("FK_AdaptiveStudyPlanHistories_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AdaptiveStudyPlanHistories_CourseEnrollments_TenantId_CourseEnrollmentId", x => new { x.TenantId, x.CourseEnrollmentId },
                "CourseEnrollments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AdaptiveStudyPlanHistories_Students_TenantId_StudentId", x => new { x.TenantId, x.StudentId },
                "Students", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_AdaptiveStudyPlanHistories_TenantId_ClientRequestId", "AdaptiveStudyPlanHistories",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AdaptiveStudyPlanHistories_TenantId_CourseEnrollmentId_GeneratedAt", "AdaptiveStudyPlanHistories",
            new[] { "TenantId", "CourseEnrollmentId", "GeneratedAt" });
        migrationBuilder.CreateIndex("IX_AdaptiveStudyPlanHistories_TenantId_StudentId", "AdaptiveStudyPlanHistories",
            new[] { "TenantId", "StudentId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.AdaptiveStudyGoals) OR EXISTS (SELECT 1 FROM dbo.AdaptiveStudyPlanHistories) OR EXISTS (SELECT 1 FROM dbo.Quizzes WHERE CourseSectionId IS NOT NULL) OR EXISTS (SELECT 1 FROM dbo.QuizQuestions WHERE Explanation IS NOT NULL) THROW 51062, 'Approved retention migration required before rolling back adaptive study evidence.', 1;");
        migrationBuilder.DropTable("AdaptiveStudyPlanHistories");
        migrationBuilder.DropTable("AdaptiveStudyGoals");
        migrationBuilder.DropCheckConstraint("CK_Quizzes_PracticeDifficulty", "Quizzes");
        migrationBuilder.DropForeignKey("FK_Quizzes_CourseSections_TenantId_CourseSectionId", "Quizzes");
        migrationBuilder.DropIndex("IX_Quizzes_TenantId_CourseSectionId", "Quizzes");
        migrationBuilder.DropIndex("IX_Quizzes_TenantId_CourseId_CourseSectionId_SkillCode", "Quizzes");
        migrationBuilder.DropColumn("CourseSectionId", "Quizzes");
        migrationBuilder.DropColumn("SkillCode", "Quizzes");
        migrationBuilder.DropColumn("PracticeDifficulty", "Quizzes");
        migrationBuilder.DropColumn("Explanation", "QuizQuestions");
    }
}
