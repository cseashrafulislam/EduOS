using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class RecruitmentOwnershipContractTests
{
    [Fact]
    public void Recruitment_api_is_tenant_admin_HR_and_CSRF_gated()
    {
        var type=typeof(RecruitmentApiController);
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x=>x.Roles=="TenantAdmin");
        Assert.Contains(type.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>(), x=>x.Policy==RequireModuleAttribute.PolicyPrefix+"HR");
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public void Recruitment_uses_tenant_ownership_and_unique_retry_and_hire_link()
    {
        var options=new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOSModel;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db=new EduOSDbContext(options);
        var entity=db.Model.FindEntityType(typeof(RecruitmentCandidate));
        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), x=>x.IsUnique &&
            x.Properties.Select(p=>p.Name).SequenceEqual(new[]{"TenantId","ClientRequestId"}));
        Assert.Contains(entity.GetIndexes(), x=>x.IsUnique &&
            x.Properties.Select(p=>p.Name).SequenceEqual(new[]{"TenantId","LinkedEmployeeId"}) &&
            x.GetFilter()=="[LinkedEmployeeId] IS NOT NULL AND [IsDeleted] = 0");
        Assert.Contains(entity.GetForeignKeys(), x=>x.Properties.Select(p=>p.Name)
            .SequenceEqual(new[]{"TenantId","CampusId"}));
        Assert.Contains(entity.GetForeignKeys(), x=>x.Properties.Select(p=>p.Name)
            .SequenceEqual(new[]{"TenantId","DesignationId"}));
        Assert.Equal((int)RecruitmentStage.Hired,6);
    }
}
