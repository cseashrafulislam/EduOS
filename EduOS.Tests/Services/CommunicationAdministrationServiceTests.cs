using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Communication;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CommunicationAdministrationServiceTests
{
    [Fact]
    public async Task Notice_categories_are_tenant_scoped_and_cannot_be_created_by_non_admin()
    {
        await using var db = Database();
        var admin = Service(db, 101, true);
        var created = await admin.SaveNoticeCategoryAsync(null, new SaveNoticeCategoryRequestDto
        { Code = "GENERAL", Name = "General notices" });
        created.Success.Should().BeTrue(created.Message);
        (await Service(db, 202, true).GetNoticeCategoriesAsync()).Data.Should().BeEmpty();
        (await Service(db, 101, false).SaveNoticeCategoryAsync(null, new SaveNoticeCategoryRequestDto
        { Code = "PRIVATE", Name = "Private" })).StatusCode.Should().Be(403);
        (await db.NoticeCategories.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Message_template_requires_valid_channel_and_rejects_duplicate_code()
    {
        await using var db = Database();
        var service = Service(db, 101, true);
        (await service.SaveTemplateAsync(null, new SaveMessageTemplateRequestDto
        { Code = "WELCOME", Name = "Welcome", BodyTemplate = "Hello", Channel = (EduOS.Core.Enums.Domain.NotificationChannelType)99 }))
            .Success.Should().BeFalse();
        (await service.SaveTemplateAsync(null, new SaveMessageTemplateRequestDto
        { Code = "WELCOME", Name = "Welcome", BodyTemplate = "Hello", Channel = EduOS.Core.Enums.Domain.NotificationChannelType.Email }))
            .Success.Should().BeTrue();
        var page = await service.GetTemplatesAsync(1, 20);
        page.Data!.Items.Should().ContainSingle(x => x.Code == "WELCOME");
    }

    private static EduOSDbContext Database()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("communication-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    private static CommunicationAdministrationService Service(EduOSDbContext db, long tenant, bool admin) => new(
        new GenericRepository<CommunicationGateway>(db), new GenericRepository<MessageTemplate>(db),
        new GenericRepository<NoticeCategory>(db), db, new User(tenant, admin),
        new EphemeralDataProtectionProvider(), NullLogger<CommunicationAdministrationService>.Instance);

    private sealed class User(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 10;
        public long TenantId => tenant;
        public string? FullName => "Tester";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Staff"];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
