using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261101070000_V3AlumniEventsEngagement")]
public sealed class V3AlumniEventsEngagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AlumniEvents", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CreateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            CampusId = table.Column<long>(type: "bigint", nullable: false),
            Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
            Description = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: true),
            StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            Status = table.Column<byte>(type: "tinyint", nullable: false),
            CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
            UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniEvents", x => x.Id);
            table.UniqueConstraint("AK_AlumniEvents_Tenant_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_AlumniEvents_Time", "[EndsAtUtc] > [StartsAtUtc]");
            table.CheckConstraint("CK_AlumniEvents_Status", "[Status] IN (1, 2, 3)");
            table.ForeignKey("FK_AlumniEvents_Campus", x => new { x.TenantId, x.CampusId },
                "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_AlumniEvents_Ref", "AlumniEvents", new[] { "TenantId", "PublicId" }, unique: true);
        migrationBuilder.CreateIndex("UX_AlumniEvents_Create", "AlumniEvents", new[] { "TenantId", "CreateRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AlumniEvents_List", "AlumniEvents",
            new[] { "TenantId", "CampusId", "Status", "StartsAtUtc" });

        migrationBuilder.CreateTable("AlumniEventRsvps", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            EventId = table.Column<long>(type: "bigint", nullable: false),
            AlumniProfileId = table.Column<long>(type: "bigint", nullable: false),
            Response = table.Column<byte>(type: "tinyint", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniEventRsvps", x => x.Id);
            table.CheckConstraint("CK_AlumniEventRsvps_Response", "[Response] IN (1, 2)");
            table.ForeignKey("FK_AlumniEventRsvps_Event", x => new { x.TenantId, x.EventId },
                "AlumniEvents", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AlumniEventRsvps_Profile", x => new { x.TenantId, x.AlumniProfileId },
                "AlumniProfiles", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_AlumniEventRsvps_Event_Profile", "AlumniEventRsvps",
            new[] { "TenantId", "EventId", "AlumniProfileId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AlumniEventRsvps_Report", "AlumniEventRsvps",
            new[] { "TenantId", "EventId", "Response" });

        migrationBuilder.CreateTable("AlumniEventCommands", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            EventId = table.Column<long>(type: "bigint", nullable: false),
            AlumniProfileId = table.Column<long>(type: "bigint", nullable: false),
            RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniEventCommands", x => x.Id);
            table.ForeignKey("FK_AlumniEventCommands_Event", x => new { x.TenantId, x.EventId },
                "AlumniEvents", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AlumniEventCommands_Profile", x => new { x.TenantId, x.AlumniProfileId },
                "AlumniProfiles", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_AlumniEventCommands_Request", "AlumniEventCommands",
            new[] { "TenantId", "ClientRequestId" }, unique: true);

        migrationBuilder.CreateTable("AlumniEventAudits", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            EventId = table.Column<long>(type: "bigint", nullable: false),
            ActionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniEventAudits", x => x.Id);
            table.ForeignKey("FK_AlumniEventAudits_Event", x => new { x.TenantId, x.EventId },
                "AlumniEvents", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_AlumniEventAudits_Event_Time", "AlumniEventAudits",
            new[] { "TenantId", "EventId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AlumniEventAudits");
        migrationBuilder.DropTable("AlumniEventCommands");
        migrationBuilder.DropTable("AlumniEventRsvps");
        migrationBuilder.DropTable("AlumniEvents");
    }
}
