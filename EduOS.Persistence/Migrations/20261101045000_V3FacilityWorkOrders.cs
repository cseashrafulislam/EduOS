using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Campus maintenance request, independent work order and immutable command history.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261101045000_V3FacilityWorkOrders")]
public sealed class V3FacilityWorkOrders : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("FacilityServiceRequests", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CampusId = t.Column<long>(type: "bigint", nullable: false),
            FacilitySpaceId = t.Column<long>(type: "bigint", nullable: false),
            RequesterUserId = t.Column<long>(type: "bigint", nullable: false),
            CategoryCode = t.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
            PriorityCode = t.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
            Summary = t.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            Description = t.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
            StateCode = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
            TriagedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ApprovedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ApprovedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            SlaDueAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            DecisionReason = t.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
            t.PrimaryKey("PK_FacilityServiceRequests", x => x.Id);
            t.UniqueConstraint("AK_FacilityServiceRequests_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_FacilityServiceRequests_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityServiceRequests_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityServiceRequests_FacilitySpaces_TenantId_FacilitySpaceId",
                x => new { x.TenantId, x.FacilitySpaceId }, "FacilitySpaces",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityServiceRequests_TenantId_CampusId_StateCode_CreatedAt",
            "FacilityServiceRequests", new[] { "TenantId", "CampusId", "StateCode", "CreatedAt" });
        m.CreateIndex("IX_FacilityServiceRequests_TenantId_FacilitySpaceId",
            "FacilityServiceRequests", new[] { "TenantId", "FacilitySpaceId" });
        m.CreateIndex("IX_FacilityServiceRequests_TenantId_ClientRequestId",
            "FacilityServiceRequests", new[] { "TenantId", "ClientRequestId" }, unique: true);

        m.CreateTable("FacilityWorkOrders", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            FacilityServiceRequestId = t.Column<long>(type: "bigint", nullable: false),
            StateCode = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
            AssignedTechnicianUserId = t.Column<long>(type: "bigint", nullable: true),
            ScheduledStartUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            ScheduledEndUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            StartedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            CompletedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            VerifiedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            VerifiedByUserId = t.Column<long>(type: "bigint", nullable: true),
            CompletionEvidence = t.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
            PauseReason = t.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
            t.PrimaryKey("PK_FacilityWorkOrders", x => x.Id);
            t.ForeignKey("FK_FacilityWorkOrders_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityWorkOrders_FacilityServiceRequests_TenantId_FacilityServiceRequestId",
                x => new { x.TenantId, x.FacilityServiceRequestId },
                "FacilityServiceRequests", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityWorkOrders_TenantId_FacilityServiceRequestId",
            "FacilityWorkOrders", new[] { "TenantId", "FacilityServiceRequestId" }, unique: true);
        m.CreateIndex("IX_FacilityWorkOrders_TenantId_StateCode_ScheduledStartUtc",
            "FacilityWorkOrders", new[] { "TenantId", "StateCode", "ScheduledStartUtc" });

        m.CreateTable("FacilityWorkflowEvents", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            FacilityServiceRequestId = t.Column<long>(type: "bigint", nullable: false),
            ClientCommandId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ActorUserId = t.Column<long>(type: "bigint", nullable: false),
            ActionCode = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
            FromState = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
            ToState = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
            Note = t.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
            t.PrimaryKey("PK_FacilityWorkflowEvents", x => x.Id);
            t.ForeignKey("FK_FacilityWorkflowEvents_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityWorkflowEvents_FacilityServiceRequests_TenantId_FacilityServiceRequestId",
                x => new { x.TenantId, x.FacilityServiceRequestId },
                "FacilityServiceRequests", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityWorkflowEvents_TenantId_ClientCommandId",
            "FacilityWorkflowEvents", new[] { "TenantId", "ClientCommandId" }, unique: true);
        m.CreateIndex("IX_FacilityWorkflowEvents_TenantId_FacilityServiceRequestId_CreatedAt",
            "FacilityWorkflowEvents", new[] { "TenantId", "FacilityServiceRequestId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("FacilityWorkflowEvents");
        m.DropTable("FacilityWorkOrders");
        m.DropTable("FacilityServiceRequests");
    }
}