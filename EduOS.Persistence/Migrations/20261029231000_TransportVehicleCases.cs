using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive operational case ledger; no finance entries or existing transport rows change.
/// Rollback drops the case ledger, so export and retain any genuine case history first.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029231000_TransportVehicleCases")]
public sealed class TransportVehicleCases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TransportVehicleCases",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                VehicleId = table.Column<long>(type: "bigint", nullable: false),
                VehicleNumberSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                CaseType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Resolution = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ResolvedBy = table.Column<long>(type: "bigint", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransportVehicleCases", x => x.Id);
                table.UniqueConstraint("AK_TransportVehicleCases_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_TransportVehicleCases_Type", "[CaseType] IN ('Maintenance', 'Incident')");
                table.CheckConstraint("CK_TransportVehicleCases_Severity", "[Severity] IN ('Low', 'Medium', 'High')");
                table.CheckConstraint("CK_TransportVehicleCases_State", "[State] IN ('Open', 'Resolved')");
                table.CheckConstraint("CK_TransportVehicleCases_Resolution",
                    "([State] = 'Open' AND [ResolvedAt] IS NULL AND [ResolvedBy] IS NULL AND [Resolution] IS NULL) OR ([State] = 'Resolved' AND [ResolvedAt] IS NOT NULL AND [ResolvedBy] IS NOT NULL AND [Resolution] IS NOT NULL)");
                table.ForeignKey("FK_TransportVehicleCases_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportVehicleCases_Campuses_TenantId_CampusId",
                    x => new { x.TenantId, x.CampusId }, "Campuses",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportVehicleCases_Vehicles_TenantId_VehicleId",
                    x => new { x.TenantId, x.VehicleId }, "Vehicles",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_TransportVehicleCases_PublicId", "TransportVehicleCases", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_TransportVehicleCases_TenantId_ClientRequestId", "TransportVehicleCases",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_TransportVehicleCases_TenantId_CampusId_OccurredOn", "TransportVehicleCases",
            new[] { "TenantId", "CampusId", "OccurredOn" });
        migrationBuilder.CreateIndex("IX_TransportVehicleCases_TenantId_VehicleId_State", "TransportVehicleCases",
            new[] { "TenantId", "VehicleId", "State" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("TransportVehicleCases");
}
