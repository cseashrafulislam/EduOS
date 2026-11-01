using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive unit-unique receipt → stock issue → asset book → canonical GL attribution.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261101220000_AssetCapitalizationCommands")]
public sealed class AssetCapitalizationCommands : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("ValuationMode", "AssetDepreciationRuns", type:"nvarchar(30)",
            maxLength:30,nullable:false,defaultValue:"Automatic");
        m.AddColumn<long>("AccountingPeriodId", "AssetImpairments", type:"bigint",nullable:true);
        m.AddColumn<long>("AccountingPeriodId", "AssetDisposals", type:"bigint",nullable:true);
        m.AddColumn<long>("ProceedsClearingAccountId", "AssetDisposals", type:"bigint",nullable:true);
        m.CreateIndex("IX_AssetImpairments_TenantId_AccountingPeriodId","AssetImpairments",new[]{"TenantId","AccountingPeriodId"});
        m.CreateIndex("IX_AssetDisposals_TenantId_AccountingPeriodId","AssetDisposals",new[]{"TenantId","AccountingPeriodId"});
        m.CreateIndex("IX_AssetDisposals_TenantId_ProceedsClearingAccountId","AssetDisposals",new[]{"TenantId","ProceedsClearingAccountId"});
        m.AddForeignKey("FK_AssetImpairments_AccountingPeriods_TenantId_AccountingPeriodId","AssetImpairments",
            columns:new[]{"TenantId","AccountingPeriodId"},principalTable:"AccountingPeriods",principalColumns:new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
        m.AddForeignKey("FK_AssetDisposals_AccountingPeriods_TenantId_AccountingPeriodId","AssetDisposals",
            columns:new[]{"TenantId","AccountingPeriodId"},principalTable:"AccountingPeriods",principalColumns:new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
        m.AddForeignKey("FK_AssetDisposals_Accounts_TenantId_ProceedsClearingAccountId","AssetDisposals",
            columns:new[]{"TenantId","ProceedsClearingAccountId"},principalTable:"Accounts",principalColumns:new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
        m.CreateTable("AssetCapitalizationCommands", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable:false).Annotation("SqlServer:Identity","1, 1"),
            TenantId = t.Column<long>("bigint",nullable:false),
            PublicId = t.Column<Guid>("uniqueidentifier",nullable:false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier",nullable:false),
            AssetId = t.Column<long>("bigint",nullable:false),
            GoodsReceiptLineId = t.Column<long>("bigint",nullable:false),
            ReceiptUnitOrdinal = t.Column<int>("int",nullable:false),
            CampusId = t.Column<long>("bigint",nullable:false),
            AccountingPeriodId = t.Column<long>("bigint",nullable:false),
            CapitalizationDate = t.Column<DateOnly>("date",nullable:false),
            InServiceDate = t.Column<DateOnly>("date",nullable:false),
            StateCode = t.Column<string>("nvarchar(30)",maxLength:30,nullable:false),
            PolicyMode = t.Column<string>("nvarchar(30)",maxLength:30,nullable:false),
            DepreciationMethod = t.Column<string>("nvarchar(30)",maxLength:30,nullable:false),
            UsefulLifeMonths = t.Column<int>("int",nullable:false),
            PolicyVersion = t.Column<int>("int",nullable:false),
            ProposedCostBasis = t.Column<decimal>("decimal(18,2)",nullable:false),
            SalvageValue = t.Column<decimal>("decimal(18,2)",nullable:false),
            FixedAssetAccountId = t.Column<long>("bigint",nullable:false),
            InventoryAccountId = t.Column<long>("bigint",nullable:false),
            AccumulatedDepreciationAccountId = t.Column<long>("bigint",nullable:false),
            DepreciationExpenseAccountId = t.Column<long>("bigint",nullable:false),
            ImpairmentExpenseAccountId = t.Column<long>("bigint",nullable:false),
            DisposalGainAccountId = t.Column<long>("bigint",nullable:false),
            DisposalLossAccountId = t.Column<long>("bigint",nullable:false),
            VarianceGainAccountId = t.Column<long>("bigint",nullable:true),
            VarianceLossAccountId = t.Column<long>("bigint",nullable:true),
            AssetBookId = t.Column<long>("bigint",nullable:true),
            StockMovementId = t.Column<long>("bigint",nullable:true),
            JournalId = t.Column<long>("bigint",nullable:true),
            ReviewedByUserId = t.Column<long>("bigint",nullable:true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2",nullable:true),
            Reason = t.Column<string>("nvarchar(800)",maxLength:800,nullable:false),
            ReviewReason = t.Column<string>("nvarchar(800)",maxLength:800,nullable:true),
            CreatedAt = t.Column<DateTime>("datetime2",nullable:false),
            CreatedBy = t.Column<long>("bigint",nullable:true),
            UpdatedAt = t.Column<DateTime>("datetime2",nullable:true),
            UpdatedBy = t.Column<long>("bigint",nullable:true),
            IsDeleted = t.Column<bool>("bit",nullable:false),
            DeletedAt = t.Column<DateTime>("datetime2",nullable:true),
            DeletedBy = t.Column<long>("bigint",nullable:true),
            RowVersion = t.Column<byte[]>("rowversion",rowVersion:true,nullable:false)
        },constraints:t=>
        {
            t.PrimaryKey("PK_AssetCapitalizationCommands",x=>x.Id);
            t.UniqueConstraint("AK_AssetCapitalizationCommands_TenantId_Id",x=>new{x.TenantId,x.Id});
            t.CheckConstraint("CK_AssetCapitalizationCommand_State","[StateCode] IN (N'Submitted',N'Posted',N'Rejected')");
            t.CheckConstraint("CK_AssetCapitalizationCommand_Cost","[ReceiptUnitOrdinal] > 0 AND [UsefulLifeMonths] > 0 AND [PolicyVersion] > 0 AND [ProposedCostBasis] > 0 AND [SalvageValue] >= 0 AND [SalvageValue] <= [ProposedCostBasis]");
            t.ForeignKey("FK_AssetCapitalizationCommands_Assets_TenantId_AssetId",
                x=>new{x.TenantId,x.AssetId},"Assets",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_ProcurementGoodsReceiptLines_TenantId_GoodsReceiptLineId",
                x=>new{x.TenantId,x.GoodsReceiptLineId},"ProcurementGoodsReceiptLines",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Campuses_TenantId_CampusId",
                x=>new{x.TenantId,x.CampusId},"Campuses",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_AccountingPeriods_TenantId_AccountingPeriodId",
                x=>new{x.TenantId,x.AccountingPeriodId},"AccountingPeriods",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_AssetBooks_TenantId_AssetBookId",
                x=>new{x.TenantId,x.AssetBookId},"AssetBooks",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_InventoryMovements_TenantId_StockMovementId",
                x=>new{x.TenantId,x.StockMovementId},"InventoryMovements",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Journals_TenantId_JournalId",
                x=>new{x.TenantId,x.JournalId},"Journals",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_FixedAssetAccountId",
                x=>new{x.TenantId,x.FixedAssetAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_InventoryAccountId",
                x=>new{x.TenantId,x.InventoryAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_AccumulatedDepreciationAccountId",
                x=>new{x.TenantId,x.AccumulatedDepreciationAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_DepreciationExpenseAccountId",
                x=>new{x.TenantId,x.DepreciationExpenseAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_ImpairmentExpenseAccountId",
                x=>new{x.TenantId,x.ImpairmentExpenseAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_DisposalGainAccountId",
                x=>new{x.TenantId,x.DisposalGainAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_DisposalLossAccountId",
                x=>new{x.TenantId,x.DisposalLossAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_VarianceGainAccountId",
                x=>new{x.TenantId,x.VarianceGainAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            t.ForeignKey("FK_AssetCapitalizationCommands_Accounts_TenantId_VarianceLossAccountId",
                x=>new{x.TenantId,x.VarianceLossAccountId},"Accounts",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_AssetCapitalizationCommands_TenantId_AssetId","AssetCapitalizationCommands",
            new[]{"TenantId","AssetId"},unique:true,filter:"[IsDeleted] = 0 AND [StateCode] <> N'Rejected'");
        m.CreateIndex("IX_AssetCapitalizationCommands_TenantId_GoodsReceiptLineId_ReceiptUnitOrdinal","AssetCapitalizationCommands",
            new[]{"TenantId","GoodsReceiptLineId","ReceiptUnitOrdinal"},unique:true,filter:"[IsDeleted] = 0 AND [StateCode] <> N'Rejected'");
        m.CreateIndex("IX_AssetCapitalizationCommands_TenantId_ClientRequestId","AssetCapitalizationCommands",
            new[]{"TenantId","ClientRequestId"},unique:true);
        m.CreateIndex("IX_AssetCapitalizationCommands_PublicId","AssetCapitalizationCommands",
            "PublicId",unique:true);
        m.CreateIndex("IX_AssetCapitalizationCommands_TenantId_PublicId","AssetCapitalizationCommands",
            new[]{"TenantId","PublicId"},unique:true);
        m.CreateIndex("IX_AssetCapitalizationCommands_TenantId_JournalId","AssetCapitalizationCommands",
            new[]{"TenantId","JournalId"},unique:true,filter:"[JournalId] IS NOT NULL");
        m.CreateIndex("IX_AssetCapitalizationCommands_TenantId_StockMovementId","AssetCapitalizationCommands",
            new[]{"TenantId","StockMovementId"},unique:true,filter:"[StockMovementId] IS NOT NULL");
        foreach(var name in new[]{"AccountingPeriodId","AccumulatedDepreciationAccountId",
            "AssetBookId","CampusId","DepreciationExpenseAccountId","DisposalGainAccountId",
            "DisposalLossAccountId","FixedAssetAccountId","ImpairmentExpenseAccountId",
            "InventoryAccountId","VarianceGainAccountId","VarianceLossAccountId"})
            m.CreateIndex("IX_AssetCapitalizationCommands_TenantId_"+name,
                "AssetCapitalizationCommands",new[]{"TenantId",name});
    }

    protected override void Down(MigrationBuilder m)
    {
        // Only a disposable/not-yet-used test database can safely roll this back.
        m.DropTable("AssetCapitalizationCommands");
        m.DropForeignKey("FK_AssetImpairments_AccountingPeriods_TenantId_AccountingPeriodId","AssetImpairments");
        m.DropForeignKey("FK_AssetDisposals_AccountingPeriods_TenantId_AccountingPeriodId","AssetDisposals");
        m.DropForeignKey("FK_AssetDisposals_Accounts_TenantId_ProceedsClearingAccountId","AssetDisposals");
        m.DropIndex("IX_AssetImpairments_TenantId_AccountingPeriodId","AssetImpairments");
        m.DropIndex("IX_AssetDisposals_TenantId_AccountingPeriodId","AssetDisposals");
        m.DropIndex("IX_AssetDisposals_TenantId_ProceedsClearingAccountId","AssetDisposals");
        m.DropColumn("ValuationMode","AssetDepreciationRuns");
        m.DropColumn("AccountingPeriodId","AssetImpairments");
        m.DropColumn("AccountingPeriodId","AssetDisposals");
        m.DropColumn("ProceedsClearingAccountId","AssetDisposals");
    }
}
