using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeStructureRegisterTests
{
    [Fact]
    public async Task Register_requires_tenant_administrator()
    {
        await using var db = Database();
        (await Service(db, new User("Accountant")).GetStructureRegisterAsync()).StatusCode.Should().Be(403);
        (await Service(db, new User("Accountant")).GetStructureAsync(1)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Register_rejects_invalid_page_and_campus()
    {
        await using var db = Database();
        var service = Service(db, new User("TenantAdmin"));
        (await service.GetStructureRegisterAsync(pageSize: 101)).StatusCode.Should().Be(400);
        (await service.GetStructureRegisterAsync(campusId: -1)).StatusCode.Should().Be(400);
        (await service.GetStructureAsync(0)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Register_includes_inactive_but_excludes_deleted_and_other_tenants()
    {
        await using var db = Database();
        db.FeeStructures.AddRange(
            new FeeStructure { TenantId = 41, CampusId = 11, AcademicYearId = 1, Name = "Old inactive", EffectiveFrom = new DateOnly(2025, 1, 1), IsActive = false },
            new FeeStructure { TenantId = 41, CampusId = 12, AcademicYearId = 1, Name = "Other campus", EffectiveFrom = new DateOnly(2026, 1, 1) },
            new FeeStructure { TenantId = 41, CampusId = 11, AcademicYearId = 1, Name = "Deleted", IsDeleted = true, EffectiveFrom = new DateOnly(2024, 1, 1) },
            new FeeStructure { TenantId = 42, CampusId = 11, AcademicYearId = 1, Name = "Foreign", EffectiveFrom = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();
        var service = Service(db, new User("TenantAdmin"));
        var all = await service.GetStructureRegisterAsync();
        all.StatusCode.Should().Be(200);
        all.Data!.TotalCount.Should().Be(2);
        var scoped = await service.GetStructureRegisterAsync(campusId: 11);
        scoped.Data!.TotalCount.Should().Be(1);
        scoped.Data.Items.Single().Name.Should().Be("Old inactive");
    }

    [Fact]
    public async Task Detail_returns_canonical_lines_and_blocks_foreign_structure()
    {
        await using var db = Database();
        var own = new FeeStructure { TenantId = 41, CampusId = 11, AcademicYearId = 1,
            Name = "Monthly", EffectiveFrom = new DateOnly(2026, 1, 1) };
        var foreign = new FeeStructure { TenantId = 42, CampusId = 11, AcademicYearId = 1,
            Name = "Foreign", EffectiveFrom = new DateOnly(2026, 1, 1) };
        db.FeeStructures.AddRange(own, foreign);
        var head = new FeeHead { TenantId = 41, Code = "TUITION", Name = "Tuition" };
        db.FeeHeads.Add(head);
        await db.SaveChangesAsync();
        db.FeeStructureLines.Add(new FeeStructureLine { TenantId = 41, FeeStructureId = own.Id,
            FeeHeadId = head.Id, Frequency = EduOS.Core.Enums.Domain.FeeFrequencyType.Monthly,
            Amount = 2500m });
        await db.SaveChangesAsync();
        var service = Service(db, new User("TenantAdmin"));
        var result = await service.GetStructureAsync(own.Id);
        result.StatusCode.Should().Be(200);
        result.Data!.Lines.Single().Amount.Should().Be(2500m);
        (await service.GetStructureAsync(foreign.Id)).StatusCode.Should().Be(404);
    }

    private static FeeBillingService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<FeeStructure>(db), new GenericRepository<FeeStructureLine>(db),
        new GenericRepository<FeeHead>(db), new GenericRepository<StudentDiscount>(db),
        new GenericRepository<DiscountRule>(db), new GenericRepository<StudentInvoice>(db),
        new GenericRepository<StudentInvoiceLine>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<PaymentAllocation>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Campus>(db), new GenericRepository<Tenant>(db),
        db, user, TimeProvider.System, NullLogger<FeeBillingService>.Instance);

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fee-structures-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
