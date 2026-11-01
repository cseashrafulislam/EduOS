using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Privacy-minimal invitation ledger. Neither destinations nor message bodies nor
/// gateway secrets are copied into the ledger. One attempt per event/profile/channel;
/// ambiguous attempts must be reconciled, never automatically retried.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261101170000_AlumniInvitationDispatch")]
public sealed class AlumniInvitationDispatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AlumniInvitationBatches", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            EventId = table.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Channel = table.Column<byte>(type: "tinyint", nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniInvitationBatches", x => x.Id);
            table.UniqueConstraint("AK_AlumniInvitationBatches_Tenant_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_AlumniInvitationBatches_Channel", "[Channel] IN (2, 3)");
            table.ForeignKey("FK_AlumniInvitationBatches_Event", x => new { x.TenantId, x.EventId },
                "AlumniEvents", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_AlumniInvitationBatches_Request", "AlumniInvitationBatches",
            new[] { "TenantId", "ClientRequestId" }, unique: true);

        migrationBuilder.CreateTable("AlumniInvitationAttempts", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            EventId = table.Column<long>(type: "bigint", nullable: false),
            AlumniProfileId = table.Column<long>(type: "bigint", nullable: false),
            BatchId = table.Column<long>(type: "bigint", nullable: false),
            Channel = table.Column<byte>(type: "tinyint", nullable: false),
            GatewayId = table.Column<long>(type: "bigint", nullable: false),
            State = table.Column<byte>(type: "tinyint", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            FailureCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniInvitationAttempts", x => x.Id);
            table.CheckConstraint("CK_AlumniInvitationAttempts_Channel", "[Channel] IN (2, 3)");
            table.CheckConstraint("CK_AlumniInvitationAttempts_State", "[State] IN (1, 2, 3, 4)");
            table.ForeignKey("FK_AlumniInvitationAttempts_Event", x => new { x.TenantId, x.EventId },
                "AlumniEvents", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AlumniInvitationAttempts_Profile", x => new { x.TenantId, x.AlumniProfileId },
                "AlumniProfiles", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AlumniInvitationAttempts_Batch", x => new { x.TenantId, x.BatchId },
                "AlumniInvitationBatches", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_AlumniInvitationAttempts_Recipient", "AlumniInvitationAttempts",
            new[] { "TenantId", "EventId", "AlumniProfileId", "Channel" }, unique: true);
        migrationBuilder.CreateIndex("IX_AlumniInvitationAttempts_Batch", "AlumniInvitationAttempts",
            new[] { "TenantId", "BatchId", "State" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.AlumniInvitationAttempts) THROW 51004, 'Invitation dispatch evidence must be retained.', 1;");
        migrationBuilder.DropTable("AlumniInvitationAttempts");
        migrationBuilder.DropTable("AlumniInvitationBatches");
    }
}
