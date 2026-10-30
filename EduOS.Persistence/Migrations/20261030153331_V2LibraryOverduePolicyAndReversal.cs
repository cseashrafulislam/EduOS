using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2LibraryOverduePolicyAndReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LibraryLiability_State",
                table: "LibraryLiabilities");

            migrationBuilder.AddColumn<decimal>(
                name: "DailyRateSnapshot",
                table: "LibraryLiabilities",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GraceDaysSnapshot",
                table: "LibraryLiabilities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LibraryFinePolicyId",
                table: "LibraryLiabilities",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverdueDaysSnapshot",
                table: "LibraryLiabilities",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LibraryFinePolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaximumCharge = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFinePolicies", x => x.Id);
                    table.UniqueConstraint("AK_LibraryFinePolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LibraryFinePolicy_Terms", "[GraceDays] BETWEEN 0 AND 36500 AND [DailyRate] >= 0 AND ([MaximumCharge] IS NULL OR [MaximumCharge] > 0)");
                    table.ForeignKey(
                        name: "FK_LibraryFinePolicies_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryFinePolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_LibraryFinePolicyId",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "LibraryFinePolicyId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LibraryLiability_OverdueSnapshot",
                table: "LibraryLiabilities",
                sql: "([Kind] <> 3 AND [LibraryFinePolicyId] IS NULL AND [OverdueDaysSnapshot] IS NULL AND [DailyRateSnapshot] IS NULL AND [GraceDaysSnapshot] IS NULL) OR ([Kind] = 3 AND [LibraryFinePolicyId] IS NOT NULL AND [OverdueDaysSnapshot] > 0 AND [DailyRateSnapshot] >= 0 AND [GraceDaysSnapshot] >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LibraryLiability_State",
                table: "LibraryLiabilities",
                sql: "[Kind] IN (1,2,3) AND [State] IN (1,2,3,4) AND (([State] IN (3,4) AND [StudentInvoiceId] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL) OR ([State] = 2 AND [StudentInvoiceId] IS NULL AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL) OR ([State] = 1 AND [StudentInvoiceId] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFinePolicies_TenantId_CampusId_EffectiveFrom",
                table: "LibraryFinePolicies",
                columns: new[] { "TenantId", "CampusId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFinePolicies_TenantId_ClientRequestId",
                table: "LibraryFinePolicies",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryLiabilities_LibraryFinePolicies_TenantId_LibraryFinePolicyId",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "LibraryFinePolicyId" },
                principalTable: "LibraryFinePolicies",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryLiabilities_LibraryFinePolicies_TenantId_LibraryFinePolicyId",
                table: "LibraryLiabilities");

            migrationBuilder.DropTable(
                name: "LibraryFinePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LibraryLiabilities_TenantId_LibraryFinePolicyId",
                table: "LibraryLiabilities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LibraryLiability_OverdueSnapshot",
                table: "LibraryLiabilities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LibraryLiability_State",
                table: "LibraryLiabilities");

            migrationBuilder.DropColumn(
                name: "DailyRateSnapshot",
                table: "LibraryLiabilities");

            migrationBuilder.DropColumn(
                name: "GraceDaysSnapshot",
                table: "LibraryLiabilities");

            migrationBuilder.DropColumn(
                name: "LibraryFinePolicyId",
                table: "LibraryLiabilities");

            migrationBuilder.DropColumn(
                name: "OverdueDaysSnapshot",
                table: "LibraryLiabilities");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LibraryLiability_State",
                table: "LibraryLiabilities",
                sql: "[Kind] IN (1,2) AND [State] IN (1,2,3) AND (([State] = 3 AND [StudentInvoiceId] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL) OR ([State] = 2 AND [StudentInvoiceId] IS NULL AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL) OR ([State] = 1 AND [StudentInvoiceId] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL))");
        }
    }
}
