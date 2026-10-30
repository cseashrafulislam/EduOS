using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HostelApplicationsAndOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostelApplications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    HostelId = table.Column<long>(type: "bigint", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    StudentNameAtSubmission = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StudentCodeAtSubmission = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HostelNameAtSubmission = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OfferedBedId = table.Column<long>(type: "bigint", nullable: true),
                    OfferExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AllocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_HostelApplications", x => x.Id);
                    table.UniqueConstraint("AK_HostelApplications_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_HostelApplication_Offer", "[State] <> 3 OR ([OfferedBedId] IS NOT NULL AND [OfferExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_HostelApplication_Period", "[EndDate] IS NULL OR [EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_HostelApplications_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplications_HostelBeds_TenantId_OfferedBedId",
                        columns: x => new { x.TenantId, x.OfferedBedId },
                        principalTable: "HostelBeds",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplications_Hostels_TenantId_HostelId",
                        columns: x => new { x.TenantId, x.HostelId },
                        principalTable: "Hostels",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplications_StudentEnrollments_TenantId_StudentEnrollmentId",
                        columns: x => new { x.TenantId, x.StudentEnrollmentId },
                        principalTable: "StudentEnrollments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplications_StudentHostelAllocations_TenantId_AllocationId",
                        columns: x => new { x.TenantId, x.AllocationId },
                        principalTable: "StudentHostelAllocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplications_Students_TenantId_StudentId",
                        columns: x => new { x.TenantId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplications_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HostelApplicationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HostelApplicationId = table.Column<long>(type: "bigint", nullable: false),
                    FromState = table.Column<int>(type: "int", nullable: true),
                    ToState = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_HostelApplicationEvents", x => x.Id);
                    table.UniqueConstraint("AK_HostelApplicationEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_HostelApplicationEvents_HostelApplications_TenantId_HostelApplicationId",
                        columns: x => new { x.TenantId, x.HostelApplicationId },
                        principalTable: "HostelApplications",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplicationEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplicationEvents_TenantId_HostelApplicationId_OccurredAtUtc_Id",
                table: "HostelApplicationEvents",
                columns: new[] { "TenantId", "HostelApplicationId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_AllocationId",
                table: "HostelApplications",
                columns: new[] { "TenantId", "AllocationId" },
                unique: true,
                filter: "[AllocationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_AllocationRequestId",
                table: "HostelApplications",
                columns: new[] { "TenantId", "AllocationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_CampusId_HostelId_State_SubmittedAtUtc_Id",
                table: "HostelApplications",
                columns: new[] { "TenantId", "CampusId", "HostelId", "State", "SubmittedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_ClientRequestId",
                table: "HostelApplications",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_HostelId",
                table: "HostelApplications",
                columns: new[] { "TenantId", "HostelId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_OfferedBedId",
                table: "HostelApplications",
                columns: new[] { "TenantId", "OfferedBedId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_StudentEnrollmentId_State_StartDate",
                table: "HostelApplications",
                columns: new[] { "TenantId", "StudentEnrollmentId", "State", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplications_TenantId_StudentId",
                table: "HostelApplications",
                columns: new[] { "TenantId", "StudentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostelApplicationEvents");

            migrationBuilder.DropTable(
                name: "HostelApplications");
        }
    }
}
