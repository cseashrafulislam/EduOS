using EduOS.Core.Entities.SaaS;
using EduOS.Service.Services.Platform;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LegalDocumentPolicyTests
{
    [Fact]
    public void Current_terms_select_latest_effective_active_version_per_code()
    {
        var now = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var docs = new[]
        {
            new LegalDocument { Id = 1, Code = "TERMS", Version = "v1", IsActive = true, EffectiveAt = now.AddDays(-30) },
            new LegalDocument { Id = 2, Code = "TERMS", Version = "v2", IsActive = true, EffectiveAt = now.AddDays(-1) },
            new LegalDocument { Id = 3, Code = "TERMS", Version = "v3", IsActive = true, EffectiveAt = now.AddDays(1) },
            new LegalDocument { Id = 4, Code = "PRIVACY", Version = "v1", IsActive = false, EffectiveAt = now.AddDays(-1) },
            new LegalDocument { Id = 5, Code = "PRIVACY", Version = "v2", IsActive = true, IsDeleted = true, EffectiveAt = now.AddDays(-1) }
        };
        var selected = LegalAcceptanceService.SelectCurrent(docs, now);
        Assert.Single(selected);
        Assert.Equal(2, selected[0].Id);
    }

    [Fact]
    public void Api_enforces_superadmin_for_publication_and_antiforgery_for_writes()
    {
        var controller = typeof(EduOS.App.Controllers.Api.LegalDocumentsApiController);
        var authorize = controller.GetMethod("Publish")!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);
        Assert.Contains(authorize, a => ((Microsoft.AspNetCore.Authorization.AuthorizeAttribute)a).Roles == "SuperAdmin");
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute), true));
    }
}
