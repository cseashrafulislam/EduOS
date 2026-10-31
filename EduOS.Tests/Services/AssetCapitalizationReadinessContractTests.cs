using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssetCapitalizationReadinessContractTests
{
    [Theory]
    [InlineData(true, true, true, true, true, 1, "BookExists")]
    [InlineData(false, false, true, true, true, 1, "AssetInactive")]
    [InlineData(true, false, false, true, true, 1, "CampusRequired")]
    [InlineData(true, false, true, false, true, 1, "AssetTrackedItemRequired")]
    [InlineData(true, false, true, true, false, 1, "CostPolicyRequired")]
    [InlineData(true, false, true, true, true, 0, "NoPostedReceiptEvidence")]
    [InlineData(true, false, true, true, true, 1, "ReviewRequired")]
    public void Preflight_never_equates_receipt_candidates_with_authorized_capitalization(
        bool active, bool book, bool campus, bool item, bool cost, int candidates, string expected)
        => Assert.Equal(expected, AssetCapitalizationStatus.Classify(active, book, campus, item, cost, candidates));

    [Fact]
    public void Preflight_is_read_only_and_role_authorized_under_existing_inventory_module()
    {
        var method = typeof(AssetLifecycleController).GetMethod("CapitalizationReadiness")!;
        Assert.Equal("{reference:guid}/capitalization-readiness",
            Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);
        var auth = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Contains("Accountant", auth.Roles);
        Assert.Contains("InventoryManager", auth.Roles);
        Assert.NotNull(typeof(IAssetLifecycleService).GetMethod("GetCapitalizationReadinessAsync"));
        Assert.NotNull(typeof(AssetLifecycleService).GetMethod("GetCapitalizationReadinessAsync"));
    }
}
