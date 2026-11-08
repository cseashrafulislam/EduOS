using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Additive: freeze assessment-component evidence with its canonical published result version.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261108093000_ObePublishedComponentEvidence")]
public sealed class ObePublishedComponentEvidence : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("ComponentCode", "ObeCourseOutcomes", type: "nvarchar(50)",
            maxLength: 50, nullable: true);
        m.DropIndex("UX_ObeCourseOutcomes_Course", "ObeCourseOutcomes");
        m.CreateIndex("UX_ObeCourseOutcomes_CourseComponent", "ObeCourseOutcomes",
            new[] { "TenantId", "PolicyId", "CurriculumSubjectId", "ComponentCode" }, unique: true);

        m.CreateTable("ObePublishedComponentRegisters", columns: t => new
        {
            TenantId = t.Column<long>("bigint", nullable: false),
            ResultPublicationId = t.Column<long>("bigint", nullable: false),
            CourseRowCount = t.Column<int>("int", nullable: false),
            ComponentRowCount = t.Column<int>("int", nullable: false),
            CapturedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ObePublishedComponentRegisters", x => new { x.TenantId, x.ResultPublicationId });
            t.ForeignKey("FK_ObePublishedComponentRegisters_Publication",
                x => new { x.TenantId, x.ResultPublicationId }, "ResultPublications",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ObePublishedComponentRegisters_Counts",
                "[CourseRowCount] > 0 AND [ComponentRowCount] >= 0");
        });

        m.CreateTable("ObePublishedComponentEvidence", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            ResultPublicationId = t.Column<long>("bigint", nullable: false),
            StudentEnrollmentId = t.Column<long>("bigint", nullable: false),
            CurriculumSubjectId = t.Column<long>("bigint", nullable: false),
            AssessmentComponentId = t.Column<long>("bigint", nullable: false),
            ComponentCode = t.Column<string>("nvarchar(50)", maxLength: 50, nullable: false),
            FullMarks = t.Column<decimal>("decimal(18,2)", nullable: false),
            ObtainedMarks = t.Column<decimal>("decimal(18,2)", nullable: false),
            IsAbsent = t.Column<bool>("bit", nullable: false),
            IsWithheld = t.Column<bool>("bit", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ObePublishedComponentEvidence", x => x.Id);
            t.ForeignKey("FK_ObePublishedComponentEvidence_Publication",
                x => new { x.TenantId, x.ResultPublicationId }, "ResultPublications",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObePublishedComponentEvidence_Enrollment",
                x => new { x.TenantId, x.StudentEnrollmentId }, "StudentEnrollments",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObePublishedComponentEvidence_Curriculum",
                x => new { x.TenantId, x.CurriculumSubjectId }, "CurriculumSubjects",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObePublishedComponentEvidence_Component",
                x => new { x.TenantId, x.AssessmentComponentId }, "AssessmentComponents",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ObePublishedComponentEvidence_Marks",
                "[FullMarks] > 0 AND [ObtainedMarks] >= 0 AND [ObtainedMarks] <= [FullMarks]");
        });
        m.CreateIndex("UX_ObePublishedComponentEvidence_Source", "ObePublishedComponentEvidence",
            new[] { "TenantId", "ResultPublicationId", "StudentEnrollmentId", "AssessmentComponentId" },
            unique: true);
        m.CreateIndex("IX_ObePublishedComponentEvidence_CLO", "ObePublishedComponentEvidence",
            new[] { "TenantId", "ResultPublicationId", "CurriculumSubjectId", "ComponentCode" });
        m.Sql("""
            CREATE TRIGGER dbo.TR_ObePublishedComponentEvidence_Immutable
            ON dbo.ObePublishedComponentEvidence AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51008, 'Published OBE component evidence cannot be changed.', 1;
            END
            """);
        m.Sql("""
            CREATE TRIGGER dbo.TR_ObePublishedComponentRegisters_Immutable
            ON dbo.ObePublishedComponentRegisters AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51009, 'Published OBE component register cannot be changed.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("DROP TRIGGER IF EXISTS dbo.TR_ObePublishedComponentRegisters_Immutable;");
        m.Sql("DROP TRIGGER IF EXISTS dbo.TR_ObePublishedComponentEvidence_Immutable;");
        m.DropTable("ObePublishedComponentEvidence");
        m.DropTable("ObePublishedComponentRegisters");
        m.DropIndex("UX_ObeCourseOutcomes_CourseComponent", "ObeCourseOutcomes");
        m.CreateIndex("UX_ObeCourseOutcomes_Course", "ObeCourseOutcomes",
            new[] { "TenantId", "PolicyId", "CurriculumSubjectId" }, unique: true);
        m.DropColumn("ComponentCode", "ObeCourseOutcomes");
    }
}
