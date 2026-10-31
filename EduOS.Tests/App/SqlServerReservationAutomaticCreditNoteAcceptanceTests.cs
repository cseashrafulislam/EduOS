using EduOS.Core.Entities.Accounting;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Finance;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Posted_original_invoice_paid_and_unpaid_credit_is_atomic_and_queryable_in_sql_server()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var checker = await CreateFinanceCheckerAsync(fixture);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long originalId, studentId, enrollmentId, campusId, liabilityId, periodId;
        Guid invoiceReference;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, checker))
        {
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            studentId = enrollment.StudentId;
            enrollmentId = enrollment.Id;
            campusId = enrollment.CampusId;
            var ar = new Account { TenantId = fixture.TenantId, Code = "AR-CN-" + suffix,
                Name = "Credit-note receivables", AccountType = AccountType.Asset };
            var income = new Account { TenantId = fixture.TenantId, Code = "INC-CN-" + suffix,
                Name = "Credit-note source income", AccountType = AccountType.Income };
            var liability = new Account { TenantId = fixture.TenantId, Code = "PAY-CN-" + suffix,
                Name = "Student-credit payable", AccountType = AccountType.Liability };
            var cash = new Account { TenantId = fixture.TenantId, Code = "CASH-CN-" + suffix,
                Name = "Cash in hand", AccountType = AccountType.Asset };
            db.Accounts.AddRange(ar, income, liability, cash);
            await db.SaveChangesAsync();
            liabilityId = liability.Id;
            var period = new AccountingPeriod { TenantId = fixture.TenantId,
                Name = "Reservation credit period " + suffix,
                StartDate = date.AddDays(-2), EndDate = date.AddDays(30) };
            var head = new FeeHead { TenantId = fixture.TenantId, Name = "Credit source " + suffix,
                Code = "CRSRC-" + suffix, IncomeAccountId = income.Id,
                ReceivableAccountId = ar.Id, IsActive = true };
            db.AddRange(period, head);
            await db.SaveChangesAsync();
            periodId = period.Id;

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var (source, issue) = await ReservationAutomaticInvoicePosting.PrepareAsync(
                db, fixture.TenantId, checker, fixture.UserId, "HOSTEL", 19000001,
                campusId, enrollmentId, studentId, 200m, head.Id, period.Id,
                date, DateTime.UtcNow, default);
            Assert.Null(issue);
            Assert.NotNull(source);
            Assert.True(await ReservationAutomaticInvoicePosting.FinalizeAsync(db,
                fixture.TenantId, "HOSTEL", 19000001, source!.JournalId, campusId,
                studentId, enrollmentId, 200m, fixture.UserId, DateTime.UtcNow, default));
            originalId = source.InvoiceId;
            var invoice = await db.StudentInvoices.SingleAsync(x => x.Id == originalId);
            invoiceReference = invoice.PublicId;

            var payment = new StudentPayment { TenantId = fixture.TenantId,
                StudentId = studentId, ReceiptNumber = "CN-REC-" + suffix,
                PaymentDate = date, PaymentMethod = PaymentMethodType.Cash,
                Amount = 80m, State = PaymentState.Successful,
                ReceivedByUserId = fixture.UserId };
            db.StudentPayments.Add(payment);
            await db.SaveChangesAsync();
            var receipt = new Journal { TenantId = fixture.TenantId,
                JournalNumber = "CN-JRN-" + suffix, JournalDate = date,
                AccountingPeriodId = period.Id, CampusId = campusId,
                State = JournalState.Posted, SourceType = "StudentPayment",
                SourceId = payment.Id, PostedByUserId = checker, PostedAt = DateTime.UtcNow };
            db.Journals.Add(receipt);
            await db.SaveChangesAsync();
            db.JournalLines.AddRange(
                new JournalLine { TenantId = fixture.TenantId, JournalId = receipt.Id,
                    LineNo = 1, AccountId = cash.Id, StudentId = studentId,
                    CampusId = campusId, DebitAmount = 80m },
                new JournalLine { TenantId = fixture.TenantId, JournalId = receipt.Id,
                    LineNo = 2, AccountId = ar.Id, StudentId = studentId,
                    CampusId = campusId, CreditAmount = 80m });
            payment.JournalId = receipt.Id;
            invoice.State = InvoiceState.PartiallyPaid;
            invoice.PaidAmount = 80m;
            invoice.DueAmount = 120m;
            db.PaymentAllocations.Add(new PaymentAllocation { TenantId = fixture.TenantId,
                StudentInvoiceId = invoice.Id, StudentPaymentId = payment.Id, Amount = 80m });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        long noteId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, checker))
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var (created, issue) = await ReservationAutomaticCreditNote.PrepareAsync(db,
                fixture.TenantId, checker, fixture.UserId, "TRANSPORT", 19000002,
                campusId, studentId, enrollmentId, -150m, invoiceReference,
                liabilityId, periodId, date, DateTime.UtcNow, default);
            Assert.Null(issue);
            Assert.NotNull(created);
            noteId = created!.Value;
            Assert.Equal(JournalState.Approved,
                (await db.Journals.AsNoTracking().SingleAsync(x => x.Id == noteId)).State);
            Assert.True(await ReservationAutomaticCreditNote.FinalizeAsync(db, fixture.TenantId,
                "TRANSPORT", 19000002, noteId, campusId, studentId, enrollmentId,
                -150m, fixture.UserId, DateTime.UtcNow, default));
            await tx.CommitAsync();
        }

        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var unchanged = await db.StudentInvoices.AsNoTracking().SingleAsync(x => x.Id == originalId);
            Assert.Equal(200m, unchanged.TotalAmount);
            Assert.Equal(80m, unchanged.PaidAmount);
            Assert.Equal(120m, unchanged.DueAmount);
            var entries = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == noteId)
                .OrderBy(x => x.LineNo).ToArrayAsync();
            Assert.Equal(150m, entries.Sum(x => x.DebitAmount));
            Assert.Equal(150m, entries.Sum(x => x.CreditAmount));
            Assert.Equal(120m, entries[1].CreditAmount);
            Assert.Equal(30m, entries[2].CreditAmount);
            var balance = (await ReservationCreditNoteBalances.ForInvoicesAsync(
                db, fixture.TenantId, [originalId], default))[originalId];
            Assert.Equal(120m, balance.UnpaidReduction);
            Assert.Equal(30m, balance.StudentCreditPayable);
            Assert.Equal(JournalState.Posted,
                (await db.Journals.AsNoTracking().SingleAsync(x => x.Id == noteId)).State);
            var finance = new FeeReceivablesAgingService(db,
                new TenantAdmin(fixture.TenantId, fixture.UserId));
            var aging = await finance.GetReportAsync(new FeeReceivablesAgingQueryDto
            {
                AgingDate = date, CampusId = campusId
            });
            Assert.True(aging.Success, aging.Message);
            Assert.DoesNotContain(aging.Data!.Invoices.Items, x => x.InvoiceNumber == unchanged.InvoiceNumber);
            var register = await finance.GetReconciliationAsync(campusId,
                unchanged.InvoiceNumber, 1, 25);
            Assert.True(register.Success, register.Message);
            var row = Assert.Single(register.Data!.Rows);
            Assert.Equal("Reconciled", row.Status);
            Assert.Equal(0m, row.DueAmount);
            Assert.Equal(150m, row.ReservationCreditAmount);
            Assert.Equal(30m, row.StudentCreditPayable);
        }
    }
}
