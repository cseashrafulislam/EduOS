using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2LibraryLiabilityPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy loans intentionally stay NULL: the historical replacement price is unknown.
            migrationBuilder.AddColumn<decimal>(
                name: "ReplacementPriceSnapshot",
                table: "BookIssues",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LibraryLiabilities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookIssueId = table.Column<long>(type: "bigint", nullable: false),
                    StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    AssessmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AssessedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StudentInvoiceId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_LibraryLiabilities", x => x.Id);
                    table.UniqueConstraint("AK_LibraryLiabilities_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LibraryLiability_Amount", "[Amount] > 0 AND [Amount] <= 999999999999.99");
                    table.CheckConstraint("CK_LibraryLiability_State", "[Kind] IN (1,2) AND [State] IN (1,2,3) AND (([State] = 3 AND [StudentInvoiceId] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL) OR ([State] = 2 AND [StudentInvoiceId] IS NULL AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL) OR ([State] = 1 AND [StudentInvoiceId] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL))");
                    table.ForeignKey(
                        name: "FK_LibraryLiabilities_BookIssues_TenantId_BookIssueId",
                        columns: x => new { x.TenantId, x.BookIssueId },
                        principalTable: "BookIssues",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryLiabilities_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryLiabilities_StudentEnrollments_TenantId_StudentEnrollmentId",
                        columns: x => new { x.TenantId, x.StudentEnrollmentId },
                        principalTable: "StudentEnrollments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryLiabilities_StudentInvoices_TenantId_StudentInvoiceId",
                        columns: x => new { x.TenantId, x.StudentInvoiceId },
                        principalTable: "StudentInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryLiabilities_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_PublicId",
                table: "LibraryLiabilities",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_BookIssueId",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "BookIssueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_CampusId_State_AssessmentDate_Id",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "CampusId", "State", "AssessmentDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_AssessmentDate_Id",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "AssessmentDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_ClientRequestId",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_PublicId",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "PublicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_StudentEnrollmentId",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "StudentEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLiabilities_TenantId_StudentInvoiceId",
                table: "LibraryLiabilities",
                columns: new[] { "TenantId", "StudentInvoiceId" },
                unique: true,
                filter: "[StudentInvoiceId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryLiabilities");

            migrationBuilder.DropColumn(
                name: "ReplacementPriceSnapshot",
                table: "BookIssues");

        }
    }
}
