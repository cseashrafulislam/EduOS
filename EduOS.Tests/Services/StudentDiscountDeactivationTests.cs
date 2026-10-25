using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentDiscountDeactivationTests
{
    [Fact]
    public async Task Tenant_admin_deactivates_assignment_without_changing_approval_or_issued_invoice()
    {
        await using var db = Database();
        await SeedAsync(db);
        db.StudentInvoices.Add(new StudentInvoice
        {
            TenantId = 41, StudentEnrollmentId = 200, InvoiceNumber = "INV-TEST",
            InvoiceDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 10),
            Subtotal = 100m, DiscountAmount = 10m, TotalAmount = 90m, DueAmount = 90m
        });
        await db.SaveChangesAsync();
        var request = new DeactivateStudentDiscountRequestDto { RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) };
        var result = await Service(db, "TenantAdmin").DeactivateStudentDiscountAsync(100, request);
        result.StatusCode.Should().Be(200);
        result.Data!.IsActive.Should().BeFalse();
        var row = await db.StudentDiscounts.SingleAsync();
        row.ApprovedByUserId.Should().Be(7);
        row.Reason.Should().Be("Merit approval");
        row.UpdatedBy.Should().Be(9);
        row.UpdatedAt.Should().NotBeNull();
        (await db.StudentInvoices.SingleAsync()).DiscountAmount.Should().Be(10m);
        (await db.StudentInvoices.SingleAsync()).TotalAmount.Should().Be(90m);
    }

    [Fact]
    public async Task Repeated_deactivation_is_idempotent_and_preserves_audit_timestamp()
    {
        await using var db = Database();
        await SeedAsync(db);
        var service = Service(db, "TenantAdmin");
        var request = new DeactivateStudentDiscountRequestDto { RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) };
        (await service.DeactivateStudentDiscountAsync(100, request)).StatusCode.Should().Be(200);
        var timestamp = (await db.StudentDiscounts.SingleAsync()).UpdatedAt;
        (await service.DeactivateStudentDiscountAsync(100, request)).StatusCode.Should().Be(200);
        (await db.StudentDiscounts.SingleAsync()).UpdatedAt.Should().Be(timestamp);
        (await db.StudentDiscounts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Stale_row_version_cannot_deactivate_active_assignment()
    {
        await using var db = Database();
        await SeedAsync(db);
        var result = await Service(db, "TenantAdmin").DeactivateStudentDiscountAsync(100,
            new DeactivateStudentDiscountRequestDto { RowVersion = Convert.ToBase64String(new byte[] { 9, 9 }) });
        result.StatusCode.Should().Be(409);
        (await db.StudentDiscounts.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Non_admin_cannot_deactivate_discount()
    {
        await using var db = Database();
        await SeedAsync(db);
        var result = await Service(db, "Accountant").DeactivateStudentDiscountAsync(100,
            new DeactivateStudentDiscountRequestDto { RowVersion = "AQIDBA==" });
        result.StatusCode.Should().Be(403);
        (await db.StudentDiscounts.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_requests_and_missing_assignments_are_rejected()
    {
        await using var db = Database();
        var service = Service(db, "TenantAdmin");
        (await service.DeactivateStudentDiscountAsync(0,
            new DeactivateStudentDiscountRequestDto { RowVersion = "AQIDBA==" })).StatusCode.Should().Be(400);
        (await service.DeactivateStudentDiscountAsync(1,
            new DeactivateStudentDiscountRequestDto())).StatusCode.Should().Be(400);
        (await service.DeactivateStudentDiscountAsync(999,
            new DeactivateStudentDiscountRequestDto { RowVersion = "AQIDBA==" })).StatusCode.Should().Be(404);
    }

    private static async Task SeedAsync(EduOSDbContext db)
    {
        db.StudentEnrollments.Add(new StudentEnrollment
        {
            Id = 200, TenantId = 41, StudentId = 20, CampusId = 5, AcademicBatchId = 8,
            RollNo = "R-20", EnrollmentDate = new DateOnly(2026, 1, 1)
        });
        db.DiscountRules.Add(new DiscountRule { Id = 300, TenantId = 41, Name = "Merit", IsPercentage = true, Value = 10m });
        db.StudentDiscounts.Add(new StudentDiscount
        {
            Id = 100, TenantId = 41, StudentEnrollmentId = 200, DiscountRuleId = 300,
            EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, ApprovedByUserId = 7,
            Reason = "Merit approval", RowVersion = new byte[] { 1, 2, 3, 4 }
        });
        await db.SaveChangesAsync();
    }

    private static FinanceAdjustmentService Service(EduOSDbContext db, string role) =>
        new(db, new User(role), TimeProvider.System);

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("discount-deactivation-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
