using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HostelFinanceClearancePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostelBillingPolicies",
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
                    table.PrimaryKey("PK_HostelBillingPolicies", x => x.Id);
                    table.UniqueConstraint("AK_HostelBillingPolicies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_HostelBillingPolicy_Mode", "[CalculationMode] IN (1,2,3)");
                    table.ForeignKey(
                        name: "FK_HostelBillingPolicies_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelBillingPolicies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HostelFinanceClearances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceAllocationId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    DestinationBedId = table.Column<long>(type: "bigint", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DestinationVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FinanceSnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CalculationMode = table.Column<int>(type: "int", nullable: false),
                    MonthlyRentAtRequest = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepositAtRequest = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SuggestedRentCredit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SuggestedDepositRefund = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ApprovedRentCredit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ApprovedDepositRefund = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    NoAdjustmentRequired = table.Column<bool>(type: "bit", nullable: false),
                    SettlementJournalId = table.Column<long>(type: "bigint", nullable: true),
                    ReconciliationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuccessorAllocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_HostelFinanceClearances", x => x.Id);
                    table.UniqueConstraint("AK_HostelFinanceClearances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_HostelFinanceClearance_Amounts", "[SuggestedRentCredit] >= 0 AND [SuggestedDepositRefund] >= 0 AND [ApprovedRentCredit] >= 0 AND [ApprovedDepositRefund] >= 0 AND [ApprovedDepositRefund] <= [DepositAtRequest] AND [CalculationMode] IN (1,2,3)");
                    table.CheckConstraint("CK_HostelFinanceClearance_Operation", "[Operation] IN (1,2)");
                    table.CheckConstraint("CK_HostelFinanceClearance_Settlement", "[SettlementJournalId] IS NULL OR [NoAdjustmentRequired] = 0");
                    table.CheckConstraint("CK_HostelFinanceClearance_State", "[State] IN (1,2,3,4)");
                    table.CheckConstraint("CK_HostelFinanceClearance_Target", "([Operation] = 1 AND [DestinationBedId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] = 2 AND [DestinationBedId] IS NULL AND [DestinationVersion] IS NULL)");
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_HostelBeds_TenantId_DestinationBedId",
                        columns: x => new { x.TenantId, x.DestinationBedId },
                        principalTable: "HostelBeds",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_Journals_TenantId_SettlementJournalId",
                        columns: x => new { x.TenantId, x.SettlementJournalId },
                        principalTable: "Journals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_StudentEnrollments_TenantId_StudentEnrollmentId",
                        columns: x => new { x.TenantId, x.StudentEnrollmentId },
                        principalTable: "StudentEnrollments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_StudentHostelAllocations_TenantId_SourceAllocationId",
                        columns: x => new { x.TenantId, x.SourceAllocationId },
                        principalTable: "StudentHostelAllocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_StudentHostelAllocations_TenantId_SuccessorAllocationId",
                        columns: x => new { x.TenantId, x.SuccessorAllocationId },
                        principalTable: "StudentHostelAllocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_Students_TenantId_StudentId",
                        columns: x => new { x.TenantId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelFinanceClearances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostelBillingPolicies_TenantId_CampusId",
                table: "HostelBillingPolicies",
                columns: new[] { "TenantId", "CampusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_CampusId",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "CampusId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_ClientRequestId",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_DestinationBedId",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "DestinationBedId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_SettlementJournalId",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "SettlementJournalId" },
                unique: true,
                filter: "[SettlementJournalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_SourceAllocationId_Operation_State",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "SourceAllocationId", "Operation", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_State_CampusId_Id",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "State", "CampusId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_StudentEnrollmentId",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "StudentEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_StudentId",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelFinanceClearances_TenantId_SuccessorAllocationId",
                table: "HostelFinanceClearances",
                columns: new[] { "TenantId", "SuccessorAllocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostelBillingPolicies");

            migrationBuilder.DropTable(
                name: "HostelFinanceClearances");
        }
    }
}
