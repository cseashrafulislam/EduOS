using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
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

public sealed class FeeStructureClosureTests
{
    private static DateOnly Cutoff
    {
        get
        {
            var future = DateTime.UtcNow.AddMonths(2);
            return new DateOnly(future.Year, future.Month, DateTime.DaysInMonth(future.Year, future.Month));
        }
    }

    [Fact]
    public async Task Only_tenant_admin_can_close_a_fee_structure()
    {
        await using var db = Database();
        var result = await Service(db, new User("Accountant")).CloseFeeStructureAsync(
            1, new CloseFeeStructureRequestDto { EffectiveTo = Cutoff });
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Rejects_non_month_end_and_invalid_identifiers()
    {
        await using var db = Database();
        var service = Service(db, new User("TenantAdmin"));
        (await service.CloseFeeStructureAsync(1, new CloseFeeStructureRequestDto
            { EffectiveTo = new DateOnly(2026, 12, 30) })).StatusCode.Should().Be(400);
        (await service.CloseFeeStructureAsync(0, new CloseFeeStructureRequestDto
            { EffectiveTo = Cutoff })).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Foreign_tenant_structure_cannot_be_closed()
    {
        var name = "fee-close-" + Guid.NewGuid().ToString("N");
        await using (var foreign = Database(name, 42))
        {
            foreign.FeeStructures.Add(new FeeStructure { TenantId = 42, CampusId = 1,
                AcademicYearId = 1, Name = "Foreign", EffectiveFrom = new DateOnly(2026, 11, 1) });
            await foreign.SaveChangesAsync();
        }
        await using var db = Database(name);
        var result = await Service(db, new User("TenantAdmin")).CloseFeeStructureAsync(
            1, new CloseFeeStructureRequestDto { EffectiveTo = Cutoff });
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Future_closure_preserves_fee_lines_and_replay_does_not_mutate()
    {
        await using var db = Database();
        db.Tenants.Add(new Tenant { Id = 41, Name = "School", Code = "SCHOOL", Email = "school@example.test" });
        var row = new FeeStructure { TenantId = 41, CampusId = 1, AcademicYearId = 1,
            Name = "Tuition", EffectiveFrom = new DateOnly(2026, 11, 1),
            RowVersion = new byte[] { 1, 2, 3, 4 } };
        db.FeeStructures.Add(row);
        var head = new FeeHead { TenantId = 41, Code = "TUITION", Name = "Tuition" };
        db.FeeHeads.Add(head);
        await db.SaveChangesAsync();
        db.FeeStructureLines.Add(new FeeStructureLine { TenantId = 41, FeeStructureId = row.Id,
            FeeHeadId = head.Id, Amount = 2500m,
            Frequency = EduOS.Core.Enums.Domain.FeeFrequencyType.Monthly });
        await db.SaveChangesAsync();
        var service = Service(db, new User("TenantAdmin"));
        var request = new CloseFeeStructureRequestDto { EffectiveTo = Cutoff,
            RowVersion = Convert.ToBase64String(row.RowVersion) };
        var first = await service.CloseFeeStructureAsync(row.Id, request);
        first.StatusCode.Should().Be(200);
        first.Data!.EffectiveTo.Should().Be(Cutoff);
        first.Data.Lines.Single().Amount.Should().Be(2500m);
        first.Data.IsActive.Should().BeTrue();
        var replay = await service.CloseFeeStructureAsync(row.Id, request);
        replay.StatusCode.Should().Be(200);
        db.FeeStructures.Single().EffectiveTo.Should().Be(Cutoff);
        db.FeeStructureLines.Count().Should().Be(1);
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

    private static EduOSDbContext Database(string? name = null, long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name ?? "fee-close-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
