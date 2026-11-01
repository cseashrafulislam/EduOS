using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Additive SQL-owned career-network employer register, on the existing EduOSDbContext.
// Historical migrations and the EF model snapshot are intentionally unchanged.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261101110000_CareerEmployerVerification")]
public sealed class CareerEmployerVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("CareerEmployers", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CampusId = table.Column<long>(type: "bigint", nullable: false),
            CreateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
            NormalizedName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
            Website = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
            Status = table.Column<byte>(type: "tinyint", nullable: false),
            CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
            VerifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CareerEmployers", x => x.Id);
            table.UniqueConstraint("AK_CareerEmployers_Tenant_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_CareerEmployers_Status", "[Status] IN (1, 2)");
            table.ForeignKey("FK_CareerEmployers_Tenant", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CareerEmployers_Campus", x => new { x.TenantId, x.CampusId },
                "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_CareerEmployers_Tenant_Public", "CareerEmployers",
            new[] { "TenantId", "PublicId" }, unique: true);
        migrationBuilder.CreateIndex("UX_CareerEmployers_Tenant_Request", "CareerEmployers",
            new[] { "TenantId", "CreateRequestId" }, unique: true);
        migrationBuilder.CreateIndex("UX_CareerEmployers_Tenant_Campus_Name", "CareerEmployers",
            new[] { "TenantId", "CampusId", "NormalizedName" }, unique: true);
        migrationBuilder.CreateIndex("IX_CareerEmployers_Tenant_Campus_Status", "CareerEmployers",
            new[] { "TenantId", "CampusId", "Status", "CreatedAtUtc" });

        migrationBuilder.CreateTable("CareerEmployerDecisions", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            EmployerId = table.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CareerEmployerDecisions", x => x.Id);
            table.ForeignKey("FK_CareerEmployerDecisions_Employer", x => new { x.TenantId, x.EmployerId },
                "CareerEmployers", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_CareerEmployerDecisions_Tenant_Request", "CareerEmployerDecisions",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_CareerEmployerDecisions_Tenant_Employer", "CareerEmployerDecisions",
            new[] { "TenantId", "EmployerId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rollback is only safe on a disposable rehearsal database, never on live employer history.
        migrationBuilder.DropTable("CareerEmployerDecisions");
        migrationBuilder.DropTable("CareerEmployers");
    }
}
