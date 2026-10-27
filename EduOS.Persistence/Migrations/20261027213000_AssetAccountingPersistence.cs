using System;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>New additive asset accounting records; existing Assets and Journals are unchanged.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261027213000_AssetAccountingPersistence")]
public sealed class AssetAccountingPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssetBooks",
            columns: table => new
            {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    InServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MethodCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    CostBasis = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SalvageValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    CostAccountId = table.Column<long?>(type: "bigint", nullable: true),
                    AccumulatedDepreciationAccountId = table.Column<long?>(type: "bigint", nullable: true),
                    DepreciationExpenseAccountId = table.Column<long?>(type: "bigint", nullable: true),
                    ImpairmentExpenseAccountId = table.Column<long?>(type: "bigint", nullable: true),
                    DisposalGainAccountId = table.Column<long?>(type: "bigint", nullable: true),
                    DisposalLossAccountId = table.Column<long?>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "undefined", rowVersion: true, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<long?>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetBooks", x => x.Id);
                table.UniqueConstraint("AK_AssetBooks_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_AssetBook_Policy", "[UsefulLifeMonths] > 0 AND [PolicyVersion] >= 1 AND [CostBasis] >= 0 AND [SalvageValue] >= 0 AND [SalvageValue] <= [CostBasis]");
                table.ForeignKey(
                    name: "FK_AssetBooks_Assets_TenantId_AssetId",
                    columns: x => new { x.TenantId, x.AssetId },
                    principalTable: "Assets",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Campuses_TenantId_CampusId",
                    columns: x => new { x.TenantId, x.CampusId },
                    principalTable: "Campuses",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Accounts_TenantId_CostAccountId",
                    columns: x => new { x.TenantId, x.CostAccountId },
                    principalTable: "Accounts",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Accounts_TenantId_AccumulatedDepreciationAccountId",
                    columns: x => new { x.TenantId, x.AccumulatedDepreciationAccountId },
                    principalTable: "Accounts",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Accounts_TenantId_DepreciationExpenseAccountId",
                    columns: x => new { x.TenantId, x.DepreciationExpenseAccountId },
                    principalTable: "Accounts",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Accounts_TenantId_ImpairmentExpenseAccountId",
                    columns: x => new { x.TenantId, x.ImpairmentExpenseAccountId },
                    principalTable: "Accounts",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Accounts_TenantId_DisposalGainAccountId",
                    columns: x => new { x.TenantId, x.DisposalGainAccountId },
                    principalTable: "Accounts",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Accounts_TenantId_DisposalLossAccountId",
                    columns: x => new { x.TenantId, x.DisposalLossAccountId },
                    principalTable: "Accounts",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetBooks_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_AccumulatedDepreciationAccountId",
            table: "AssetBooks",
            columns: new[] { "TenantId", "AccumulatedDepreciationAccountId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_AssetId",
            table: "AssetBooks",
            columns: new[] { "TenantId", "AssetId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_CampusId_IsActive",
            table: "AssetBooks",
            columns: new[] { "TenantId", "CampusId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_CostAccountId",
            table: "AssetBooks",
            columns: new[] { "TenantId", "CostAccountId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_DepreciationExpenseAccountId",
            table: "AssetBooks",
            columns: new[] { "TenantId", "DepreciationExpenseAccountId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_DisposalGainAccountId",
            table: "AssetBooks",
            columns: new[] { "TenantId", "DisposalGainAccountId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_DisposalLossAccountId",
            table: "AssetBooks",
            columns: new[] { "TenantId", "DisposalLossAccountId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetBooks_TenantId_ImpairmentExpenseAccountId",
            table: "AssetBooks",
            columns: new[] { "TenantId", "ImpairmentExpenseAccountId" });


        migrationBuilder.CreateTable(
            name: "AssetDepreciationRuns",
            columns: table => new
            {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    AccountingPeriodId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    StateCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    JournalId = table.Column<long?>(type: "bigint", nullable: true),
                    ReversalJournalId = table.Column<long?>(type: "bigint", nullable: true),
                    ApprovedByUserId = table.Column<long?>(type: "bigint", nullable: true),
                    ApprovedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    PostedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    Remarks = table.Column<string?>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "undefined", rowVersion: true, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<long?>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetDepreciationRuns", x => x.Id);
                table.UniqueConstraint("AK_AssetDepreciationRuns_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_AssetDepreciationRun_Amount", "[TotalAmount] >= 0");
                table.ForeignKey(
                    name: "FK_AssetDepreciationRuns_Campuses_TenantId_CampusId",
                    columns: x => new { x.TenantId, x.CampusId },
                    principalTable: "Campuses",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationRuns_AccountingPeriods_TenantId_AccountingPeriodId",
                    columns: x => new { x.TenantId, x.AccountingPeriodId },
                    principalTable: "AccountingPeriods",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationRuns_Journals_TenantId_JournalId",
                    columns: x => new { x.TenantId, x.JournalId },
                    principalTable: "Journals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationRuns_Journals_TenantId_ReversalJournalId",
                    columns: x => new { x.TenantId, x.ReversalJournalId },
                    principalTable: "Journals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationRuns_AspNetUsers_ApprovedByUserId",
                    column: x => x.ApprovedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationRuns_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationRuns_ApprovedByUserId",
            table: "AssetDepreciationRuns",
            column: "ApprovedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationRuns_TenantId_AccountingPeriodId_StateCode",
            table: "AssetDepreciationRuns",
            columns: new[] { "TenantId", "AccountingPeriodId", "StateCode" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationRuns_TenantId_CampusId",
            table: "AssetDepreciationRuns",
            columns: new[] { "TenantId", "CampusId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationRuns_TenantId_ClientRequestId",
            table: "AssetDepreciationRuns",
            columns: new[] { "TenantId", "ClientRequestId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationRuns_TenantId_JournalId",
            table: "AssetDepreciationRuns",
            columns: new[] { "TenantId", "JournalId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationRuns_TenantId_ReversalJournalId",
            table: "AssetDepreciationRuns",
            columns: new[] { "TenantId", "ReversalJournalId" });


        migrationBuilder.CreateTable(
            name: "AssetDepreciationLines",
            columns: table => new
            {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetDepreciationRunId = table.Column<long>(type: "bigint", nullable: false),
                    AssetBookId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OpeningAccumulated = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ClosingAccumulated = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ClosingCarryingValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    JournalLineId = table.Column<long?>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "undefined", rowVersion: true, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<long?>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetDepreciationLines", x => x.Id);
                table.UniqueConstraint("AK_AssetDepreciationLines_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_AssetDepreciationLine_Amounts", "[Amount] >= 0 AND [OpeningAccumulated] >= 0 AND [ClosingAccumulated] >= [OpeningAccumulated] AND [ClosingCarryingValue] >= 0");
                table.ForeignKey(
                    name: "FK_AssetDepreciationLines_AssetDepreciationRuns_TenantId_AssetDepreciationRunId",
                    columns: x => new { x.TenantId, x.AssetDepreciationRunId },
                    principalTable: "AssetDepreciationRuns",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationLines_AssetBooks_TenantId_AssetBookId",
                    columns: x => new { x.TenantId, x.AssetBookId },
                    principalTable: "AssetBooks",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationLines_JournalLines_TenantId_JournalLineId",
                    columns: x => new { x.TenantId, x.JournalLineId },
                    principalTable: "JournalLines",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDepreciationLines_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationLines_TenantId_AssetBookId_PeriodEnd",
            table: "AssetDepreciationLines",
            columns: new[] { "TenantId", "AssetBookId", "PeriodEnd" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationLines_TenantId_AssetDepreciationRunId_AssetBookId",
            table: "AssetDepreciationLines",
            columns: new[] { "TenantId", "AssetDepreciationRunId", "AssetBookId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_AssetDepreciationLines_TenantId_JournalLineId",
            table: "AssetDepreciationLines",
            columns: new[] { "TenantId", "JournalLineId" });


        migrationBuilder.CreateTable(
            name: "AssetImpairments",
            columns: table => new
            {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetBookId = table.Column<long>(type: "bigint", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StateCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    JournalId = table.Column<long?>(type: "bigint", nullable: true),
                    ReversalJournalId = table.Column<long?>(type: "bigint", nullable: true),
                    ApprovedByUserId = table.Column<long?>(type: "bigint", nullable: true),
                    ApprovedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    Reason = table.Column<string?>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "undefined", rowVersion: true, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<long?>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetImpairments", x => x.Id);
                table.UniqueConstraint("AK_AssetImpairments_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_AssetImpairment_Amount", "[Amount] > 0");
                table.ForeignKey(
                    name: "FK_AssetImpairments_AssetBooks_TenantId_AssetBookId",
                    columns: x => new { x.TenantId, x.AssetBookId },
                    principalTable: "AssetBooks",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetImpairments_Journals_TenantId_JournalId",
                    columns: x => new { x.TenantId, x.JournalId },
                    principalTable: "Journals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetImpairments_Journals_TenantId_ReversalJournalId",
                    columns: x => new { x.TenantId, x.ReversalJournalId },
                    principalTable: "Journals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetImpairments_AspNetUsers_ApprovedByUserId",
                    column: x => x.ApprovedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetImpairments_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetImpairments_ApprovedByUserId",
            table: "AssetImpairments",
            column: "ApprovedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_AssetImpairments_TenantId_AssetBookId_EffectiveDate",
            table: "AssetImpairments",
            columns: new[] { "TenantId", "AssetBookId", "EffectiveDate" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetImpairments_TenantId_ClientRequestId",
            table: "AssetImpairments",
            columns: new[] { "TenantId", "ClientRequestId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_AssetImpairments_TenantId_JournalId",
            table: "AssetImpairments",
            columns: new[] { "TenantId", "JournalId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetImpairments_TenantId_ReversalJournalId",
            table: "AssetImpairments",
            columns: new[] { "TenantId", "ReversalJournalId" });


        migrationBuilder.CreateTable(
            name: "AssetDisposals",
            columns: table => new
            {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetBookId = table.Column<long>(type: "bigint", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisposalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Proceeds = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CarryingValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GainLoss = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StateCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    JournalId = table.Column<long?>(type: "bigint", nullable: true),
                    ReversalJournalId = table.Column<long?>(type: "bigint", nullable: true),
                    ApprovedByUserId = table.Column<long?>(type: "bigint", nullable: true),
                    ApprovedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "undefined", rowVersion: true, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<long?>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetDisposals", x => x.Id);
                table.UniqueConstraint("AK_AssetDisposals_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_AssetDisposal_Amounts", "[Proceeds] >= 0 AND [AccumulatedDepreciation] >= 0 AND [CarryingValue] >= 0");
                table.ForeignKey(
                    name: "FK_AssetDisposals_AssetBooks_TenantId_AssetBookId",
                    columns: x => new { x.TenantId, x.AssetBookId },
                    principalTable: "AssetBooks",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDisposals_Journals_TenantId_JournalId",
                    columns: x => new { x.TenantId, x.JournalId },
                    principalTable: "Journals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDisposals_Journals_TenantId_ReversalJournalId",
                    columns: x => new { x.TenantId, x.ReversalJournalId },
                    principalTable: "Journals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDisposals_AspNetUsers_ApprovedByUserId",
                    column: x => x.ApprovedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AssetDisposals_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssetDisposals_ApprovedByUserId",
            table: "AssetDisposals",
            column: "ApprovedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_AssetDisposals_TenantId_AssetBookId",
            table: "AssetDisposals",
            columns: new[] { "TenantId", "AssetBookId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_AssetDisposals_TenantId_ClientRequestId",
            table: "AssetDisposals",
            columns: new[] { "TenantId", "ClientRequestId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_AssetDisposals_TenantId_JournalId",
            table: "AssetDisposals",
            columns: new[] { "TenantId", "JournalId" });

        migrationBuilder.CreateIndex(
            name: "IX_AssetDisposals_TenantId_ReversalJournalId",
            table: "AssetDisposals",
            columns: new[] { "TenantId", "ReversalJournalId" });

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AssetDisposals");
        migrationBuilder.DropTable(name: "AssetImpairments");
        migrationBuilder.DropTable(name: "AssetDepreciationLines");
        migrationBuilder.DropTable(name: "AssetDepreciationRuns");
        migrationBuilder.DropTable(name: "AssetBooks");
    }
}
