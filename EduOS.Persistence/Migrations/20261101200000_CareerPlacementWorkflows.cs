using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Additive, SQL-owned career workflow. Existing alumni, student and employer records remain authoritative.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261101200000_CareerPlacementWorkflows")]
public sealed class CareerPlacementWorkflows : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CareerOpportunities", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            PublicId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            EmployerId = t.Column<long>(type: "bigint", nullable: false),
            CreateRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestHash = t.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            Title = t.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
            Description = t.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
            Kind = t.Column<byte>(type: "tinyint", nullable: false),
            ApplyUntilUtc = t.Column<DateTime>(type: "datetime2", nullable: false),
            Status = t.Column<byte>(type: "tinyint", nullable: false),
            CreatedByUserId = t.Column<long>(type: "bigint", nullable: false),
            ApprovedByUserId = t.Column<long>(type: "bigint", nullable: true),
            CreatedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: false),
            RowVersion = t.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CareerOpportunities", x => x.Id);
            t.UniqueConstraint("AK_CareerOpportunities_Tenant_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_CareerOpportunities_Kind", "[Kind] IN (1,2)");
            t.CheckConstraint("CK_CareerOpportunities_Status", "[Status] IN (1,2,3)");
            t.ForeignKey("FK_CareerOpportunities_Employer", x => new { x.TenantId, x.EmployerId },
                "CareerEmployers", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_CareerOpportunities_Tenant_Public", "CareerOpportunities", new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("UX_CareerOpportunities_Tenant_Request", "CareerOpportunities", new[] { "TenantId", "CreateRequestId" }, unique: true);
        m.CreateIndex("IX_CareerOpportunities_Tenant_Employer_Status_Deadline", "CareerOpportunities",
            new[] { "TenantId", "EmployerId", "Status", "ApplyUntilUtc" });

        m.CreateTable("CareerOpportunityDecisions", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            OpportunityId = t.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestHash = t.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ActionCode = t.Column<byte>(type: "tinyint", nullable: false),
            ActorUserId = t.Column<long>(type: "bigint", nullable: false),
            OccurredAtUtc = t.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CareerOpportunityDecisions", x => x.Id);
            t.CheckConstraint("CK_CareerOpportunityDecisions_Action", "[ActionCode] IN (2,3)");
            t.ForeignKey("FK_CareerOpportunityDecisions_Opportunity", x => new { x.TenantId, x.OpportunityId },
                "CareerOpportunities", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_CareerOpportunityDecisions_Request", "CareerOpportunityDecisions", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("UX_CareerOpportunityDecisions_Action", "CareerOpportunityDecisions", new[] { "TenantId", "OpportunityId", "ActionCode" }, unique: true);

        m.CreateTable("CareerApplications", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            PublicId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            OpportunityId = t.Column<long>(type: "bigint", nullable: false),
            StudentId = t.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestHash = t.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            Status = t.Column<byte>(type: "tinyint", nullable: false),
            ProposedOutcome = t.Column<byte>(type: "tinyint", nullable: true),
            ProposedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ApprovedByUserId = t.Column<long>(type: "bigint", nullable: true),
            AppliedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: false),
            RowVersion = t.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CareerApplications", x => x.Id);
            t.UniqueConstraint("AK_CareerApplications_Tenant_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_CareerApplications_Status", "[Status] IN (1,2,3,4)");
            t.CheckConstraint("CK_CareerApplications_Outcome", "[ProposedOutcome] IS NULL OR [ProposedOutcome] IN (2,3)");
            t.ForeignKey("FK_CareerApplications_Opportunity", x => new { x.TenantId, x.OpportunityId },
                "CareerOpportunities", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CareerApplications_Student", x => new { x.TenantId, x.StudentId },
                "Students", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_CareerApplications_Public", "CareerApplications", new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("UX_CareerApplications_Request", "CareerApplications", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("UX_CareerApplications_OneStudent", "CareerApplications", new[] { "TenantId", "OpportunityId", "StudentId" }, unique: true);
        m.CreateIndex("IX_CareerApplications_Status", "CareerApplications", new[] { "TenantId", "OpportunityId", "Status", "AppliedAtUtc" });

        m.CreateTable("CareerApplicationDecisions", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            ApplicationId = t.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestHash = t.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ActionCode = t.Column<byte>(type: "tinyint", nullable: false),
            Outcome = t.Column<byte>(type: "tinyint", nullable: false),
            ActorUserId = t.Column<long>(type: "bigint", nullable: false),
            OccurredAtUtc = t.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CareerApplicationDecisions", x => x.Id);
            t.CheckConstraint("CK_CareerApplicationDecisions_Action", "[ActionCode] IN (1,2,3)");
            t.ForeignKey("FK_CareerApplicationDecisions_Application", x => new { x.TenantId, x.ApplicationId },
                "CareerApplications", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_CareerApplicationDecisions_Request", "CareerApplicationDecisions", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("UX_CareerApplicationDecisions_Action", "CareerApplicationDecisions", new[] { "TenantId", "ApplicationId", "ActionCode" }, unique: true);
    }

    protected override void Down(MigrationBuilder m)
    {
        // Disposable/test database only: production history must be retained for audit.
        m.DropTable("CareerApplicationDecisions");
        m.DropTable("CareerOpportunityDecisions");
        m.DropTable("CareerApplications");
        m.DropTable("CareerOpportunities");
    }
}
