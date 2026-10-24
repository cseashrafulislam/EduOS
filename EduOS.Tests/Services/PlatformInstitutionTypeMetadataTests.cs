using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PlatformInstitutionTypeMetadataTests
{
    [Fact]
    public async Task Superadmin_updates_metadata_and_retry_has_one_audit()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition
        {
            Code = "TEST_SCHOOL", Name = "Old School", NameBangla = "পুরোনো",
            DefaultAcademicCycle = AcademicCycleType.Annual, IsActive = true,
            RowVersion = [1, 2, 3]
        };
        db.InstitutionTypeDefinitions.Add(type);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var request = new SaveInstitutionTypeMetadataRequestDto
        {
            RequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(type.RowVersion),
            Name = " New School ", NameBangla = " নতুন ", Description = " Updated ",
            AcademicCycleType = "Semester", DisplayOrder = 42
        };
        var changed = await service.SaveInstitutionTypeMetadataAsync(type.Id, request);
        changed.Success.Should().BeTrue(changed.Message);
        changed.Data!.Name.Should().Be("New School");
        changed.Data.NameBangla.Should().Be("নতুন");
        changed.Data.AcademicCycleType.Should().Be("Semester");
        changed.Data.DisplayOrder.Should().Be(42);
        (await service.SaveInstitutionTypeMetadataAsync(type.Id, request)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "InstitutionType.MetadataChanged")).Should().Be(1);
        type.Code.Should().Be("TEST_SCHOOL");
        type.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_admin_cannot_read_or_modify_metadata()
    {
        await using var db = Context();
        var service = Service(db, new User(false));
        (await service.GetAdminInstitutionTypesAsync()).StatusCode.Should().Be(403);
        (await service.SaveInstitutionTypeMetadataAsync(1, new SaveInstitutionTypeMetadataRequestDto
        {
            RequestId = Guid.NewGuid(), RowVersion = "AQID", Name = "Denied",
            AcademicCycleType = "Annual"
        })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Stale_version_and_unknown_cycle_do_not_change_metadata()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition
        {
            Code = "TEST_SCHOOL", Name = "Original", RowVersion = [1, 2, 3]
        };
        db.InstitutionTypeDefinitions.Add(type);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var stale = await service.SaveInstitutionTypeMetadataAsync(type.Id,
            new SaveInstitutionTypeMetadataRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = "CQkJ",
                Name = "Changed", AcademicCycleType = "Annual"
            });
        stale.StatusCode.Should().Be(409);
        var invalid = await service.SaveInstitutionTypeMetadataAsync(type.Id,
            new SaveInstitutionTypeMetadataRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = "AQID",
                Name = "Changed", AcademicCycleType = "99"
            });
        invalid.StatusCode.Should().Be(400);
        type.Name.Should().Be("Original");
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "InstitutionType.MetadataChanged")).Should().Be(0);
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("institution-metadata-" + Guid.NewGuid().ToString("N")).Options);

    private static PlatformCatalogService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<InstitutionTypeDefinition>(db),
        new GenericRepository<InstitutionTypeModule>(db),
        new GenericRepository<ProductModule>(db),
        NullLogger<PlatformCatalogService>.Instance, db, user);

    private sealed class User(bool superAdmin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 50;
        public long TenantId => superAdmin ? 0 : 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => superAdmin;
        public bool IsTenantAdmin => !superAdmin;
        public IReadOnlyList<string> Roles => superAdmin ? ["SuperAdmin"] : ["TenantAdmin"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
