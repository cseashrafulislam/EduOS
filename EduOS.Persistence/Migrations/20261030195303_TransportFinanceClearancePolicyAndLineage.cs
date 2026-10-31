using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransportFinanceClearancePolicyAndLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PreviousAssignmentId",
                table: "StudentTransports",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TransportBillingPolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    CalculationMode = table.Column<int>(type: "int", nullable: false),
                    AllowManualOverride = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_TransportBillingPolicies", x => x.Id);
                    table.UniqueConstraint("AK_TransportBillingPolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_TransportBillingPolicy_Mode", "[CalculationMode] IN (1,2,3)");
                    table.ForeignKey(
                        name: "FK_TransportBillingPolicies_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportBillingPolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransportFinanceClearances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceAssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    DestinationVehicleId = table.Column<long>(type: "bigint", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DestinationVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FinanceSnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CalculationMode = table.Column<int>(type: "int", nullable: false),
                    MonthlyFareAtRequest = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SuggestedFareCredit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ApprovedFareCredit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    NoAdjustmentRequired = table.Column<bool>(type: "bit", nullable: false),
                    SettlementJournalId = table.Column<long>(type: "bigint", nullable: true),
                    ReconciliationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuccessorAssignmentId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_TransportFinanceClearances", x => x.Id);
                    table.UniqueConstraint("AK_TransportFinanceClearances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_TransportFinanceClearance_Amounts", "[MonthlyFareAtRequest] >= 0 AND [SuggestedFareCredit] >= 0 AND [ApprovedFareCredit] >= 0 AND [CalculationMode] IN (1,2,3)");
                    table.CheckConstraint("CK_TransportFinanceClearance_Operation", "[Operation] IN (1,2)");
                    table.CheckConstraint("CK_TransportFinanceClearance_Settlement", "[SettlementJournalId] IS NULL OR [NoAdjustmentRequired] = 0");
                    table.CheckConstraint("CK_TransportFinanceClearance_State", "[State] IN (1,2,3,4)");
                    table.CheckConstraint("CK_TransportFinanceClearance_Target", "([Operation] = 1 AND [DestinationVehicleId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] = 2 AND [DestinationVehicleId] IS NULL AND [DestinationVersion] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_Journals_TenantId_SettlementJournalId",
                        columns: x => new { x.TenantId, x.SettlementJournalId },
                        principalTable: "Journals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_StudentEnrollments_TenantId_StudentEnrollmentId",
                        columns: x => new { x.TenantId, x.StudentEnrollmentId },
                        principalTable: "StudentEnrollments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_StudentTransports_TenantId_SourceAssignmentId",
                        columns: x => new { x.TenantId, x.SourceAssignmentId },
                        principalTable: "StudentTransports",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_StudentTransports_TenantId_SuccessorAssignmentId",
                        columns: x => new { x.TenantId, x.SuccessorAssignmentId },
                        principalTable: "StudentTransports",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_Students_TenantId_StudentId",
                        columns: x => new { x.TenantId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportFinanceClearances_Vehicles_TenantId_DestinationVehicleId",
                        columns: x => new { x.TenantId, x.DestinationVehicleId },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentTransports_TenantId_PreviousAssignmentId",
                table: "StudentTransports",
                columns: new[] { "TenantId", "PreviousAssignmentId" },
                unique: true,
                filter: "[PreviousAssignmentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StudentTransport_Predecessor",
                table: "StudentTransports",
                sql: "[PreviousAssignmentId] IS NULL OR [PreviousAssignmentId] <> [Id]");

            migrationBuilder.CreateIndex(
                name: "IX_TransportBillingPolicies_TenantId_CampusId",
                table: "TransportBillingPolicies",
                columns: new[] { "TenantId", "CampusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_CampusId",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "CampusId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_ClientRequestId",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_DestinationVehicleId",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "DestinationVehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_SettlementJournalId",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "SettlementJournalId" },
                unique: true,
                filter: "[SettlementJournalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_SourceAssignmentId_Operation_State",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "SourceAssignmentId", "Operation", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_State_CampusId_Id",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "State", "CampusId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_StudentEnrollmentId",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "StudentEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_StudentId",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportFinanceClearances_TenantId_SuccessorAssignmentId",
                table: "TransportFinanceClearances",
                columns: new[] { "TenantId", "SuccessorAssignmentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_StudentTransports_StudentTransports_TenantId_PreviousAssignmentId",
                table: "StudentTransports",
                columns: new[] { "TenantId", "PreviousAssignmentId" },
                principalTable: "StudentTransports",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentTransports_StudentTransports_TenantId_PreviousAssignmentId",
                table: "StudentTransports");

            migrationBuilder.DropTable(
                name: "TransportBillingPolicies");

            migrationBuilder.DropTable(
                name: "TransportFinanceClearances");

            migrationBuilder.DropIndex(
                name: "IX_StudentTransports_TenantId_PreviousAssignmentId",
                table: "StudentTransports");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StudentTransport_Predecessor",
                table: "StudentTransports");

            migrationBuilder.DropColumn(
                name: "PreviousAssignmentId",
                table: "StudentTransports");
        }
    }
}
