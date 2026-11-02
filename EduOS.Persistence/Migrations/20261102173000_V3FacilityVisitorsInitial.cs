using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Minimal campus visitor preregistration, independent host decision and badge lifecycle.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261102173000_V3FacilityVisitorsInitial")]
public sealed class V3FacilityVisitorsInitial : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("FacilityVisitorVisits", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CampusId = t.Column<long>(type: "bigint", nullable: false),
            HostUserId = t.Column<long>(type: "bigint", nullable: false),
            VisitorName = t.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            Purpose = t.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            VisitStartUtc = t.Column<DateTime>(type: "datetime2", nullable: false),
            VisitEndUtc = t.Column<DateTime>(type: "datetime2", nullable: false),
            StateCode = t.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            ApprovedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ApprovedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            BadgeCode = t.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
            CheckedInAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            CheckedOutAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
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
            t.PrimaryKey("PK_FacilityVisitorVisits", x => x.Id);
            t.UniqueConstraint("AK_FacilityVisitorVisits_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_FacilityVisitorVisits_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityVisitorVisits_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityVisitorVisits_TenantId_ClientRequestId",
            "FacilityVisitorVisits", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_FacilityVisitorVisits_TenantId_CampusId_StateCode_VisitStartUtc",
            "FacilityVisitorVisits", new[] { "TenantId", "CampusId", "StateCode", "VisitStartUtc" });
        m.CreateIndex("IX_FacilityVisitorVisits_TenantId_HostUserId_StateCode",
            "FacilityVisitorVisits", new[] { "TenantId", "HostUserId", "StateCode" });
        m.CreateIndex("IX_FacilityVisitorVisits_TenantId_CampusId_BadgeCode",
            "FacilityVisitorVisits", new[] { "TenantId", "CampusId", "BadgeCode" },
            unique: true, filter: "[StateCode] = N'CheckedIn' AND [IsDeleted] = 0");
        m.AddCheckConstraint("CK_FacilityVisitorVisits_Window", "FacilityVisitorVisits",
            "[VisitStartUtc] < [VisitEndUtc]");
        m.AddCheckConstraint("CK_FacilityVisitorVisits_State", "FacilityVisitorVisits",
            "[StateCode] IN (N'PendingHost',N'Approved',N'Rejected',N'CheckedIn',N'CheckedOut')");

        m.CreateTable("FacilityVisitorEvents", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            FacilityVisitorVisitId = t.Column<long>(type: "bigint", nullable: false),
            ClientCommandId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ActorUserId = t.Column<long>(type: "bigint", nullable: false),
            ActionCode = t.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            FromState = t.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            ToState = t.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            BadgeCode = t.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
            Reason = t.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
            t.PrimaryKey("PK_FacilityVisitorEvents", x => x.Id);
            t.UniqueConstraint("AK_FacilityVisitorEvents_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_FacilityVisitorEvents_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityVisitorEvents_FacilityVisitorVisits_TenantId_FacilityVisitorVisitId",
                x => new { x.TenantId, x.FacilityVisitorVisitId }, "FacilityVisitorVisits",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityVisitorEvents_TenantId_ClientCommandId",
            "FacilityVisitorEvents", new[] { "TenantId", "ClientCommandId" }, unique: true);
        m.CreateIndex("IX_FacilityVisitorEvents_TenantId_FacilityVisitorVisitId_CreatedAt",
            "FacilityVisitorEvents", new[] { "TenantId", "FacilityVisitorVisitId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("FacilityVisitorEvents");
        m.DropTable("FacilityVisitorVisits");
    }
}
