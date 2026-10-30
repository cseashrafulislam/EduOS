using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.DTOs.Payroll;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// An opt-in, disposable SQL Server acceptance test. Unlike InMemory tests, this
/// verifies real foreign keys, tenant filters, SQL rowversion, posted journals,
/// immutable result history and the paid-invoice credit-note reconciliation.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerV1FinanceCrossModuleIntegrationTests
{
    [Fact]
    public async Task Admission_published_result_and_paid_fee_credit_note_reconcile_without_rewriting_history()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        long tenantId, campusId, studentId, enrollmentId, resultSummaryId, invoiceId, paymentId;
        long refundId, periodId, cashId, receivableId, incomeId, payerId;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "V1FI-" + suffix, Name = "V1 Finance SQL CI",
                Email = "v1fi-" + suffix + "@example.test", State = TenantState.Active };
            var person = new Person { FullName = "V1 SQL Test Learner" };
            setup.AddRange(tenant, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;

            var requester = User("requester", suffix);
            var approver = User("approver", suffix);
            var payer = User("payer", suffix);
            setup.ApplicationUsers.AddRange(requester, approver, payer);
            await setup.SaveChangesAsync();
            payerId = payer.Id;

            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear { TenantId = tenantId, Name = date.Year.ToString(),
                    Code = date.Year.ToString(), StartDate = new DateOnly(date.Year, 1, 1),
                    EndDate = new DateOnly(date.Year, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "General", Code = "GEN" };
                var student = new Student { TenantId = tenantId, PersonId = person.Id,
                    StudentCode = "V1-" + suffix, FullName = person.FullName, AdmissionDate = date };
                var cash = new Account { TenantId = tenantId, Code = "CASH-HAND", Name = "Cash in hand",
                    AccountType = AccountType.Asset };
                var receivable = new Account { TenantId = tenantId, Code = "TUITION-AR", Name = "Tuition receivable",
                    AccountType = AccountType.Asset };
                var income = new Account { TenantId = tenantId, Code = "TUITION-REV", Name = "Tuition income",
                    AccountType = AccountType.Income };
                var period = new AccountingPeriod { TenantId = tenantId, Name = "Open payout CI",
                    StartDate = date.AddDays(-5), EndDate = date.AddDays(5) };
                setup.AddRange(campus, year, program, student, cash, receivable, income, period);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                studentId = student.Id;
                cashId = cash.Id;
                receivableId = receivable.Id;
                incomeId = income.Id;
                periodId = period.Id;

                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level 1", Code = "L1", LevelNo = 1 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Current", Code = "CUR", EffectiveFrom = date, IsCurrent = true };
                var fee = new FeeHead { TenantId = tenantId, Code = "TUITION", Name = "Tuition",
                    IncomeAccountId = income.Id, ReceivableAccountId = receivable.Id };
                setup.AddRange(level, curriculum, fee);
                await setup.SaveChangesAsync();

                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Batch", Code = "BATCH", Capacity = 30 };
                setup.AcademicBatches.Add(batch);
                await setup.SaveChangesAsync();

                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campus.Id, AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                    AcademicCurriculumId = curriculum.Id, RollNo = "01", EnrollmentDate = date,
                    ClientRequestId = Guid.NewGuid() };
                var assessment = new Assessment { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, Name = "Annual", Code = "ANNUAL", State = AssessmentState.Published,
                    StartDate = date, EndDate = date };
                setup.AddRange(enrollment, assessment);
                await setup.SaveChangesAsync();
                enrollmentId = enrollment.Id;

                var publication = new ResultPublication { TenantId = tenantId, AssessmentId = assessment.Id,
                    AcademicBatchId = batch.Id, VersionNo = 1, State = ResultPublicationState.Published,
                    PublishedAt = DateTime.UtcNow, PublishedByUserId = approver.Id };
                setup.ResultPublications.Add(publication);
                await setup.SaveChangesAsync();
                var summary = new StudentResultSummary { TenantId = tenantId,
                    ResultPublicationId = publication.Id, AssessmentId = assessment.Id,
                    StudentEnrollmentId = enrollment.Id, PublicationVersionNo = 1,
                    TotalMarks = 100m, ObtainedMarks = 82m, Percentage = 82m, IsPassed = true };
                setup.StudentResultSummaries.Add(summary);
                await setup.SaveChangesAsync();
                resultSummaryId = summary.Id;

                var invoice = new StudentInvoice { TenantId = tenantId, StudentEnrollmentId = enrollment.Id,
                    InvoiceNumber = "V1INV-" + suffix, InvoiceDate = date, DueDate = date,
                    ClientRequestId = Guid.NewGuid(), Subtotal = 100m, TotalAmount = 100m,
                    DueAmount = 100m, State = InvoiceState.Issued };
                var payment = new StudentPayment { TenantId = tenantId, StudentId = student.Id,
                    ReceiptNumber = "V1REC-" + suffix, PaymentDate = date, Amount = 100m,
                    ClientRequestId = Guid.NewGuid(), ReceivedByUserId = payer.Id,
                    PaymentMethod = PaymentMethodType.Cash, State = PaymentState.Initiated };
                setup.AddRange(invoice, payment);
                await setup.SaveChangesAsync();
                invoiceId = invoice.Id;
                paymentId = payment.Id;

                var invoiceJournal = PostedJournal(tenantId, periodId, campusId, invoiceId,
                    "StudentInvoice", "V1-INV-" + suffix, date, approver.Id, payer.Id);
                var receiptJournal = PostedJournal(tenantId, periodId, campusId, paymentId,
                    "StudentPayment", "V1-RCP-" + suffix, date, approver.Id, payer.Id);
                setup.Journals.AddRange(invoiceJournal, receiptJournal);
                await setup.SaveChangesAsync();
                setup.JournalLines.AddRange(
                    Line(tenantId, invoiceJournal.Id, 1, receivableId, campusId, studentId, 100m, 0m),
                    Line(tenantId, invoiceJournal.Id, 2, incomeId, campusId, studentId, 0m, 100m, fee.Id, program.Id),
                    Line(tenantId, receiptJournal.Id, 1, cashId, campusId, studentId, 100m, 0m),
                    Line(tenantId, receiptJournal.Id, 2, receivableId, campusId, studentId, 0m, 100m));
                setup.StudentInvoiceLines.Add(new StudentInvoiceLine { TenantId = tenantId,
                    StudentInvoiceId = invoice.Id, FeeHeadId = fee.Id, Description = "Tuition",
                    Amount = 100m, NetAmount = 100m });
                setup.PaymentAllocations.Add(new PaymentAllocation { TenantId = tenantId,
                    StudentInvoiceId = invoice.Id, StudentPaymentId = payment.Id, Amount = 100m });
                invoice.State = InvoiceState.Paid;
                invoice.PaidAmount = 100m;
                invoice.DueAmount = 0m;
                invoice.JournalId = invoiceJournal.Id;
                invoice.PaidAt = DateTime.UtcNow;
                payment.State = PaymentState.Successful;
                payment.JournalId = receiptJournal.Id;
                payment.CompletedAt = DateTime.UtcNow;
                await setup.SaveChangesAsync();

                var refund = new Refund { TenantId = tenantId, ClientRequestId = Guid.NewGuid(),
                    StudentPaymentId = payment.Id, Amount = 20m, Reason = "Independent approved correction",
                    State = RefundState.Approved, RequestedByUserId = requester.Id,
                    ApprovedByUserId = approver.Id, ApprovedAt = DateTime.UtcNow };
                setup.Refunds.Add(refund);
                await setup.SaveChangesAsync();
                refundId = refund.Id;
            }
        }

        await using (var db = new EduOSDbContext(options, Accessor(tenantId)))
        {
            var row = await db.Refunds.AsNoTracking().SingleAsync(x => x.Id == refundId);
            Assert.NotEmpty(row.RowVersion); // Real SQL Server-generated optimistic concurrency token.
            var request = new RecordRefundDisbursementRequestDto { ClientRequestId = Guid.NewGuid(),
                AccountingPeriodId = periodId, PayoutDate = date,
                PayoutEvidenceReference = "CASH-V1-" + suffix,
                RowVersion = Convert.ToBase64String(row.RowVersion) };
            var service = new FinanceAdjustmentService(db, new Actor(tenantId, payerId), TimeProvider.System);
            var first = await service.RecordRefundDisbursementAsync(refundId, request);
            Assert.True(first.Success, first.Message);
            var replay = await service.RecordRefundDisbursementAsync(refundId, request);
            Assert.True(replay.Success, replay.Message);

            var originalInvoice = await db.StudentInvoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId);
            Assert.Equal(InvoiceState.Paid, originalInvoice.State);
            Assert.Equal(100m, originalInvoice.TotalAmount);
            Assert.Equal(100m, originalInvoice.PaidAmount);
            Assert.Equal(0m, originalInvoice.DueAmount);
            var originalPayment = await db.StudentPayments.AsNoTracking().SingleAsync(x => x.Id == paymentId);
            Assert.Equal(PaymentState.Successful, originalPayment.State);
            Assert.Equal(100m, originalPayment.Amount);
            var creditAllocation = await db.RefundAllocations.AsNoTracking().SingleAsync(x => x.RefundId == refundId);
            Assert.True(creditAllocation.IsCreditNote);
            Assert.Equal(20m, creditAllocation.Amount);

            var notes = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.SourceId == refundId && (x.SourceType == "StudentRefundCreditNote" || x.SourceType == "StudentRefund"))
                .OrderBy(x => x.SourceType).ToListAsync();
            Assert.Equal(2, notes.Count);
            Assert.All(notes, x => Assert.Equal(JournalState.Posted, x.State));
            foreach (var journal in notes)
            {
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == journal.Id).ToListAsync();
                Assert.Equal(20m, lines.Sum(x => x.DebitAmount));
                Assert.Equal(20m, lines.Sum(x => x.CreditAmount));
                Assert.All(lines, x => Assert.Equal(studentId, x.StudentId));
                if (journal.SourceType == "StudentRefundCreditNote")
                {
                    Assert.Contains(lines, x => x.AccountId == incomeId && x.DebitAmount == 20m);
                    Assert.Contains(lines, x => x.AccountId == receivableId && x.CreditAmount == 20m);
                }
                else
                {
                    Assert.Contains(lines, x => x.AccountId == receivableId && x.DebitAmount == 20m);
                    Assert.Contains(lines, x => x.AccountId == cashId && x.CreditAmount == 20m);
                }
            }

            // The SAME enrollment is the immutable result snapshot's owner and
            // the billed student's owner: no loosely matched names/phone numbers.
            var lineage = await (from enrollment in db.StudentEnrollments
                join result in db.StudentResultSummaries on enrollment.Id equals result.StudentEnrollmentId
                join invoice in db.StudentInvoices on enrollment.Id equals invoice.StudentEnrollmentId
                where enrollment.Id == enrollmentId && result.Id == resultSummaryId && invoice.Id == invoiceId
                select new { enrollment.StudentId, enrollment.CampusId, result.IsPassed, invoice.State }).SingleAsync();
            Assert.Equal(studentId, lineage.StudentId);
            Assert.Equal(campusId, lineage.CampusId);
            Assert.True(lineage.IsPassed);
            Assert.Equal(InvoiceState.Paid, lineage.State);

            var reconciliation = await new FeeReceivablesAgingService(db, new Actor(tenantId, payerId))
                .GetReconciliationAsync(campusId, null, 1, 25);
            Assert.True(reconciliation.Success, reconciliation.Message);
            var reconciled = Assert.Single(reconciliation.Data!.Rows);
            Assert.Equal(20m, reconciled.CreditNoteAmount);
            Assert.Equal(100m, reconciled.NetAllocatedAmount);
            Assert.Equal("Reconciled", reconciled.Status);

            var foreign = new FinanceAdjustmentService(db, new Actor(tenantId + 100_000, payerId), TimeProvider.System);
            var denied = await foreign.RecordRefundDisbursementAsync(refundId, request);
            Assert.False(denied.Success);
            Assert.Contains(denied.StatusCode, new[] { 403, 404 });
        }

        await using (var immutable = new EduOSDbContext(options, Accessor(tenantId)))
        {
            var paid = await immutable.StudentInvoices.SingleAsync(x => x.Id == invoiceId);
            paid.PaidAmount = 80m;
            paid.DueAmount = 20m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => immutable.SaveChangesAsync());
        }
        await using (var immutable = new EduOSDbContext(options, Accessor(tenantId)))
        {
            var result = await immutable.StudentResultSummaries.SingleAsync(x => x.Id == resultSummaryId);
            result.ObtainedMarks = 99m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => immutable.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Posted_payroll_settlement_and_independent_return_reconcile_against_real_sql_journals()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        long tenantId, makerId, reviewerId, paymentId, originalJournalId, periodId;
        Guid paymentReference;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "V1PAY-" + suffix, Name = "V1 Payroll SQL CI",
                Email = "v1pay-" + suffix + "@example.test", State = TenantState.Active };
            var person = new Person { FullName = "Payroll SQL Employee" };
            setup.AddRange(tenant, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            var maker = User("salary-maker", suffix);
            var reviewer = User("salary-reviewer", suffix);
            setup.ApplicationUsers.AddRange(maker, reviewer);
            await setup.SaveChangesAsync();
            makerId = maker.Id;
            reviewerId = reviewer.Id;

            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "MAIN", Name = "Main" };
                var designation = new Designation { TenantId = tenantId, Code = "TEACH", Name = "Teacher" };
                var payable = new Account { TenantId = tenantId, Code = "SALARY-PAYABLE",
                    Name = "Salary payable", AccountType = AccountType.Liability };
                var cash = new Account { TenantId = tenantId, Code = "CASH-HAND",
                    Name = "Cash in hand", AccountType = AccountType.Asset };
                var expense = new Account { TenantId = tenantId, Code = "SALARY-EXP",
                    Name = "Salary expense", AccountType = AccountType.Expense };
                var period = new AccountingPeriod { TenantId = tenantId, Name = "Payroll return CI",
                    StartDate = date.AddDays(-5), EndDate = date.AddDays(5) };
                setup.AddRange(campus, designation, payable, cash, expense, period);
                await setup.SaveChangesAsync();
                periodId = period.Id;

                var employee = new Employee { TenantId = tenantId, PersonId = person.Id,
                    DesignationId = designation.Id, EmployeeCode = "V1EMP-" + suffix,
                    FullName = person.FullName, JoiningDate = date.AddMonths(-1) };
                setup.Employees.Add(employee);
                await setup.SaveChangesAsync();
                var run = new PayrollRun { TenantId = tenantId, RunNumber = "V1RUN-" + suffix,
                    ClientRequestId = Guid.NewGuid(), CampusId = campus.Id, Year = date.Year,
                    Month = date.Month, PeriodStart = new DateOnly(date.Year, date.Month, 1),
                    PeriodEnd = date, State = PayrollRunState.Draft };
                setup.PayrollRuns.Add(run);
                await setup.SaveChangesAsync();
                var payrollEmployee = new PayrollEmployee { TenantId = tenantId,
                    PayrollRunId = run.Id, EmployeeId = employee.Id,
                    GrossAmount = 100m, NetAmount = 100m };
                setup.PayrollEmployees.Add(payrollEmployee);
                await setup.SaveChangesAsync();
                var payment = new PayrollPayment { TenantId = tenantId,
                    ClientRequestId = Guid.NewGuid(), PayrollEmployeeId = payrollEmployee.Id,
                    PaymentDate = date, Amount = 40m, PaymentMethod = PaymentMethodType.Cash,
                    State = PaymentState.Initiated };
                setup.PayrollPayments.Add(payment);
                await setup.SaveChangesAsync();
                paymentReference = payment.PublicId;
                paymentId = payment.Id;

                var accrual = PostedJournal(tenantId, periodId, campus.Id, run.Id,
                    "PayrollRun", "V1-ACC-" + suffix, date, reviewer.Id, maker.Id);
                var settlement = PostedJournal(tenantId, periodId, campus.Id, payment.Id,
                    "PayrollPayment", "V1-SET-" + suffix, date, reviewer.Id, maker.Id);
                setup.Journals.AddRange(accrual, settlement);
                await setup.SaveChangesAsync();
                originalJournalId = settlement.Id;
                setup.JournalLines.AddRange(
                    PayrollLine(tenantId, accrual.Id, 1, expense.Id, campus.Id, employee.Id,
                        100m, 0m, "Salary expense"),
                    PayrollLine(tenantId, accrual.Id, 2, payable.Id, campus.Id, employee.Id,
                        0m, 100m, "Payroll net payable"),
                    PayrollLine(tenantId, settlement.Id, 1, payable.Id, campus.Id, employee.Id,
                        40m, 0m, "Salary payable settlement"),
                    PayrollLine(tenantId, settlement.Id, 2, cash.Id, campus.Id, employee.Id,
                        0m, 40m, "Cash/bank payroll disbursement"));
                run.State = PayrollRunState.Posted;
                run.JournalId = accrual.Id;
                run.ApprovedByUserId = reviewer.Id;
                run.ApprovedAt = DateTime.UtcNow;
                run.PostedAt = DateTime.UtcNow;
                payment.State = PaymentState.Successful;
                payment.JournalId = settlement.Id;
                await setup.SaveChangesAsync();
            }
        }

        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var originalPayment = await db.PayrollPayments.AsNoTracking().SingleAsync(x => x.Id == paymentId);
        Assert.NotEmpty(originalPayment.RowVersion);
        var request = new RequestPayrollPaymentReturnDto { ClientRequestId = Guid.NewGuid(),
            AccountingPeriodId = periodId, ReturnDate = date,
            EvidenceReference = "RETURN-V1-" + suffix,
            Reason = "Verified overpayment returned to institution",
            RowVersion = Convert.ToBase64String(originalPayment.RowVersion) };
        var makerService = new PayrollPaymentPostingService(db, new Actor(tenantId, makerId), TimeProvider.System);
        var proposed = await makerService.RequestReturnAsync(paymentReference, request);
        Assert.True(proposed.Success, proposed.Message);
        Assert.Equal(JournalState.Submitted, proposed.Data!.State);
        var decision = new ReviewPayrollPaymentReturnDto { Approve = true,
            RowVersion = proposed.Data.RowVersion };
        Assert.Equal(403, (await makerService.ReviewReturnAsync(paymentReference,
            proposed.Data.ReturnReference, decision)).StatusCode);

        var checker = new PayrollPaymentPostingService(db, new Actor(tenantId, reviewerId), TimeProvider.System);
        var approved = await checker.ReviewReturnAsync(paymentReference, proposed.Data.ReturnReference, decision);
        Assert.True(approved.Success, approved.Message);
        Assert.Equal(JournalState.Posted, approved.Data!.State);
        var replay = await checker.ReviewReturnAsync(paymentReference, proposed.Data.ReturnReference, decision);
        Assert.True(replay.Success, replay.Message);

        var contra = await db.Journals.AsNoTracking().SingleAsync(x => x.TenantId == tenantId &&
            x.SourceType == "PayrollPaymentReturn" && x.SourceId == paymentId);
        Assert.Equal(originalJournalId, contra.ReversalOfJournalId);
        Assert.Equal(makerId, contra.CreatedBy);
        Assert.Equal(reviewerId, contra.ApprovedByUserId);
        var originalLines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == originalJournalId)
            .OrderBy(x => x.LineNo).ToListAsync();
        var contraLines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == contra.Id)
            .OrderBy(x => x.LineNo).ToListAsync();
        Assert.Equal(2, originalLines.Count);
        Assert.Equal(2, contraLines.Count);
        for (var i = 0; i < originalLines.Count; i++)
        {
            Assert.Equal(originalLines[i].DebitAmount, contraLines[i].CreditAmount);
            Assert.Equal(originalLines[i].CreditAmount, contraLines[i].DebitAmount);
        }
        Assert.Equal(40m, contraLines.Sum(x => x.DebitAmount));
        Assert.Equal(40m, contraLines.Sum(x => x.CreditAmount));
        Assert.Equal(PaymentState.Successful,
            (await db.PayrollPayments.AsNoTracking().SingleAsync(x => x.Id == paymentId)).State);
        var register = await checker.GetRegisterAsync(false, 1, 10);
        Assert.True(register.Success, register.Message);
        var returned = Assert.Single(register.Data!.Payments);
        Assert.Equal("Returned", returned.ReconciliationStatus);
        Assert.Equal(0m, returned.SettledAmount);
        // Return restores the full net-payable capacity: the unpaid 60 plus
        // the independently returned 40, without modifying the paid receipt.
        Assert.Equal(100m, returned.RemainingAmount);

        var otherTenant = new PayrollPaymentPostingService(db,
            new Actor(tenantId + 100_000, makerId), TimeProvider.System);
        var denied = await otherTenant.RequestReturnAsync(paymentReference, request);
        Assert.False(denied.Success);
    }

    private static JournalLine PayrollLine(long tenantId, long journalId, int lineNo,
        long accountId, long campusId, long employeeId, decimal debit, decimal credit,
        string description) => new() { TenantId = tenantId, JournalId = journalId,
            LineNo = lineNo, AccountId = accountId, CampusId = campusId, EmployeeId = employeeId,
            DebitAmount = debit, CreditAmount = credit, Description = description };

    private static Journal PostedJournal(long tenantId, long periodId, long campusId,
        long sourceId, string source, string number, DateOnly date, long approverId, long payerId) =>
        new() { TenantId = tenantId, JournalNumber = number, JournalDate = date,
            AccountingPeriodId = periodId, CampusId = campusId, SourceType = source, SourceId = sourceId,
            State = JournalState.Posted, ApprovedByUserId = approverId, ApprovedAt = DateTime.UtcNow,
            PostedByUserId = payerId, PostedAt = DateTime.UtcNow };

    private static JournalLine Line(long tenantId, long journalId, int lineNo, long accountId,
        long campusId, long studentId, decimal debit, decimal credit, long? feeId = null,
        long? programId = null) => new() { TenantId = tenantId, JournalId = journalId,
            LineNo = lineNo, AccountId = accountId, CampusId = campusId, StudentId = studentId,
            DebitAmount = debit, CreditAmount = credit, FeeHeadId = feeId, AcademicProgramId = programId };

    private static ApplicationUser User(string role, string suffix)
    {
        var username = "v1fi-" + role + "-" + suffix;
        return new ApplicationUser { UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = username + "@example.test",
            NormalizedEmail = (username + "@example.test").ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"), FullName = "V1 CI " + role };
    }

    private static IHttpContextAccessor Accessor(long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return accessor;
    }

    private sealed class Actor(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
