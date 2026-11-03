using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// SQL-owned workflow, following the existing Career persistence convention.
// All reads and writes still use the one tenant-scoped EduOSDbContext.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103090000_ResearchEthicsWorkflow")]
public sealed class ResearchEthicsWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ResearchProposals", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CampusId = table.Column<long>(type: "bigint", nullable: false),
            AcademicProgramId = table.Column<long>(type: "bigint", nullable: true),
            CreateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
            Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            Abstract = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
            Protocol = table.Column<string>(type: "nvarchar(6000)", maxLength: 6000, nullable: false),
            ContentSha256 = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
            CurrentRevision = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
            StateCode = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            SupervisorUserId = table.Column<long>(type: "bigint", nullable: true),
            ReviewerUserId = table.Column<long>(type: "bigint", nullable: true),
            RiskCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ResearchProposals", x => x.Id);
            table.UniqueConstraint("AK_ResearchProposals_Tenant_Id", x => new { x.TenantId, x.Id });
            table.ForeignKey("FK_ResearchProposals_Campus", x => new { x.TenantId, x.CampusId },
                "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ResearchProposals_Program", x => new { x.TenantId, x.AcademicProgramId },
                "AcademicPrograms", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_ResearchProposals_State",
                "[StateCode] IN (N'Draft',N'Submitted',N'Assigned',N'SupervisorApproved',N'ReviewRecommended',N'ChangesRequested',N'EthicsApproved',N'EthicsRejected',N'Withdrawn')");
            table.CheckConstraint("CK_ResearchProposals_Revision", "[CurrentRevision] >= 0");
        });
        migrationBuilder.CreateIndex("UX_ResearchProposals_Public", "ResearchProposals", new[] { "TenantId", "PublicId" }, unique: true);
        migrationBuilder.CreateIndex("UX_ResearchProposals_CreateRequest", "ResearchProposals", new[] { "TenantId", "CreateRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ResearchProposals_Campus_State", "ResearchProposals", new[] { "TenantId", "CampusId", "StateCode", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex("IX_ResearchProposals_Owner", "ResearchProposals", new[] { "TenantId", "OwnerUserId", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex("IX_ResearchProposals_Supervisor", "ResearchProposals", new[] { "TenantId", "SupervisorUserId", "StateCode" });
        migrationBuilder.CreateIndex("IX_ResearchProposals_Reviewer", "ResearchProposals", new[] { "TenantId", "ReviewerUserId", "StateCode" });

        migrationBuilder.CreateTable("ResearchProposalRevisions", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            ProposalId = table.Column<long>(type: "bigint", nullable: false),
            RevisionNo = table.Column<int>(type: "int", nullable: false),
            Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            Abstract = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
            Protocol = table.Column<string>(type: "nvarchar(6000)", maxLength: 6000, nullable: false),
            ContentSha256 = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
            SubmittedByUserId = table.Column<long>(type: "bigint", nullable: false),
            SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ResearchProposalRevisions", x => x.Id);
            table.ForeignKey("FK_ResearchProposalRevisions_Proposal", x => new { x.TenantId, x.ProposalId },
                "ResearchProposals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_ResearchProposalRevisions_No", "[RevisionNo] > 0");
        });
        migrationBuilder.CreateIndex("UX_ResearchProposalRevisions_Number", "ResearchProposalRevisions",
            new[] { "TenantId", "ProposalId", "RevisionNo" }, unique: true);

        migrationBuilder.CreateTable("ResearchProposalEvents", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            ProposalId = table.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestSha256 = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            ActionCode = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            FromState = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            ToState = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            RevisionNo = table.Column<int>(type: "int", nullable: false),
            Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
            ResultSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
            OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ResearchProposalEvents", x => x.Id);
            table.ForeignKey("FK_ResearchProposalEvents_Proposal", x => new { x.TenantId, x.ProposalId },
                "ResearchProposals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_ResearchProposalEvents_Request", "ResearchProposalEvents",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ResearchProposalEvents_History", "ResearchProposalEvents",
            new[] { "TenantId", "ProposalId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Only for a disposable database/verified restore; live decisions are legal audit evidence.
        migrationBuilder.DropTable("ResearchProposalEvents");
        migrationBuilder.DropTable("ResearchProposalRevisions");
        migrationBuilder.DropTable("ResearchProposals");
    }
}
