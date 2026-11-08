using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261108090000_SmartOmrEvidence")]
public sealed class SmartOmrEvidence : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("OmrTemplates", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            AssessmentComponentId = t.Column<long>("bigint", nullable: false),
            Name = t.Column<string>("nvarchar(160)", maxLength: 160, nullable: false),
            AnswerKey = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            OptionsPerQuestion = t.Column<int>("int", nullable: false),
            CorrectAnswerMarks = t.Column<decimal>("decimal(18,2)", nullable: false),
            IncorrectAnswerMarks = t.Column<decimal>("decimal(18,2)", nullable: false),
            SealedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            SealedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_OmrTemplates", x => x.Id);
            t.UniqueConstraint("AK_OmrTemplates_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_OmrTemplates_Marks", "[OptionsPerQuestion] IN (4,5) AND LEN([AnswerKey]) BETWEEN 1 AND 200 AND [CorrectAnswerMarks] > 0 AND [IncorrectAnswerMarks] >= 0");
            t.ForeignKey("FK_OmrTemplates_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrTemplates_AssessmentComponents_TenantId_AssessmentComponentId",
                x => new { x.TenantId, x.AssessmentComponentId }, "AssessmentComponents",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrTemplates_AspNetUsers_SealedByUserId", x => x.SealedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateTable("OmrSheets", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            OmrTemplateId = t.Column<long>("bigint", nullable: false),
            StudentSubjectRegistrationId = t.Column<long>("bigint", nullable: false),
            FileAssetId = t.Column<long>("bigint", nullable: false),
            SuggestedAnswers = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            SuggestionStates = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            ConfirmedAnswers = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            State = t.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            UncertainCount = t.Column<int>("int", nullable: false),
            CapturedByUserId = t.Column<long>("bigint", nullable: false),
            ReviewedByUserId = t.Column<long>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            PostedByUserId = t.Column<long>("bigint", nullable: true),
            PostedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_OmrSheets", x => x.Id);
            t.UniqueConstraint("AK_OmrSheets_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_OmrSheets_State", "[State] IN ('Captured','Reviewed','Posted') AND [UncertainCount] BETWEEN 0 AND 200");
            t.ForeignKey("FK_OmrSheets_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheets_OmrTemplates_TenantId_OmrTemplateId",
                x => new { x.TenantId, x.OmrTemplateId }, "OmrTemplates",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheets_StudentSubjectRegistrations_TenantId_StudentSubjectRegistrationId",
                x => new { x.TenantId, x.StudentSubjectRegistrationId }, "StudentSubjectRegistrations",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheets_FileAssets_TenantId_FileAssetId",
                x => new { x.TenantId, x.FileAssetId }, "FileAssets",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheets_AspNetUsers_CapturedByUserId", x => x.CapturedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheets_AspNetUsers_ReviewedByUserId", x => x.ReviewedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheets_AspNetUsers_PostedByUserId", x => x.PostedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateTable("OmrSheetEvents", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            OmrSheetId = t.Column<long>("bigint", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            Action = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            BeforeAnswers = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            AfterAnswers = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
            ActorUserId = t.Column<long>("bigint", nullable: false),
            OccurredAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_OmrSheetEvents", x => x.Id);
            t.UniqueConstraint("AK_OmrSheetEvents_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_OmrSheetEvents_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheetEvents_OmrSheets_TenantId_OmrSheetId",
                x => new { x.TenantId, x.OmrSheetId }, "OmrSheets",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_OmrSheetEvents_AspNetUsers_ActorUserId", x => x.ActorUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_OmrTemplates_PublicId", "OmrTemplates", "PublicId", unique: true);
        m.CreateIndex("IX_OmrTemplates_TenantId_AssessmentComponentId", "OmrTemplates",
            new[] { "TenantId", "AssessmentComponentId" });
        m.CreateIndex("IX_OmrTemplates_SealedByUserId", "OmrTemplates", "SealedByUserId");
        m.CreateIndex("IX_OmrSheets_PublicId", "OmrSheets", "PublicId", unique: true);
        m.CreateIndex("IX_OmrSheets_TenantId_ClientRequestId", "OmrSheets",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_OmrSheets_TenantId_OmrTemplateId_StudentSubjectRegistrationId", "OmrSheets",
            new[] { "TenantId", "OmrTemplateId", "StudentSubjectRegistrationId" }, unique: true);
        m.CreateIndex("IX_OmrSheets_TenantId_FileAssetId", "OmrSheets",
            new[] { "TenantId", "FileAssetId" }, unique: true);
        m.CreateIndex("IX_OmrSheets_TenantId_StudentSubjectRegistrationId", "OmrSheets",
            new[] { "TenantId", "StudentSubjectRegistrationId" });
        m.CreateIndex("IX_OmrSheets_TenantId_State_CreatedAt", "OmrSheets",
            new[] { "TenantId", "State", "CreatedAt" });
        m.CreateIndex("IX_OmrSheets_CapturedByUserId", "OmrSheets", "CapturedByUserId");
        m.CreateIndex("IX_OmrSheets_ReviewedByUserId", "OmrSheets", "ReviewedByUserId");
        m.CreateIndex("IX_OmrSheets_PostedByUserId", "OmrSheets", "PostedByUserId");
        m.CreateIndex("IX_OmrSheetEvents_TenantId_ClientRequestId", "OmrSheetEvents",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_OmrSheetEvents_TenantId_OmrSheetId_Id", "OmrSheetEvents",
            new[] { "TenantId", "OmrSheetId", "Id" });
        m.CreateIndex("IX_OmrSheetEvents_ActorUserId", "OmrSheetEvents", "ActorUserId");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("OmrSheetEvents");
        m.DropTable("OmrSheets");
        m.DropTable("OmrTemplates");
    }
}
