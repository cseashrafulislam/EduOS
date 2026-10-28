using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.HR;

/// <summary>Settles verified institutional payroll requests against posted salary payable, not platform billing.</summary>
public sealed class PayrollPaymentPostingService : IPayrollPaymentPostingService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly IDataProtectionProvider? dataProtection;
    public PayrollPaymentPostingService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    public PayrollPaymentPostingService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
        IDataProtectionProvider dataProtection) : this(db, user, clock)
    { this.dataProtection = dataProtection; }

    private bool CanRead => user.IsAuthenticated && user.TenantId > 0 && db.CurrentTenantId == user.TenantId &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool CanPost => user.IsAuthenticated && user.TenantId > 0 && db.CurrentTenantId == user.TenantId && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static bool Money(decimal amount) => amount >= 0m && amount <= 9999999999999999.99m &&
        decimal.Round(amount, 2) == amount;
    private static bool Version(byte[] actual, string supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual != null &&
            actual.Length > 0 && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static Guid PostingKey(long tenant, Guid paymentReference)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            "EduOS:Institution:PayrollPayment:" + tenant + ":" + paymentReference.ToString("N")));
        return new Guid(bytes.AsSpan(0, 16));
    }
    private static bool CashLedger(Account account) =>
        account.Code.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
        account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase);

    public async Task<ApiResponse<PayrollPaymentPostingRegisterDto>> GetRegisterAsync(
        bool pendingOnly, int page, int pageSize, CancellationToken ct = default,
        Guid? runReference = null)
    {
        if (!CanRead) return Error<PayrollPaymentPostingRegisterDto>("Payroll settlement access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 10 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PayrollPaymentPostingRegisterDto>("Invalid payroll payment pagination.");
        if (runReference == Guid.Empty)
            return Error<PayrollPaymentPostingRegisterDto>("Invalid payroll run reference.");
        var tenant = user.TenantId;
        var query = from payment in db.PayrollPayments.AsNoTracking()
                    join employee in db.PayrollEmployees.AsNoTracking()
                        on new { payment.TenantId, Id = payment.PayrollEmployeeId }
                        equals new { employee.TenantId, employee.Id }
                    join run in db.PayrollRuns.AsNoTracking()
                        on new { employee.TenantId, Id = employee.PayrollRunId }
                        equals new { run.TenantId, run.Id }
                    where payment.TenantId == tenant && !payment.IsDeleted && !employee.IsDeleted &&
                        !run.IsDeleted && run.State == PayrollRunState.Posted && run.JournalId.HasValue &&
                        (pendingOnly ? payment.State == PaymentState.AwaitingVerification :
                            (payment.State == PaymentState.AwaitingVerification || payment.State == PaymentState.Successful))
                    select new { Payment = payment, Employee = employee, Run = run };
        if (runReference.HasValue)
            query = query.Where(x => x.Run.PublicId == runReference.Value);
        if (!user.IsTenantAdmin)
            query = query.Where(x => x.Run.CampusId.HasValue && db.UserCampusAccesses.Any(a =>
                a.TenantId == tenant && a.UserId == user.UserId && a.CampusId == x.Run.CampusId &&
                a.IsActive && !a.IsDeleted));
        var total = await query.CountAsync(ct);
        var records = await query.OrderByDescending(x => x.Payment.PaymentDate)
            .ThenByDescending(x => x.Payment.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var bankIds = records.Where(x => x.Payment.BankAccountId.HasValue)
            .Select(x => x.Payment.BankAccountId!.Value).Distinct().ToArray();
        var banks = await db.BankAccounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            bankIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        var journalIds = records.Where(x => x.Payment.JournalId.HasValue)
            .Select(x => x.Payment.JournalId!.Value).Distinct().ToArray();
        var journals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
            journalIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);

        // Reconcile the complete successful settlement history for employees on this page.
        // Deleted historical postings are deliberately included: they cannot silently free payable.
        var employeeIds = records.Select(x => x.Employee.Id).Distinct().ToArray();
        var successfulHistory = await db.PayrollPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && employeeIds.Contains(x.PayrollEmployeeId) &&
                x.State == PaymentState.Successful)
            .Take(501).ToListAsync(ct);
        if (successfulHistory.Count > 500)
            return Error<PayrollPaymentPostingRegisterDto>("Payroll settlement history exceeds the safe reconciliation limit.", 409);
        var settlementJournalIds = successfulHistory.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).Distinct().ToArray();
        var relatedJournalIds = settlementJournalIds.Concat(records
            .Where(x => x.Run.JournalId.HasValue).Select(x => x.Run.JournalId!.Value))
            .Distinct().ToArray();
        var relatedJournals = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && relatedJournalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var relatedLines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && relatedJournalIds.Contains(x.JournalId) &&
                (settlementJournalIds.Contains(x.JournalId) || x.IsDeleted ||
                    x.Description == "Payroll net payable"))
            .Take(3001).ToListAsync(ct);
        if (relatedLines.Count > 3000)
            return Error<PayrollPaymentPostingRegisterDto>("Payroll journal history exceeds the safe reconciliation limit.", 409);
        var accrualIds = records.Where(x => x.Run.JournalId.HasValue)
            .Select(x => x.Run.JournalId!.Value).Distinct().ToArray();
        var accrualTotals = await db.JournalLines.AsNoTracking()
            .Where(x => x.TenantId == tenant && accrualIds.Contains(x.JournalId) && !x.IsDeleted)
            .GroupBy(x => x.JournalId)
            .Select(g => new { JournalId = g.Key, Debit = g.Sum(l => l.DebitAmount),
                Credit = g.Sum(l => l.CreditAmount) })
            .ToDictionaryAsync(x => x.JournalId, ct);
        var runIds = records.Select(x => x.Run.Id).Distinct().ToArray();
        var netByRun = await db.PayrollEmployees.AsNoTracking()
            .Where(x => x.TenantId == tenant && runIds.Contains(x.PayrollRunId) && !x.IsDeleted)
            .GroupBy(x => x.PayrollRunId)
            .Select(g => new { RunId = g.Key, Net = g.Sum(e => e.NetAmount) })
            .ToDictionaryAsync(x => x.RunId, x => x.Net, ct);
        var reversalIds = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ReversalOfJournalId.HasValue &&
                relatedJournalIds.Contains(x.ReversalOfJournalId.Value) &&
                (x.State == JournalState.Posted || x.State == JournalState.Reversed))
            .Select(x => x.ReversalOfJournalId!.Value).Take(501).ToListAsync(ct);
        if (reversalIds.Count > 500)
            return Error<PayrollPaymentPostingRegisterDto>("Payroll reversal history exceeds the safe reconciliation limit.", 409);
        var reversed = reversalIds.ToHashSet();
        var historyByEmployee = successfulHistory.ToLookup(x => x.PayrollEmployeeId);
        var linesByJournal = relatedLines.ToLookup(x => x.JournalId);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.StartDate).Take(101)
            .Select(x => new PayrollPostingPeriodDto { Id = x.Id, Name = x.Name,
                StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync(ct);
        if (periods.Count > 100) return Error<PayrollPaymentPostingRegisterDto>("Too many open accounting periods.", 409);
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT" && x.AccountType == AccountType.Asset)
            .OrderBy(x => x.Code).Take(301).ToListAsync(ct);
        if (accounts.Count > 300) return Error<PayrollPaymentPostingRegisterDto>("Too many asset accounts for the selector.", 409);
        return ApiResponse<PayrollPaymentPostingRegisterDto>.SuccessResponse(new PayrollPaymentPostingRegisterDto
        {
            Page = page, PageSize = pageSize, TotalCount = total, OpenPeriods = periods,
            Accounts = accounts.Select(x => new PayrollPostingAccountDto
                { Id = x.Id, Code = x.Code, Name = x.Name, AccountType = x.AccountType.ToString() }).ToArray(),
            Payments = records.Select(x =>
            {
                var history = historyByEmployee[x.Employee.Id].ToArray();
                var settled = history.Sum(p => p.Amount);
                var remaining = x.Employee.NetAmount - settled;
                string? issue = null;
                var accrual = x.Run.JournalId.HasValue
                    ? relatedJournals.GetValueOrDefault(x.Run.JournalId.Value) : null;
                var payableLines = accrual == null ? Array.Empty<JournalLine>() :
                    linesByJournal[accrual.Id].Where(l => !l.IsDeleted &&
                        l.Description == "Payroll net payable" &&
                        l.CreditAmount > 0m && l.DebitAmount == 0m).ToArray();
                var accrualBalance = accrual == null ? null : accrualTotals.GetValueOrDefault(accrual.Id);
                if (!Money(x.Employee.NetAmount) || x.Employee.NetAmount <= 0m)
                    issue = "Employee net salary snapshot is invalid.";
                else if (accrual == null || accrual.IsDeleted ||
                    accrual.SourceType != "PayrollRun" || accrual.SourceId != x.Run.Id ||
                    accrual.State != JournalState.Posted || accrual.ReversalOfJournalId.HasValue ||
                    accrual.CampusId != x.Run.CampusId || accrual.CurrencyCode != "BDT" ||
                    reversed.Contains(accrual.Id) ||
                    linesByJournal[accrual.Id].Any(l => l.IsDeleted) || payableLines.Length != 1 ||
                    accrualBalance == null || accrualBalance.Debit <= 0m ||
                    accrualBalance.Debit != accrualBalance.Credit ||
                    !netByRun.ContainsKey(x.Run.Id) ||
                    payableLines[0].CreditAmount != netByRun[x.Run.Id])
                    issue = "Payroll accrual or salary payable provenance needs reconciliation.";
                else if (history.Any(p => p.IsDeleted || !Money(p.Amount) || p.Amount <= 0m))
                    issue = "Deleted or invalid successful payment history requires reconciliation.";
                else if (settled > x.Employee.NetAmount)
                    issue = "Successful settlements exceed the employee net salary.";
                else
                {
                    foreach (var paid in history)
                    {
                        var posted = paid.JournalId.HasValue
                            ? relatedJournals.GetValueOrDefault(paid.JournalId.Value) : null;
                        var entries = posted == null ? Array.Empty<JournalLine>() :
                            linesByJournal[posted.Id].ToArray();
                        if (posted == null || posted.IsDeleted || posted.SourceType != "PayrollPayment" ||
                            posted.SourceId != paid.Id || posted.State != JournalState.Posted ||
                            posted.ReversalOfJournalId.HasValue || reversed.Contains(posted.Id) ||
                            posted.CampusId != x.Run.CampusId || posted.CurrencyCode != "BDT" ||
                            posted.JournalDate != paid.PaymentDate || paid.CurrencyCode != "BDT" ||
                            entries.Length != 2 || entries.Any(l => l.IsDeleted) ||
                            entries.Count(l => l.Description == "Salary payable settlement" &&
                                l.AccountId == payableLines[0].AccountId &&
                                l.EmployeeId == x.Employee.EmployeeId && l.CampusId == x.Run.CampusId &&
                                l.DebitAmount == paid.Amount && l.CreditAmount == 0m) != 1 ||
                            entries.Count(l => l.Description == "Cash/bank payroll disbursement" &&
                                l.EmployeeId == x.Employee.EmployeeId && l.CampusId == x.Run.CampusId &&
                                l.AccountId != payableLines[0].AccountId &&
                                l.CreditAmount == paid.Amount && l.DebitAmount == 0m) != 1)
                        {
                            issue = "A successful payment has missing, reversed or inconsistent GL evidence.";
                            break;
                        }
                    }
                }
                if (issue == null && x.Payment.State == PaymentState.Successful &&
                    !history.Any(p => p.Id == x.Payment.Id))
                    issue = "Successful payment is absent from its settlement history.";
                if (issue == null && x.Payment.State == PaymentState.AwaitingVerification &&
                    (x.Payment.JournalId.HasValue || !Money(x.Payment.Amount) ||
                        x.Payment.Amount <= 0m || x.Payment.Amount > remaining))
                    issue = "Pending payment exceeds remaining salary or has invalid posting state.";
                return new PayrollPaymentPostingRowDto
                {
                    Reference = x.Payment.PublicId, RunReference = x.Run.PublicId,
                    RunNumber = x.Run.RunNumber,
                    EmployeeName = x.Employee.EmployeeNameSnapshot ?? x.Employee.EmployeeCodeSnapshot ?? "Employee",
                    CampusId = x.Run.CampusId, PaymentDate = x.Payment.PaymentDate,
                    PaymentMethod = x.Payment.PaymentMethod, State = x.Payment.State,
                    Amount = x.Payment.Amount, NetSalaryAmount = x.Employee.NetAmount,
                    SettledAmount = settled, RemainingAmount = remaining,
                    ReconciliationStatus = issue != null ? "RequiresReview" :
                        x.Payment.State == PaymentState.Successful ? "Reconciled" : "Pending",
                    ReconciliationIssue = issue,
                    BankAccountId = x.Payment.BankAccountId,
                    BankLedgerId = x.Payment.BankAccountId.HasValue &&
                        banks.TryGetValue(x.Payment.BankAccountId.Value, out var bank) ? bank.AccountId : null,
                    JournalReference = x.Payment.JournalId.HasValue &&
                        journals.TryGetValue(x.Payment.JournalId.Value, out var journal) ? journal.PublicId : null,
                    RowVersion = Convert.ToBase64String(x.Payment.RowVersion)
                };
            }).ToArray()
        });
    }

    /// <summary>
    /// A read-only, bank-neutral review worksheet. It is deliberately NOT a bank instruction,
    /// an acknowledgement of a transfer, or a substitute for independently verifying the GL.
    /// Decrypted account numbers are present only in the transient response body, never in logs.
    /// </summary>
    public async Task<ApiResponse<PayrollBankReviewExportDto>> GetBankReviewCsvAsync(
        Guid runReference, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PayrollBankReviewExportDto>("Tenant administrator required.", 403);
        if (runReference == Guid.Empty) return Error<PayrollBankReviewExportDto>("Payroll run reference required.");
        if (dataProtection == null) return Error<PayrollBankReviewExportDto>("Bank export security is unavailable.", 503);
        var tenant = user.TenantId;
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == runReference && !x.IsDeleted &&
            x.State == PayrollRunState.Posted && x.JournalId.HasValue, ct);
        if (run == null) return Error<PayrollBankReviewExportDto>("Posted payroll run not found.", 404);

        // Collect only the complete, reconciled pending register; never silently export a
        // truncated selection or any record that has failed the salary-payable checks.
        var payments = new List<PayrollPaymentPostingRowDto>();
        for (var page = 1; ; page++)
        {
            var register = await GetRegisterAsync(true, page, 10, ct, runReference);
            if (!register.Success || register.Data == null)
                return Error<PayrollBankReviewExportDto>(register.Message, register.StatusCode);
            if (register.Data.TotalCount > 1000)
                return Error<PayrollBankReviewExportDto>("Run exceeds the 1,000-payment review export limit.", 409);
            payments.AddRange(register.Data.Payments);
            if (payments.Count >= register.Data.TotalCount) break;
        }
        var bankRows = payments.Where(x => x.PaymentMethod == PaymentMethodType.BankTransfer).ToArray();
        if (bankRows.Length == 0)
            return Error<PayrollBankReviewExportDto>("No pending bank transfer requests for this run.", 404);
        if (bankRows.Any(x => x.ReconciliationStatus != "Pending" || x.RunReference != runReference ||
            !Money(x.Amount) || x.Amount <= 0m))
            return Error<PayrollBankReviewExportDto>("Bank payment review requires fully reconciled pending requests.", 409);

        var paymentIds = bankRows.Select(x => x.Reference).ToArray();
        var details = await (from payment in db.PayrollPayments.AsNoTracking()
            join salary in db.PayrollEmployees.AsNoTracking()
                on new { payment.TenantId, Id = payment.PayrollEmployeeId }
                equals new { salary.TenantId, salary.Id }
            join employee in db.Employees.AsNoTracking()
                on new { salary.TenantId, Id = salary.EmployeeId }
                equals new { employee.TenantId, employee.Id }
            join target in db.EmployeeBankAccounts.AsNoTracking()
                on new { payment.TenantId, Id = payment.EmployeeBankAccountId }
                equals new { target.TenantId, Id = (long?)target.Id }
            join source in db.BankAccounts.AsNoTracking()
                on new { payment.TenantId, Id = payment.BankAccountId }
                equals new { source.TenantId, Id = (long?)source.Id }
            join sourceLedger in db.Accounts.AsNoTracking()
                on new { source.TenantId, Id = source.AccountId }
                equals new { sourceLedger.TenantId, sourceLedger.Id }
            where payment.TenantId == tenant && paymentIds.Contains(payment.PublicId) &&
                !payment.IsDeleted && !salary.IsDeleted && !employee.IsDeleted &&
                !target.IsDeleted && target.IsActive && target.EmployeeId == employee.Id &&
                target.CurrencyCode == "BDT" && !source.IsDeleted && source.IsActive &&
                source.CurrencyCode == "BDT" && (!source.CampusId.HasValue || source.CampusId == run.CampusId) &&
                !sourceLedger.IsDeleted && sourceLedger.IsActive && sourceLedger.AllowsPosting &&
                !sourceLedger.IsGroup && sourceLedger.AccountType == AccountType.Asset &&
                sourceLedger.CurrencyCode == "BDT" &&
                payment.State == PaymentState.AwaitingVerification && !payment.JournalId.HasValue &&
                payment.CurrencyCode == "BDT" && salary.PayrollRunId == run.Id
            select new { Payment = payment, Employee = employee, Target = target, Source = source })
            .ToListAsync(ct);
        if (details.Count != bankRows.Length || details.Select(x => x.Payment.PublicId).Distinct().Count() != bankRows.Length)
            return Error<PayrollBankReviewExportDto>("Bank routing or employee account provenance changed; reload.", 409);
        var rowsById = bankRows.ToDictionary(x => x.Reference);
        var protector = dataProtection.CreateProtector("EduOS.HR.EmployeeBankAccount.v1", tenant.ToString(CultureInfo.InvariantCulture));
        var csv = new StringBuilder("ReviewStatus,PaymentReference,RunNumber,EmployeeCode,EmployeeName,PaymentDate,AmountBDT,SourceBank,DestinationBank,DestinationAccountName,DestinationAccountNumber,RoutingNumber\r\n");
        foreach (var row in details.OrderBy(x => x.Employee.EmployeeCode).ThenBy(x => x.Payment.PublicId))
        {
            if (!rowsById.TryGetValue(row.Payment.PublicId, out var register) ||
                row.Payment.Amount != register.Amount ||
                row.Payment.BankAccountId != register.BankAccountId ||
                string.IsNullOrWhiteSpace(row.Payment.ExternalReference) ||
                row.Target.ProtectedAccountNumber.Length == 0)
                return Error<PayrollBankReviewExportDto>("Payment details changed or lack an external reference; reload.", 409);
            string number;
            try { number = protector.Unprotect(row.Target.ProtectedAccountNumber); }
            catch (CryptographicException)
            { return Error<PayrollBankReviewExportDto>("Employee bank account cannot be decrypted; contact administration.", 409); }
            if (string.IsNullOrWhiteSpace(number) || number.Length > 200 ||
                !number.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '/') ||
                !number.EndsWith(row.Target.AccountNumberLast4 ?? "", StringComparison.Ordinal))
                return Error<PayrollBankReviewExportDto>("Employee bank account needs verification.", 409);
            csv.AppendJoin(',', new[] {
                Csv("REVIEW_ONLY"), Csv(row.Payment.PublicId.ToString("D")), Csv(run.RunNumber),
                Csv(row.Employee.EmployeeCode), Csv(row.Employee.FullName),
                Csv(row.Payment.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                row.Payment.Amount.ToString("F2", CultureInfo.InvariantCulture),
                Csv(row.Source.BankName), Csv(row.Target.BankName), Csv(row.Target.AccountName),
                Csv(number), Csv(row.Target.RoutingNumber ?? "")
            }).Append("\r\n");
        }
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant, UserId = user.UserId, Action = "Payroll.BankReviewExported",
            EntityName = "PayrollRun", EntityId = run.Id, CorrelationId = runReference.ToString("D"),
            NewValue = $"PendingBankPayments={bankRows.Length}", IsSuccess = true,
            OccurredAt = clock.GetUtcNow().UtcDateTime
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Error<PayrollBankReviewExportDto>("Bank export audit unavailable.", 503); }
        return ApiResponse<PayrollBankReviewExportDto>.SuccessResponse(new PayrollBankReviewExportDto
        {
            FileName = "payroll-bank-review-" + runReference.ToString("N") + ".csv",
            Csv = csv.ToString(), PaymentCount = bankRows.Length
        });
    }

    private static string Csv(string value)
    {
        // RFC 4180 escaping plus neutralisation for spreadsheet formula injection.
        if (value.Length > 0 && ("=+-@\t\r\n".Contains(value[0]) || char.IsWhiteSpace(value[0])))
            value = "'" + value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    public async Task<ApiResponse<PostPayrollPaymentJournalResultDto>> PostAsync(
        Guid paymentReference, PostPayrollPaymentJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PostPayrollPaymentJournalResultDto>("Tenant administrator permission required.", 403);
        if (paymentReference == Guid.Empty || request == null || request.AccountingPeriodId <= 0 ||
            request.SettlementAccountId <= 0 || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 256)
            return Error<PostPayrollPaymentJournalResultDto>("Payment, open period, settlement account and row version required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var payment = await db.PayrollPayments.SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == paymentReference && !x.IsDeleted, ct);
            if (payment == null) return Error<PostPayrollPaymentJournalResultDto>("Payroll payment not found.", 404);
            if (payment.JournalId.HasValue)
            {
                var existing = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == payment.JournalId && !x.IsDeleted, ct);
                if (payment.State != PaymentState.Successful || existing == null ||
                    existing.SourceType != "PayrollPayment" || existing.SourceId != payment.Id ||
                    existing.State != JournalState.Posted || existing.ReversalOfJournalId.HasValue ||
                    existing.AccountingPeriodId != request.AccountingPeriodId ||
                    existing.CurrencyCode != "BDT" ||
                    await PayrollPostingHistoryIntegrity.HasPostedReversalAsync(db, tenant, existing.Id, ct) ||
                    await PayrollPostingHistoryIntegrity.HasDeletedLinesAsync(db, tenant, existing.Id, ct))
                    return Error<PostPayrollPaymentJournalResultDto>("Linked payroll payment journal requires reconciliation.", 409);
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.JournalId == existing.Id && !x.IsDeleted).ToListAsync(ct);
                if (lines.Count != 2 || lines.Count(x => x.CreditAmount == payment.Amount &&
                    x.DebitAmount == 0m && x.AccountId == request.SettlementAccountId) != 1 ||
                    lines.Count(x => x.DebitAmount == payment.Amount && x.CreditAmount == 0m) != 1 ||
                    lines.Any(x => x.DebitAmount < 0m || x.CreditAmount < 0m ||
                        (x.DebitAmount > 0m && x.CreditAmount > 0m)))
                    return Error<PostPayrollPaymentJournalResultDto>("Retry uses different settlement account or journal is unbalanced.", 409);
                return ApiResponse<PostPayrollPaymentJournalResultDto>.SuccessResponse(new PostPayrollPaymentJournalResultDto
                { PaymentReference = payment.PublicId, JournalReference = existing.PublicId,
                    JournalNumber = existing.JournalNumber, Amount = payment.Amount, AlreadyPosted = true });
            }
            if (!Version(payment.RowVersion, request.RowVersion))
                return Error<PostPayrollPaymentJournalResultDto>("Payment changed; reload and retry.", 409);
            if (payment.State != PaymentState.AwaitingVerification || payment.CurrencyCode != "BDT" ||
                !Money(payment.Amount) || payment.Amount <= 0m ||
                payment.PaymentMethod is not (PaymentMethodType.Cash or PaymentMethodType.BankTransfer))
                return Error<PostPayrollPaymentJournalResultDto>("Only pending BDT cash or bank-transfer payroll requests can be verified.", 409);
            if (payment.CreatedBy == user.UserId)
                return Error<PostPayrollPaymentJournalResultDto>("Payment maker cannot verify their own settlement.", 409);
            var employee = await db.PayrollEmployees.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == payment.PayrollEmployeeId && !x.IsDeleted, ct);
            if (employee == null || !Money(employee.NetAmount) || payment.Amount > employee.NetAmount)
                return Error<PostPayrollPaymentJournalResultDto>("Payroll employee or net salary is invalid.", 409);
            var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == employee.PayrollRunId && !x.IsDeleted, ct);
            if (run == null || run.State != PayrollRunState.Posted || !run.JournalId.HasValue ||
                payment.PaymentDate < run.PeriodEnd)
                return Error<PostPayrollPaymentJournalResultDto>("Payroll accrual must be posted before settlement.", 409);
            if (run.CampusId.HasValue)
            {
                if (!await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.Id == run.CampusId && x.IsActive && !x.IsDeleted, ct) ||
                    !await db.EmployeeCampusAssignments.AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.EmployeeId == employee.EmployeeId &&
                        x.CampusId == run.CampusId && x.EffectiveFrom <= run.PeriodEnd &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo >= run.PeriodEnd) && !x.IsDeleted, ct))
                    return Error<PostPayrollPaymentJournalResultDto>("Employee is not assigned to the payroll campus.", 409);
            }
            var accrual = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == run.JournalId && !x.IsDeleted, ct);
            if (accrual == null || accrual.SourceType != "PayrollRun" || accrual.SourceId != run.Id ||
                accrual.State != JournalState.Posted || accrual.ReversalOfJournalId.HasValue ||
                accrual.CampusId != run.CampusId || accrual.CurrencyCode != "BDT" ||
                await PayrollPostingHistoryIntegrity.HasPostedReversalAsync(db, tenant, accrual.Id, ct) ||
                await PayrollPostingHistoryIntegrity.HasDeletedLinesAsync(db, tenant, accrual.Id, ct))
                return Error<PostPayrollPaymentJournalResultDto>("Payroll accrual is missing or reversed.", 409);
            var accrualLines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                x.JournalId == accrual.Id && !x.IsDeleted).ToListAsync(ct);
            var payableLines = accrualLines.Where(x => x.Description == "Payroll net payable" &&
                x.CreditAmount > 0m && x.DebitAmount == 0m).ToArray();
            var people = await db.PayrollEmployees.AsNoTracking().Where(x => x.TenantId == tenant &&
                x.PayrollRunId == run.Id && !x.IsDeleted).Take(5001).ToListAsync(ct);
            if (people.Count is < 1 or > 5000 || people.Any(x => !Money(x.NetAmount)) ||
                payableLines.Length != 1 || payableLines[0].CreditAmount != people.Sum(x => x.NetAmount) ||
                accrualLines.Sum(x => x.DebitAmount) != accrualLines.Sum(x => x.CreditAmount))
                return Error<PostPayrollPaymentJournalResultDto>("Payroll accrual payable does not reconcile.", 409);
            var payable = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == payableLines[0].AccountId && !x.IsDeleted &&
                x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT" &&
                x.AccountType == AccountType.Liability, ct);
            var settlement = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.SettlementAccountId && !x.IsDeleted &&
                x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT" &&
                x.AccountType == AccountType.Asset, ct);
            if (payable == null || settlement == null || payable.Id == settlement.Id)
                return Error<PostPayrollPaymentJournalResultDto>("Active BDT salary payable and asset settlement ledgers required.", 409);
            if (payment.PaymentMethod == PaymentMethodType.Cash)
            {
                if (payment.BankAccountId.HasValue || payment.EmployeeBankAccountId.HasValue ||
                    !CashLedger(settlement) || await db.BankAccounts.AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.AccountId == settlement.Id, ct))
                    return Error<PostPayrollPaymentJournalResultDto>("Cash settlement requires a cash-in-hand ledger, not a bank ledger.", 409);
            }
            else
            {
                if (!payment.BankAccountId.HasValue || !payment.EmployeeBankAccountId.HasValue ||
                    string.IsNullOrWhiteSpace(payment.ExternalReference))
                    return Error<PostPayrollPaymentJournalResultDto>("Bank transfer requires source bank, employee bank and external reference.", 409);
                var bank = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == payment.BankAccountId && x.IsActive &&
                    !x.IsDeleted && x.CurrencyCode == "BDT" && x.AccountId == settlement.Id &&
                    (!x.CampusId.HasValue || x.CampusId == run.CampusId), ct);
                if (bank == null || !await db.EmployeeBankAccounts.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == payment.EmployeeBankAccountId &&
                    x.EmployeeId == employee.EmployeeId && x.IsActive && !x.IsDeleted &&
                    x.CurrencyCode == "BDT", ct))
                    return Error<PostPayrollPaymentJournalResultDto>("Bank transfer account provenance is invalid.", 409);
            }
            var posted = await db.PayrollPayments.AsNoTracking().Where(x => x.TenantId == tenant &&
                x.PayrollEmployeeId == employee.Id && x.Id != payment.Id &&
                x.State == PaymentState.Successful).IgnoreQueryFilters().ToListAsync(ct);
            if (posted.Any(x => x.IsDeleted || !x.JournalId.HasValue) || posted.Sum(x => x.Amount) + payment.Amount > employee.NetAmount)
                return Error<PostPayrollPaymentJournalResultDto>("Settlement exceeds remaining net salary or prior payment needs reconciliation.", 409);
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                ((x.SourceType == "PayrollPayment" && x.SourceId == payment.Id) ||
                x.IdempotencyKey == PostingKey(tenant, payment.PublicId)), ct))
                return Error<PostPayrollPaymentJournalResultDto>("Unlinked payroll payment journal exists; reconcile first.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate <= payment.PaymentDate && x.EndDate >= payment.PaymentDate, ct);
            if (period == null) return Error<PostPayrollPaymentJournalResultDto>("Open accounting period must cover payment date.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var reference = Guid.NewGuid();
            var journal = new Journal
            {
                TenantId = tenant, PublicId = reference, IdempotencyKey = PostingKey(tenant, payment.PublicId),
                JournalNumber = "JRN-" + reference.ToString("N").ToUpperInvariant(),
                JournalDate = payment.PaymentDate, AccountingPeriodId = period.Id, CampusId = run.CampusId,
                CurrencyCode = "BDT", State = JournalState.Posted,
                SourceType = "PayrollPayment", SourceId = payment.Id,
                ReferenceNumber = payment.PublicId.ToString("N"),
                Description = "Institutional payroll payment " + run.RunNumber,
                ApprovedByUserId = user.UserId, ApprovedAt = now,
                PostedByUserId = user.UserId, PostedAt = now, CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(journal);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(
                new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 1,
                    AccountId = payable.Id, CampusId = run.CampusId, EmployeeId = employee.EmployeeId,
                    DebitAmount = payment.Amount, CreditAmount = 0m, Description = "Salary payable settlement",
                    CreatedAt = now, CreatedBy = user.UserId },
                new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 2,
                    AccountId = settlement.Id, CampusId = run.CampusId, EmployeeId = employee.EmployeeId,
                    DebitAmount = 0m, CreditAmount = payment.Amount, Description = "Cash/bank payroll disbursement",
                    CreatedAt = now, CreatedBy = user.UserId });
            payment.State = PaymentState.Successful; payment.JournalId = journal.Id;
            payment.UpdatedAt = now; payment.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PostPayrollPaymentJournalResultDto>.SuccessResponse(
                new PostPayrollPaymentJournalResultDto
                { PaymentReference = payment.PublicId, JournalReference = reference,
                    JournalNumber = journal.JournalNumber, Amount = payment.Amount },
                "Payroll payment verified and posted to institutional GL.");
        }
        catch (DbUpdateConcurrencyException)
        { return Error<PostPayrollPaymentJournalResultDto>("Payroll payment changed concurrently; reload.", 409); }
        catch (DbUpdateException)
        { return Error<PostPayrollPaymentJournalResultDto>("Payroll payment journal conflicts with another transaction.", 409); }
    }
}
