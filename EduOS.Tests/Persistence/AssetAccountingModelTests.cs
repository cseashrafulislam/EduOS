using EduOS.Core.Entities.Inventory;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class AssetAccountingModelTests
{
    [Fact]
    public void Receipt_unit_capitalization_and_event_finance_provenance_are_tenant_scoped_in_EF_model()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOS_AssetModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var capitalization = model.FindEntityType(typeof(AssetCapitalizationCommand))!;
        Assert.NotNull(capitalization);
        Assert.NotNull(capitalization.FindProperty(nameof(AssetCapitalizationCommand.GoodsReceiptLineId)));
        Assert.NotNull(capitalization.FindProperty(nameof(AssetCapitalizationCommand.ReceiptUnitOrdinal)));
        Assert.NotNull(capitalization.FindProperty(nameof(AssetCapitalizationCommand.StockMovementId)));
        Assert.NotNull(capitalization.FindProperty(nameof(AssetCapitalizationCommand.JournalId)));
        var indexes = capitalization.GetIndexes().ToArray();
        Assert.Contains(indexes, i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            { "TenantId", "GoodsReceiptLineId", "ReceiptUnitOrdinal" }));
        Assert.Contains(indexes, i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "AssetId" }));
        foreach (var fk in capitalization.GetForeignKeys())
        {
            Assert.Contains("TenantId", fk.Properties.Select(x => x.Name));
            Assert.Equal(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
        Assert.Equal(typeof(long?), model.FindEntityType(typeof(AssetDisposal))!
            .FindProperty(nameof(AssetDisposal.AccountingPeriodId))!.ClrType);
        Assert.NotNull(model.FindEntityType(typeof(AssetImpairment))!
            .FindProperty(nameof(AssetImpairment.AccountingPeriodId)));
        Assert.NotNull(model.FindEntityType(typeof(AssetDepreciationRun))!
            .FindProperty(nameof(AssetDepreciationRun.ValuationMode)));
        Assert.NotNull(db.GetService<IMigrationsAssembly>().ModelSnapshot);
    }
}
