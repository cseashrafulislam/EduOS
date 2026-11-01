using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementReturnsAndAssetPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetDepreciationPolicyAmendments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetBookId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    AccountingPeriodId = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StateCode = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    PreviousPolicyVersion = table.Column<int>(type: "int", nullable: false),
                    NewPolicyVersion = table.Column<int>(type: "int", nullable: false),
                    SuggestedRemainingMonths = table.Column<int>(type: "int", nullable: false),
                    RemainingLifeCorrectionMonths = table.Column<int>(type: "int", nullable: false),
                    ApprovedRemainingMonths = table.Column<int>(type: "int", nullable: false),
                    OpeningCarryingValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PreviousSalvageValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewSalvageValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
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
                    table.PrimaryKey("PK_AssetDepreciationPolicyAmendments", x => x.Id);
                    table.UniqueConstraint("AK_AssetDepreciationPolicyAmendments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_AssetPolicyAmendment_Remaining", "[PreviousPolicyVersion] > 0 AND [NewPolicyVersion] > [PreviousPolicyVersion] AND [SuggestedRemainingMonths] > 0 AND [ApprovedRemainingMonths] > 0 AND [NewSalvageValue] >= 0 AND [OpeningCarryingValue] >= [NewSalvageValue]");
                    table.CheckConstraint("CK_AssetPolicyAmendment_State", "[StateCode] IN (N'Submitted',N'Approved',N'Rejected')");
                    table.ForeignKey(
                        name: "FK_AssetDepreciationPolicyAmendments_AccountingPeriods_TenantId_AccountingPeriodId",
                        columns: x => new { x.TenantId, x.AccountingPeriodId },
                        principalTable: "AccountingPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationPolicyAmendments_AssetBooks_TenantId_AssetBookId",
                        columns: x => new { x.TenantId, x.AssetBookId },
                        principalTable: "AssetBooks",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationPolicyAmendments_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationPolicyAmendments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementSupplierReturnCredits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierInvoiceLineId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    AccountingPeriodId = table.Column<long>(type: "bigint", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SupplierReturnReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StateCode = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    StockValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AutomaticCreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnpaidCreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundReceivableAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StockAccountId = table.Column<long>(type: "bigint", nullable: false),
                    PayableAccountId = table.Column<long>(type: "bigint", nullable: false),
                    VarianceAccountId = table.Column<long>(type: "bigint", nullable: true),
                    RefundReceivableAccountId = table.Column<long>(type: "bigint", nullable: true),
                    StockMovementId = table.Column<long>(type: "bigint", nullable: true),
                    JournalId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
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
                    table.PrimaryKey("PK_ProcurementSupplierReturnCredits", x => x.Id);
                    table.UniqueConstraint("AK_ProcurementSupplierReturnCredits_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_SupplierReturnCredit_Amounts", "[Quantity] > 0 AND [StockValue] > 0 AND [CreditAmount] > 0 AND [UnpaidCreditAmount] >= 0 AND [RefundReceivableAmount] >= 0");
                    table.CheckConstraint("CK_SupplierReturnCredit_State", "[StateCode] IN (N'Submitted',N'Posted',N'Rejected')");
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_AccountingPeriods_TenantId_AccountingPeriodId",
                        columns: x => new { x.TenantId, x.AccountingPeriodId },
                        principalTable: "AccountingPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_Accounts_TenantId_PayableAccountId",
                        columns: x => new { x.TenantId, x.PayableAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_Accounts_TenantId_RefundReceivableAccountId",
                        columns: x => new { x.TenantId, x.RefundReceivableAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_Accounts_TenantId_StockAccountId",
                        columns: x => new { x.TenantId, x.StockAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_Accounts_TenantId_VarianceAccountId",
                        columns: x => new { x.TenantId, x.VarianceAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_InventoryMovements_TenantId_StockMovementId",
                        columns: x => new { x.TenantId, x.StockMovementId },
                        principalTable: "InventoryMovements",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_Journals_TenantId_JournalId",
                        columns: x => new { x.TenantId, x.JournalId },
                        principalTable: "Journals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_ProcurementSupplierInvoiceLines_TenantId_SupplierInvoiceLineId",
                        columns: x => new { x.TenantId, x.SupplierInvoiceLineId },
                        principalTable: "ProcurementSupplierInvoiceLines",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_ProcurementSupplierInvoices_TenantId_SupplierInvoiceId",
                        columns: x => new { x.TenantId, x.SupplierInvoiceId },
                        principalTable: "ProcurementSupplierInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierReturnCredits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationPolicyAmendments_PublicId",
                table: "AssetDepreciationPolicyAmendments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationPolicyAmendments_TenantId_AccountingPeriodId",
                table: "AssetDepreciationPolicyAmendments",
                columns: new[] { "TenantId", "AccountingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationPolicyAmendments_TenantId_AssetBookId_NewPolicyVersion",
                table: "AssetDepreciationPolicyAmendments",
                columns: new[] { "TenantId", "AssetBookId", "NewPolicyVersion" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [StateCode] <> N'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationPolicyAmendments_TenantId_CampusId_EffectiveFrom",
                table: "AssetDepreciationPolicyAmendments",
                columns: new[] { "TenantId", "CampusId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationPolicyAmendments_TenantId_ClientRequestId",
                table: "AssetDepreciationPolicyAmendments",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationPolicyAmendments_TenantId_PublicId",
                table: "AssetDepreciationPolicyAmendments",
                columns: new[] { "TenantId", "PublicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_PublicId",
                table: "ProcurementSupplierReturnCredits",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_AccountingPeriodId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "AccountingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_CampusId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "CampusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_ClientRequestId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_JournalId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "JournalId" },
                unique: true,
                filter: "[JournalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_PayableAccountId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "PayableAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_PublicId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "PublicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_RefundReceivableAccountId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "RefundReceivableAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_StockAccountId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "StockAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_StockMovementId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "StockMovementId" },
                unique: true,
                filter: "[StockMovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_SupplierInvoiceId_ReturnDate",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "SupplierInvoiceId", "ReturnDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_SupplierInvoiceLineId_StateCode",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "SupplierInvoiceLineId", "StateCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierReturnCredits_TenantId_VarianceAccountId",
                table: "ProcurementSupplierReturnCredits",
                columns: new[] { "TenantId", "VarianceAccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetDepreciationPolicyAmendments");

            migrationBuilder.DropTable(
                name: "ProcurementSupplierReturnCredits");
        }
    }
}
