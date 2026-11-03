using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Independent maker/checker approvals for campus visitor retention and legal-hold settings.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103222000_FacilityVisitorApprovedPrivacyPolicies")]
public sealed class FacilityVisitorApprovedPrivacyPolicies : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("FacilityVisitorPolicyChanges", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            CampusId = t.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            PolicyCode = t.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            ProposedValue = t.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
            PreviousValue = t.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
            PreviousVersion = t.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
            StateCode = t.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
            RequestReason = t.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            RequestedByUserId = t.Column<long>(type: "bigint", nullable: false),
            ReviewedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ReviewCommandId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            ReviewReason = t.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
            t.PrimaryKey("PK_FacilityVisitorPolicyChanges", x => x.Id);
            t.UniqueConstraint("AK_FacilityVisitorPolicyChanges_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_FacilityVisitorPolicyChanges_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityVisitorPolicyChanges_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityVisitorPolicyChanges_TenantId_ClientRequestId",
            "FacilityVisitorPolicyChanges", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_FacilityVisitorPolicyChanges_TenantId_CampusId_PolicyCode",
            "FacilityVisitorPolicyChanges", new[] { "TenantId", "CampusId", "PolicyCode" },
            unique: true, filter: "[StateCode] = N'Submitted' AND [IsDeleted] = 0");
        m.CreateIndex("IX_FacilityVisitorPolicyChanges_TenantId_CampusId_CreatedAt",
            "FacilityVisitorPolicyChanges", new[] { "TenantId", "CampusId", "CreatedAt" });
        m.AddCheckConstraint("CK_FacilityVisitorPolicyChanges_State", "FacilityVisitorPolicyChanges",
            "[StateCode] IN (N'Submitted',N'Approved',N'Rejected')");
    }

    protected override void Down(MigrationBuilder m) =>
        m.DropTable("FacilityVisitorPolicyChanges");
}
