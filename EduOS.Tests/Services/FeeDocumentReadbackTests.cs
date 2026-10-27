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

public sealed class FeeDocumentReadbackTests
{
    [Fact]
    public async Task Invoice_document_is_scoped_to_student_and_preserves_line_snapshots()
    {
        var store = Guid.NewGuid().ToString("N");
        await using var db = Database(41, store);
        var data = Seed(db, store);
        var service = Service(db, new User("Accountant"));
        var result = await service.GetStudentInvoiceDocumentAsync(data.student, data.invoice);
        result.StatusCode.Should().Be(200);
        result.Data!.InvoiceNumber.Should().Be("INV-41");
        result.Data.Lines.Should().ContainSingle().Which.FeeHeadName.Should().Be("Tuition snapshot");
        (await service.GetStudentInvoiceDocumentAsync(data.otherStudent, data.invoice)).StatusCode.Should().Be(404);
        (await service.GetStudentInvoiceDocumentAsync(data.student, data.foreignInvoice)).StatusCode.Should().Be(404);
        (await service.GetStudentInvoiceDocumentAsync(Guid.Empty, data.invoice)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Invoice_allocation_audit_is_tenant_owned_and_paged()
    {
        var store = Guid.NewGuid().ToString("N");
        await using var db = Database(41, store);
        var data = Seed(db, store);
        var service = Service(db, new User("TenantAdmin"));
        var result = await service.GetInvoiceAllocationAuditAsync(data.student, data.invoice, 1, 1);
        result.StatusCode.Should().Be(200);
        result.Data!.TotalAllocations.Should().Be(1);
        result.Data.Items.Should().ContainSingle().Which.ReceiptNumber.Should().Be("RCP-41");
        result.Data.GrossSuccessfulAllocations.Should().Be(50m);
        result.Data.GrossToNetDifference.Should().Be(0m);
        result.Data.MatchesCurrentInvoice.Should().BeTrue();
        (await service.GetInvoiceAllocationAuditAsync(data.otherStudent, data.invoice)).StatusCode.Should().Be(404);
        (await service.GetInvoiceAllocationAuditAsync(data.student, data.foreignInvoice)).StatusCode.Should().Be(404);
        (await service.GetInvoiceAllocationAuditAsync(data.student, data.invoice, 0, 25)).StatusCode.Should().Be(400);
        (await Service(db, new User("Accountant"))
            .GetInvoiceAllocationAuditAsync(data.student, data.invoice)).StatusCode.Should().Be(403);
        await using var foreignContext = Database(42, store);
        (await Service(foreignContext, new User("TenantAdmin"))
            .GetInvoiceAllocationAuditAsync(data.student, data.invoice)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Invoice_allocation_audit_flags_gross_to_net_discrepancy_without_changing_invoice()
    {
        var store = Guid.NewGuid().ToString("N");
        await using var db = Database(41, store);
        var data = Seed(db, store);
        db.StudentPayments.Add(new StudentPayment { Id = 43, TenantId = 41,
            PublicId = Guid.NewGuid(), StudentId = 1, ReceiptNumber = "RCP-EXTRA",
            Amount = 10m, PaymentDate = new DateOnly(2026, 10, 3) });
        db.PaymentAllocations.Add(new PaymentAllocation { Id = 53, TenantId = 41,
            StudentPaymentId = 43, StudentInvoiceId = 20, Amount = 10m });
        await db.SaveChangesAsync();
        var result = await Service(db, new User("TenantAdmin"))
            .GetInvoiceAllocationAuditAsync(data.student, data.invoice, 1, 1);
        result.StatusCode.Should().Be(200);
        result.Data!.TotalAllocations.Should().Be(2);
        result.Data.Items.Should().HaveCount(1);
        result.Data.GrossSuccessfulAllocations.Should().Be(60m);
        result.Data.GrossToNetDifference.Should().Be(10m);
        result.Data.MatchesCurrentInvoice.Should().BeFalse();
        db.StudentInvoices.Single(x => x.Id == 20).PaidAmount.Should().Be(50m);
    }

    [Fact]
    public async Task Receipt_document_is_scoped_to_student_and_preserves_allocations()
    {
        var store = Guid.NewGuid().ToString("N");
        await using var db = Database(41, store);
        var data = Seed(db, store);
        var service = Service(db, new User("TenantAdmin"));
        var result = await service.GetStudentPaymentDocumentAsync(data.student, data.payment);
        result.StatusCode.Should().Be(200);
        result.Data!.StudentCode.Should().Be("S-41");
        result.Data.Payment.Allocations.Should().ContainSingle().Which.InvoiceNumber.Should().Be("INV-41");
        (await service.GetStudentPaymentDocumentAsync(data.otherStudent, data.payment)).StatusCode.Should().Be(404);
        (await service.GetStudentPaymentDocumentAsync(data.student, data.foreignPayment)).StatusCode.Should().Be(404);
        // Posted receipts remain readable: the tenant guard rejects successful-payment deletion.
        db.StudentPayments.Single(x => x.PublicId == data.payment).IsDeleted = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        (await service.GetStudentPaymentDocumentAsync(data.student, data.payment)).StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Cashier_and_mismatched_tenant_context_cannot_read_finance_documents()
    {
        var store = Guid.NewGuid().ToString("N");
        await using var db = Database(41, store);
        var data = Seed(db, store);
        var cashier = Service(db, new User("Cashier"));
        (await cashier.GetStudentInvoiceDocumentAsync(data.student, data.invoice)).StatusCode.Should().Be(403);
        (await cashier.GetStudentPaymentDocumentAsync(data.student, data.payment)).StatusCode.Should().Be(403);
        await using var wrongContext = Database(42, store);
        var wrongService = Service(wrongContext, new User("Accountant"));
        (await wrongService.GetStudentInvoiceDocumentAsync(data.student, data.invoice)).StatusCode.Should().Be(404);
    }

    private static (Guid student, Guid otherStudent, Guid invoice, Guid payment,
        Guid foreignInvoice, Guid foreignPayment) Seed(EduOSDbContext db, string store)
    {
        var student = Guid.NewGuid(); var other = Guid.NewGuid();
        var invoice = Guid.NewGuid(); var payment = Guid.NewGuid();
        var foreignInvoice = Guid.NewGuid(); var foreignPayment = Guid.NewGuid();
        db.Students.AddRange(
            new Student { Id = 1, TenantId = 41, PublicId = student, StudentCode = "S-41", FullName = "Learner" },
            new Student { Id = 2, TenantId = 41, PublicId = other, StudentCode = "S-42", FullName = "Other" });
        db.StudentEnrollments.AddRange(
            new StudentEnrollment { Id = 10, TenantId = 41, StudentId = 1, CampusId = 5, RollNo = "1" },
            new StudentEnrollment { Id = 11, TenantId = 41, StudentId = 2, CampusId = 5, RollNo = "2" });
        db.StudentInvoices.AddRange(
            new StudentInvoice { Id = 20, TenantId = 41, PublicId = invoice, StudentEnrollmentId = 10,
                InvoiceNumber = "INV-41", InvoiceDate = new DateOnly(2026, 10, 1),
                DueDate = new DateOnly(2026, 10, 31), TotalAmount = 100m, Subtotal = 100m,
                PaidAmount = 50m, DueAmount = 50m });
        db.StudentInvoiceLines.Add(new StudentInvoiceLine { Id = 30, TenantId = 41,
            StudentInvoiceId = 20, FeeHeadId = 4, FeeHeadNameSnapshot = "Tuition snapshot",
            Description = "October", Amount = 100m, NetAmount = 100m });
        db.StudentPayments.AddRange(
            new StudentPayment { Id = 40, TenantId = 41, PublicId = payment, StudentId = 1,
                ReceiptNumber = "RCP-41", Amount = 50m, PaymentDate = new DateOnly(2026, 10, 2) });
        db.PaymentAllocations.Add(new PaymentAllocation { Id = 50, TenantId = 41,
            StudentPaymentId = 40, StudentInvoiceId = 20, Amount = 50m });
        db.SaveChanges();
        // Cross-tenant fixtures must be inserted through their own authenticated tenant
        // context. The production SaveChanges tenant guard must never be bypassed.
        using (var foreign = Database(42, store))
        {
            foreign.Students.Add(new Student { Id = 3, TenantId = 42,
                StudentCode = "FOREIGN", FullName = "Foreign" });
            foreign.StudentEnrollments.Add(new StudentEnrollment { Id = 12, TenantId = 42,
                StudentId = 3, CampusId = 5, RollNo = "3" });
            foreign.StudentInvoices.Add(new StudentInvoice { Id = 21, TenantId = 42,
                PublicId = foreignInvoice, StudentEnrollmentId = 12,
                InvoiceNumber = "INV-FOREIGN", TotalAmount = 10m });
            foreign.StudentPayments.Add(new StudentPayment { Id = 41, TenantId = 42,
                PublicId = foreignPayment, StudentId = 3,
                ReceiptNumber = "RCP-FOREIGN", Amount = 10m });
            foreign.SaveChanges();
        }
        return (student, other, invoice, payment, foreignInvoice, foreignPayment);
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

    private static EduOSDbContext Database(long tenant = 41, string? store = null)
    {
        var accessor = new LocalContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fee-documents-" + (store ?? Guid.NewGuid().ToString("N"))).Options, accessor);
    }

    // HttpContextAccessor uses a static AsyncLocal shared across instances. Separate
    // tenant fixtures need independent request contexts or the foreign seed switches
    // the original DbContext's global tenant filter to the wrong tenant.
    private sealed class LocalContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
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
