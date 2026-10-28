using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Tenant-owned recruitment pipeline and auditable review history, no Person/Employee copy.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028062500_RecruitmentCandidateWorkflow")]
public sealed class RecruitmentCandidateWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RecruitmentCandidates", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false)
                .Annotation("SqlServer:Identity", "1, 1"),
            PublicId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CampusId = t.Column<long>(type: "bigint", nullable: false),
            DesignationId = t.Column<long>(type: "bigint", nullable: false),
            FullName = t.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            Email = t.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
            Phone = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
            AppliedOn = t.Column<DateOnly>(type: "date", nullable: false),
            InterviewOn = t.Column<DateOnly>(type: "date", nullable: true),
            ConsentConfirmedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
            Stage = t.Column<int>(type: "int", nullable: false),
            LinkedEmployeeId = t.Column<long>(type: "bigint", nullable: true),
            Notes = t.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedBy = t.Column<long>(type: "bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            UpdatedBy = t.Column<long>(type: "bigint", nullable: true),
            IsDeleted = t.Column<bool>(type: "bit", nullable: false),
            DeletedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            DeletedBy = t.Column<long>(type: "bigint", nullable: true),
            RowVersion = t.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_RecruitmentCandidates", x => x.Id);
            t.UniqueConstraint("AK_RecruitmentCandidates_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_RecruitmentCandidate_Stage",
                "[Stage] BETWEEN 1 AND 6 AND ([Stage] <> 6 OR [LinkedEmployeeId] IS NOT NULL)");
            t.ForeignKey("FK_RecruitmentCandidates_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_RecruitmentCandidates_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_RecruitmentCandidates_Designations_TenantId_DesignationId",
                x => new { x.TenantId, x.DesignationId }, "Designations", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_RecruitmentCandidates_Employees_TenantId_LinkedEmployeeId",
                x => new { x.TenantId, x.LinkedEmployeeId }, "Employees", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_RecruitmentCandidates_PublicId", "RecruitmentCandidates", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_RecruitmentCandidates_TenantId_ClientRequestId",
            "RecruitmentCandidates", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_RecruitmentCandidates_TenantId_CampusId_Stage_AppliedOn",
            "RecruitmentCandidates", new[] { "TenantId", "CampusId", "Stage", "AppliedOn" });
        migrationBuilder.CreateIndex("IX_RecruitmentCandidates_TenantId_LinkedEmployeeId",
            "RecruitmentCandidates", new[] { "TenantId", "LinkedEmployeeId" },
            unique: true, filter: "[LinkedEmployeeId] IS NOT NULL AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_RecruitmentCandidates_TenantId_DesignationId",
            "RecruitmentCandidates", new[] { "TenantId", "DesignationId" });

        migrationBuilder.CreateTable("RecruitmentStageHistories", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false)
                .Annotation("SqlServer:Identity", "1, 1"),
            RecruitmentCandidateId = t.Column<long>(type: "bigint", nullable: false),
            PreviousStage = t.Column<int>(type: "int", nullable: true),
            NewStage = t.Column<int>(type: "int", nullable: false),
            TransitionAt = t.Column<DateTime>(type: "datetime2", nullable: false),
            Reason = t.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
            ActorUserId = t.Column<long>(type: "bigint", nullable: false),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedBy = t.Column<long>(type: "bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            UpdatedBy = t.Column<long>(type: "bigint", nullable: true),
            IsDeleted = t.Column<bool>(type: "bit", nullable: false),
            DeletedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            DeletedBy = t.Column<long>(type: "bigint", nullable: true),
            RowVersion = t.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_RecruitmentStageHistories", x => x.Id);
            t.UniqueConstraint("AK_RecruitmentStageHistories_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_RecruitmentStageHistories_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_RecruitmentStageHistories_RecruitmentCandidates_TenantId_RecruitmentCandidateId",
                x => new { x.TenantId, x.RecruitmentCandidateId },
                "RecruitmentCandidates", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_RecruitmentStageHistories_TenantId_RecruitmentCandidateId_TransitionAt_Id",
            "RecruitmentStageHistories", new[] { "TenantId", "RecruitmentCandidateId", "TransitionAt", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rollback drops the new recruitment feature tables; back up exported records first.
        migrationBuilder.DropTable("RecruitmentStageHistories");
        migrationBuilder.DropTable("RecruitmentCandidates");
    }
}
