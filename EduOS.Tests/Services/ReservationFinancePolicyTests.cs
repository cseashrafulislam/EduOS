using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationFinancePolicyTests
{

    [Fact]
    public async Task Issued_unposted_invoice_blocks_reservation_mutations_before_any_payment()
    {
        await using var db = Context(101);
        db.StudentInvoices.Add(new StudentInvoice
        {
            TenantId = 101, StudentEnrollmentId = 11, ClientRequestId = Guid.NewGuid(),
            InvoiceNumber = "RF-ISSUED", InvoiceDate = new DateOnly(2026, 10, 1),
            DueDate = new DateOnly(2026, 10, 31), State = InvoiceState.Issued,
            JournalId = null, PaidAmount = 0m
        });
        await db.SaveChangesAsync();

        // Hostel reschedule and Transport extension both consume this Finance gate.
        Assert.True(await new ReservationFinancePolicy(db).HasPostedActivityAsync(101, 10, 11));
        Assert.False(await new ReservationFinancePolicy(db).HasPostedActivityAsync(101, 10, 12));
        Assert.Empty(db.Journals);
    }

    [Fact]
    public async Task Cancelled_unposted_invoice_is_not_a_blocker_but_its_posting_is_immutable()
    {
        await using var db = Context(101);
        var invoice = new StudentInvoice
        {
            TenantId = 101, StudentEnrollmentId = 11, ClientRequestId = Guid.NewGuid(),
            InvoiceNumber = "RF-CANCELLED", InvoiceDate = new DateOnly(2026, 10, 1),
            DueDate = new DateOnly(2026, 10, 31), State = InvoiceState.Cancelled
        };
        db.StudentInvoices.Add(invoice);
        await db.SaveChangesAsync();
        var policy = new ReservationFinancePolicy(db);
        Assert.False(await policy.HasPostedActivityAsync(101, 10, 11));

        // A journal reference remains financial history even when state is Cancelled.
        invoice.JournalId = 42;
        await db.SaveChangesAsync();
        Assert.True(await policy.HasPostedActivityAsync(101, 10, 11));
    }

    [Fact]
    public async Task Draft_invoice_does_not_block_but_posted_invoice_does()
    {
        await using var db = Context(101);
        var invoice = new StudentInvoice
        {
            TenantId = 101, StudentEnrollmentId = 11, ClientRequestId = Guid.NewGuid(),
            InvoiceNumber = "RF-1", InvoiceDate = new DateOnly(2026, 10, 1),
            DueDate = new DateOnly(2026, 10, 31), State = InvoiceState.Draft
        };
        db.StudentInvoices.Add(invoice);
        await db.SaveChangesAsync();
        var policy = new ReservationFinancePolicy(db);
        Assert.False(await policy.HasPostedActivityAsync(101, 10, 11));

        invoice.State = InvoiceState.Issued;
        invoice.JournalId = 99;
        await db.SaveChangesAsync();
        Assert.True(await policy.HasPostedActivityAsync(101, 10, 11));
        Assert.False(await policy.HasPostedActivityAsync(101, 10, 12));
    }

    [Fact]
    public async Task Successful_student_payment_blocks_even_when_invoice_is_not_linked()
    {
        await using var db = Context(101);
        db.StudentPayments.Add(new StudentPayment
        {
            TenantId = 101, StudentId = 10, ClientRequestId = Guid.NewGuid(),
            ReceiptNumber = "RF-P1", PaymentDate = new DateOnly(2026, 10, 2),
            ReceivedByUserId = 7, Amount = 100, State = PaymentState.Successful
        });
        await db.SaveChangesAsync();
        var policy = new ReservationFinancePolicy(db);
        Assert.True(await policy.HasPostedActivityAsync(101, 10, 11));
        Assert.False(await policy.HasPostedActivityAsync(101, 20, 11));
    }

    [Fact]
    public async Task Missing_scope_fails_closed_and_other_tenant_is_not_visible()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-finance-" + Guid.NewGuid().ToString("N")).Options;
        await using (var foreign = Context(202, options))
        {
            foreign.StudentInvoices.Add(new StudentInvoice
            {
                TenantId = 202, StudentEnrollmentId = 11, ClientRequestId = Guid.NewGuid(),
                InvoiceNumber = "RF-X", InvoiceDate = new DateOnly(2026, 10, 1),
                DueDate = new DateOnly(2026, 10, 31), State = InvoiceState.Paid,
                JournalId = 1
            });
            await foreign.SaveChangesAsync();
        }
        await using var db = Context(101, options);
        Assert.True(await new ReservationFinancePolicy(db).HasPostedActivityAsync(0, 10, 11));
        Assert.False(await new ReservationFinancePolicy(db).HasPostedActivityAsync(101, 10, 11));
    }

    private static EduOSDbContext Context(long tenant, DbContextOptions<EduOSDbContext>? options = null)
    {
        options ??= new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-finance-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
}
