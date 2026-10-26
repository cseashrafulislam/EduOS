using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransportNormalizedIdentityUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedVehicleNumber",
                table: "Vehicles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                computedColumnSql: "UPPER(LTRIM(RTRIM([VehicleNumber])))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedLicenseNumber",
                table: "TransportDrivers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                computedColumnSql: "UPPER(LTRIM(RTRIM([LicenseNumber])))",
                stored: true);

            // Never silently merge or discard existing transport identities.
            // The deployment must resolve duplicate active values per tenant first.
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM dbo.Vehicles
    WHERE IsDeleted = 0 AND NormalizedVehicleNumber IS NOT NULL
    GROUP BY TenantId, NormalizedVehicleNumber
    HAVING COUNT_BIG(*) > 1
)
    THROW 51036, 'Transport migration blocked: duplicate active normalized vehicle numbers exist within a tenant.', 1;
IF EXISTS (
    SELECT 1 FROM dbo.TransportDrivers
    WHERE IsDeleted = 0 AND NormalizedLicenseNumber IS NOT NULL
    GROUP BY TenantId, NormalizedLicenseNumber
    HAVING COUNT_BIG(*) > 1
)
    THROW 51037, 'Transport migration blocked: duplicate active normalized driver license numbers exist within a tenant.', 1;
");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_VehicleNumber",
                table: "Vehicles");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_VehicleNumber",
                table: "Vehicles",
                columns: new[] { "TenantId", "VehicleNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_NormalizedVehicleNumber",
                table: "Vehicles",
                columns: new[] { "TenantId", "NormalizedVehicleNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDrivers_TenantId_NormalizedLicenseNumber",
                table: "TransportDrivers",
                columns: new[] { "TenantId", "NormalizedLicenseNumber" },
                unique: true,
                filter: "[LicenseNumber] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_NormalizedVehicleNumber",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_TransportDrivers_TenantId_NormalizedLicenseNumber",
                table: "TransportDrivers");

            migrationBuilder.DropColumn(
                name: "NormalizedVehicleNumber",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "NormalizedLicenseNumber",
                table: "TransportDrivers");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_VehicleNumber",
                table: "Vehicles");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_VehicleNumber",
                table: "Vehicles",
                columns: new[] { "TenantId", "VehicleNumber" },
                unique: true);
        }
    }
}
