using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.HR;

/// <summary>Cash recovery of a disbursed institutional employee loan, reconciled with
/// immutable payroll recoveries and any posted payroll contra compensations.</summary>
public sealed class LoanRecoveryService : ILoanRecoveryService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public LoanRecoveryService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }

    private bool Allowed => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static bool Money(decimal value) => value > 0m && value <= 999999999999m &&
        decimal.Round(value, 2) == value;
    private static bool Version(byte[] actual, string supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual.Length > 0 &&
            CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
    private static Guid PostingKey(long tenant, Guid requestId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            "EduOS:Institution:LoanRecovery:" + tenant + ":" + requestId.ToString("N")));
        return new Guid(bytes.AsSpan(0, 16));
    }
    private static bool CashLedger(Account account) =>
        account.Code.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
        account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase);

    public async Task<ApiResponse<LoanRecoveryRegisterDto>> GetRegisterAsync(
        bool openOnly, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<LoanRecoveryRegisterDto>("Tenant administrator permission required.", 403);
        if (page < 1 || pageSize is < 1 or > 10 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<LoanRecoveryRegisterDto>("Invalid pagination.");
        var tenant = user.TenantId;
        var query = from loan in db.LoanAdvances.AsNoTracking()
                    join employee in db.Employees.AsNoTracking()
                    on new { loan.TenantId, Id = loan.EmployeeId }
                    equals new { employee.TenantId, employee.Id }
                    where loan.TenantId == tenant && !loan.IsDeleted && !employee.IsDeleted &&
                        (!openOnly || (!loan.IsClosed && loan.OutstandingAmount > 0m)) &&
                        db.Journals.AsNoTracking().Any(j => j.TenantId == tenant &&
                            j.SourceType == "LoanAdvance" && j.SourceId == loan.Id &&
                            j.State == JournalState.Posted && !j.IsDeleted)
                    select new { Loan = loan, Employee = employee };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Loan.IssueDate).ThenByDescending(x => x.Loan.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Loan.Id).ToArray();
        var journals = await db.Journals.AsNoTracking().Where(j => j.TenantId == tenant &&
            j.SourceType == "LoanAdvance" && j.SourceId.HasValue && ids.Contains(j.SourceId.Value) &&
            j.State == JournalState.Posted && !j.IsDeleted).ToListAsync(ct);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.StartDate).Take(101)
            .Select(x => new PayrollPostingPeriodDto
                { Id = x.Id, Name = x.Name, StartDate = x.StartDate, EndDate = x.EndDate })
            .ToListAsync(ct);
        if (periods.Count > 100)
            return Error<LoanRecoveryRegisterDto>("Too many open accounting periods.", 409);
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT" && x.AccountType == AccountType.Asset)
            .OrderBy(x => x.Code).Take(301).ToListAsync(ct);
        if (accounts.Count > 300)
            return Error<LoanRecoveryRegisterDto>("Too many asset accounts.", 409);
        var bankIds = await db.BankAccounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted).Select(x => x.AccountId).ToListAsync(ct);
        return ApiResponse<LoanRecoveryRegisterDto>.SuccessResponse(new LoanRecoveryRegisterDto
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            OpenPeriods = periods,
            CashAccounts = accounts.Where(x => CashLedger(x) && !bankIds.Contains(x.Id))
                .Select(x => new PayrollPostingAccountDto
                { Id = x.Id, Code = x.Code, Name = x.Name, AccountType = x.AccountType.ToString() }).ToArray(),
            Loans = rows.Select(x => new LoanRecoveryLoanDto
            {
                LoanId = x.Loan.Id, EmployeeName = x.Employee.FullName,
                IssueDate = x.Loan.IssueDate, PrincipalAmount = x.Loan.PrincipalAmount,
                OutstandingAmount = x.Loan.OutstandingAmount, IsClosed = x.Loan.IsClosed,
                RowVersion = Convert.ToBase64String(x.Loan.RowVersion),
                DisbursementJournalReference = journals.First(j => j.SourceId == x.Loan.Id).PublicId
            }).ToArray()
        });
    }

    public async Task<ApiResponse<IReadOnlyList<LoanRecoveryHistoryDto>>> GetHistoryAsync(
        long loanId, CancellationToken ct = default)
    {
        if (!Allowed) return Error<IReadOnlyList<LoanRecoveryHistoryDto>>("Tenant administrator permission required.", 403);
        if (loanId <= 0) return Error<IReadOnlyList<LoanRecoveryHistoryDto>>("Invalid loan.");
        var tenant = user.TenantId;
        if (!await db.LoanAdvances.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
            x.Id == loanId && !x.IsDeleted, ct))
            return Error<IReadOnlyList<LoanRecoveryHistoryDto>>("Loan not found.", 404);
        var records = await (from r in db.LoanAdvanceRecoveries.AsNoTracking()
            join j in db.Journals.AsNoTracking() on new { r.TenantId, Id = r.JournalId }
                equals new { j.TenantId, Id = (long?)j.Id }
            where r.TenantId == tenant && r.LoanAdvanceId == loanId && !r.IsDeleted &&
                j.State == JournalState.Posted && !j.IsDeleted &&
                ((j.SourceType == "LoanAdvanceRecovery" && j.SourceId == r.Id &&
                    !r.PayrollEmployeeId.HasValue) ||
                 (j.SourceType == "PayrollRun" && r.PayrollEmployeeId.HasValue))
            orderby r.RecoveryDate descending, r.Id descending
            select new LoanRecoveryHistoryDto
            { RecoveryId = r.Id, RecoveryDate = r.RecoveryDate, Amount = r.Amount,
                Remarks = r.Remarks, JournalReference = j.PublicId, JournalNumber = j.JournalNumber,
                IsCompensated = db.Journals.AsNoTracking().Any(x => x.TenantId == tenant &&
                    !x.IsDeleted && x.ReversalOfJournalId == j.Id &&
                    x.State == JournalState.Posted && x.SourceType == "PayrollRunReversal" &&
                    x.SourceId == j.SourceId),
                CompensationJournalNumber = db.Journals.AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.ReversalOfJournalId == j.Id &&
                    x.State == JournalState.Posted && x.SourceType == "PayrollRunReversal" &&
                    x.SourceId == j.SourceId).Select(x => x.JournalNumber).FirstOrDefault() })
            .Take(501).ToListAsync(ct);
        if (records.Count > 500)
            return Error<IReadOnlyList<LoanRecoveryHistoryDto>>("Recovery history exceeds safe display limit.", 409);
        return ApiResponse<IReadOnlyList<LoanRecoveryHistoryDto>>.SuccessResponse(records);
    }

    public async Task<ApiResponse<PostLoanRecoveryResultDto>> PostAsync(
        long loanId, PostLoanRecoveryRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PostLoanRecoveryResultDto>("Tenant administrator permission required.", 403);
        if (loanId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.RecoveryDate == default || !Money(request.Amount) ||
            request.AccountingPeriodId <= 0 || request.CashAccountId <= 0 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256 ||
            request.Remarks?.Length > 500)
            return Error<PostLoanRecoveryResultDto>("Recovery request, amount, period, cash ledger and version required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var loan = await db.LoanAdvances.SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == loanId && !x.IsDeleted, ct);
            if (loan == null) return Error<PostLoanRecoveryResultDto>("Loan not found.", 404);

            var replay = await db.LoanAdvanceRecoveries.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, ct);
            if (replay != null)
            {
                if (replay.LoanAdvanceId != loanId || replay.RecoveryDate != request.RecoveryDate ||
                    replay.Amount != request.Amount || replay.Remarks != Clean(request.Remarks) ||
                    !replay.JournalId.HasValue)
                    return Error<PostLoanRecoveryResultDto>("Request ID already used for a different recovery.", 409);
                var posted = await db.Journals.AsNoTracking().SingleOrDefaultAsync(j =>
                    j.TenantId == tenant && j.Id == replay.JournalId && !j.IsDeleted, ct);
                if (posted == null || posted.State != JournalState.Posted ||
                    posted.SourceType != "LoanAdvanceRecovery" || posted.SourceId != replay.Id ||
                    posted.IdempotencyKey != PostingKey(tenant, request.ClientRequestId) ||
                    posted.JournalDate != request.RecoveryDate ||
                    posted.AccountingPeriodId != request.AccountingPeriodId ||
                    await db.Journals.AsNoTracking().AnyAsync(j => j.TenantId == tenant &&
                        j.ReversalOfJournalId == posted.Id && j.State == JournalState.Posted && !j.IsDeleted, ct))
                    return Error<PostLoanRecoveryResultDto>("Existing recovery journal needs reconciliation.", 409);
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.JournalId == posted.Id && !x.IsDeleted).ToListAsync(ct);
                if (lines.Count != 2 ||
                    lines.Count(x => x.AccountId == request.CashAccountId &&
                        x.DebitAmount == request.Amount && x.CreditAmount == 0m) != 1 ||
                    lines.Count(x => x.CreditAmount == request.Amount && x.DebitAmount == 0m) != 1)
                    return Error<PostLoanRecoveryResultDto>("Existing recovery entries do not match.", 409);
                return ApiResponse<PostLoanRecoveryResultDto>.SuccessResponse(new PostLoanRecoveryResultDto
                { LoanId = loanId, RecoveryId = replay.Id, JournalReference = posted.PublicId,
                    JournalNumber = posted.JournalNumber, Amount = replay.Amount,
                    OutstandingAmount = loan.OutstandingAmount, AlreadyPosted = true });
            }
            if (!Version(loan.RowVersion, request.RowVersion))
                return Error<PostLoanRecoveryResultDto>("Loan changed; reload and retry.", 409);
            if (loan.IsClosed || !Money(loan.PrincipalAmount) || loan.OutstandingAmount <= 0m ||
                !Money(loan.OutstandingAmount) || request.Amount > loan.OutstandingAmount ||
                request.RecoveryDate < loan.IssueDate ||
                request.RecoveryDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return Error<PostLoanRecoveryResultDto>("Recovery date or amount exceeds open loan balance.", 409);
            var disbursements = await db.Journals.AsNoTracking().Where(j => j.TenantId == tenant &&
                j.SourceType == "LoanAdvance" && j.SourceId == loan.Id && !j.IsDeleted)
                .Take(2).ToListAsync(ct);
            if (disbursements.Count != 1 || disbursements[0].State != JournalState.Posted ||
                disbursements[0].ReversalOfJournalId.HasValue ||
                disbursements[0].JournalDate > request.RecoveryDate ||
                await db.Journals.AsNoTracking().AnyAsync(j => j.TenantId == tenant &&
                    j.ReversalOfJournalId == disbursements[0].Id &&
                    j.State == JournalState.Posted && !j.IsDeleted, ct))
                return Error<PostLoanRecoveryResultDto>("Unreversed posted loan disbursement required.", 409);
            var disbursement = disbursements[0];
            var sourceLines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                x.JournalId == disbursement.Id && !x.IsDeleted).ToListAsync(ct);
            var receivables = sourceLines.Where(x => x.DebitAmount == loan.PrincipalAmount &&
                x.CreditAmount == 0m && x.EmployeeId == loan.EmployeeId &&
                x.CampusId == disbursement.CampusId).ToArray();
            if (sourceLines.Count != 2 || receivables.Length != 1 ||
                sourceLines.Count(x => x.CreditAmount == loan.PrincipalAmount &&
                    x.DebitAmount == 0m) != 1 ||
                !disbursement.CampusId.HasValue || disbursement.CurrencyCode != "BDT")
                return Error<PostLoanRecoveryResultDto>("Disbursement GL does not reconcile with loan principal.", 409);
            var receivable = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == receivables[0].AccountId &&
                x.AccountType == AccountType.Asset && x.CurrencyCode == "BDT" &&
                x.IsActive && !x.IsGroup && x.AllowsPosting && !x.IsDeleted, ct);
            var cash = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.CashAccountId &&
                x.AccountType == AccountType.Asset && x.CurrencyCode == "BDT" &&
                x.IsActive && !x.IsGroup && x.AllowsPosting && !x.IsDeleted, ct);
            if (receivable == null || cash == null || receivable.Id == cash.Id ||
                CashLedger(receivable) || !CashLedger(cash) ||
                await db.BankAccounts.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    !x.IsDeleted && (x.AccountId == cash.Id || x.AccountId == receivable.Id), ct))
                return Error<PostLoanRecoveryResultDto>("Active loan receivable and non-bank cash-in-hand ledgers required.", 409);
            if (!await db.Employees.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.Id == loan.EmployeeId && !x.IsDeleted && x.JoiningDate <= request.RecoveryDate &&
                (!x.LeavingDate.HasValue || x.LeavingDate >= request.RecoveryDate), ct))
                return Error<PostLoanRecoveryResultDto>("Employee employment dates do not cover recovery.", 409);
            var campusIds = await db.EmployeeCampusAssignments.AsNoTracking().Where(x =>
                x.TenantId == tenant && x.EmployeeId == loan.EmployeeId && !x.IsDeleted &&
                x.EffectiveFrom <= request.RecoveryDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.RecoveryDate))
                .Select(x => x.CampusId).Distinct().Take(2).ToListAsync(ct);
            if (campusIds.Count != 1 || campusIds[0] != disbursement.CampusId ||
                !await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.Id == campusIds[0] && x.IsActive && !x.IsDeleted, ct))
                return Error<PostLoanRecoveryResultDto>("Loan recovery requires the original active employee campus.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId &&
                !x.IsDeleted && !x.IsClosed && x.StartDate <= request.RecoveryDate &&
                x.EndDate >= request.RecoveryDate, ct);
            if (period == null)
                return Error<PostLoanRecoveryResultDto>("Open accounting period must cover recovery date.", 409);

            // Existing payroll snapshots may already withhold this employee's loan installment.
            // They lack a per-loan allocation; never post a second cash recovery against them.
            var payrollWithholding = await (from line in db.PayrollLines.AsNoTracking()
                join pe in db.PayrollEmployees.AsNoTracking()
                    on new { line.TenantId, Id = line.PayrollEmployeeId }
                    equals new { pe.TenantId, pe.Id }
                join run in db.PayrollRuns.AsNoTracking()
                    on new { pe.TenantId, Id = pe.PayrollRunId }
                    equals new { run.TenantId, run.Id }
                join component in db.SalaryComponents.AsNoTracking()
                    on new { line.TenantId, Id = line.SalaryComponentId }
                    equals new { component.TenantId, component.Id }
                where line.TenantId == tenant && !line.IsDeleted && !pe.IsDeleted &&
                    !run.IsDeleted && !component.IsDeleted &&
                    pe.EmployeeId == loan.EmployeeId && run.PeriodEnd >= loan.IssueDate &&
                    run.State != PayrollRunState.Cancelled && component.Code == "LOAN_RECOVERY" &&
                    line.Amount > 0m
                select new { PayrollEmployeeId = pe.Id, Amount = line.Amount,
                    RunJournalId = run.JournalId, RunState = run.State }).ToListAsync(ct);
            var payrollEmployeeIds = payrollWithholding.Select(x => x.PayrollEmployeeId).Distinct().ToArray();
            var payrollSettlements = await db.LoanAdvanceRecoveries.AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted && x.PayrollEmployeeId.HasValue &&
                payrollEmployeeIds.Contains(x.PayrollEmployeeId.Value)).ToListAsync(ct);
            foreach (var withheld in payrollWithholding)
            {
                var settlements = payrollSettlements.Where(x =>
                    x.PayrollEmployeeId == withheld.PayrollEmployeeId).ToArray();
                if (withheld.RunState != PayrollRunState.Posted || !withheld.RunJournalId.HasValue ||
                    settlements.Length == 0 || settlements.Any(x => x.JournalId != withheld.RunJournalId) ||
                    settlements.Sum(x => x.Amount) != withheld.Amount)
                    return Error<PostLoanRecoveryResultDto>(
                        "Payroll loan withholding is not fully settled per loan; reconcile payroll first.", 409);
            }
            var previous = await db.LoanAdvanceRecoveries.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.LoanAdvanceId == loanId)
                .OrderBy(x => x.Id).Take(501).ToListAsync(ct);
            if (previous.Count > 500)
                return Error<PostLoanRecoveryResultDto>("Loan recovery history exceeds the safe reconciliation limit.", 409);
            var (historyError, history) = await LoanRecoveryJournalHistory.ReadAsync(db, tenant, previous, ct);
            if (historyError != null || loan.PrincipalAmount - LoanRecoveryJournalHistory.NetRecovered(history) != loan.OutstandingAmount)
                return Error<PostLoanRecoveryResultDto>("Loan outstanding and recovery history do not reconcile.", 409);
            foreach (var entry in history)
            {
                var prior = entry.Recovery;
                var journal = entry.Source;
                if (journal.CampusId != disbursement.CampusId)
                    return Error<PostLoanRecoveryResultDto>("Earlier recovery journal needs reconciliation.", 409);
                var priorLines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.JournalId == journal.Id && !x.IsDeleted).ToListAsync(ct);
                if (prior.PayrollEmployeeId.HasValue)
                {
                    if (priorLines.Count(x => x.AccountId == receivable.Id &&
                        x.EmployeeId == loan.EmployeeId && x.Description == "Payroll loan recovery " + loan.Id &&
                        x.CreditAmount == prior.Amount && x.DebitAmount == 0m) != 1 ||
                        !await db.PayrollEmployees.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                            x.Id == prior.PayrollEmployeeId && x.EmployeeId == loan.EmployeeId &&
                            !x.IsDeleted, ct))
                        return Error<PostLoanRecoveryResultDto>("Earlier payroll recovery entries need reconciliation.", 409);
                }
                else if (priorLines.Count != 2 || priorLines.Count(x =>
                    x.AccountId == receivable.Id && x.CreditAmount == prior.Amount &&
                    x.DebitAmount == 0m) != 1 ||
                    priorLines.Count(x => x.DebitAmount == prior.Amount && x.CreditAmount == 0m) != 1)
                    return Error<PostLoanRecoveryResultDto>("Earlier cash recovery entries need reconciliation.", 409);
            }
            if (await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                !x.IsDeleted && x.IdempotencyKey == PostingKey(tenant, request.ClientRequestId), ct))
                return Error<PostLoanRecoveryResultDto>("Unlinked recovery journal requires reconciliation.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var journalReference = Guid.NewGuid();
            // Insert a draft, then link the append-only recovery before transitioning to Posted.
            // All steps are enclosed by the same serializable transaction.
            var newJournal = new Journal
            {
                TenantId = tenant, PublicId = journalReference,
                IdempotencyKey = PostingKey(tenant, request.ClientRequestId),
                JournalNumber = "JRN-" + journalReference.ToString("N").ToUpperInvariant(),
                JournalDate = request.RecoveryDate, AccountingPeriodId = period.Id,
                CampusId = disbursement.CampusId, CurrencyCode = "BDT",
                State = JournalState.Draft, SourceType = "LoanAdvanceRecovery",
                ReferenceNumber = "LOAN-" + loan.Id,
                Description = "Institutional employee loan cash recovery",
                CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(newJournal);
            await db.SaveChangesAsync(ct);
            var recovery = new LoanAdvanceRecovery
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId,
                LoanAdvanceId = loan.Id, RecoveryDate = request.RecoveryDate,
                Amount = request.Amount, JournalId = newJournal.Id,
                Remarks = Clean(request.Remarks), CreatedAt = now, CreatedBy = user.UserId
            };
            db.LoanAdvanceRecoveries.Add(recovery);
            await db.SaveChangesAsync(ct);
            newJournal.SourceId = recovery.Id;
            newJournal.State = JournalState.Posted;
            newJournal.ApprovedAt = now; newJournal.ApprovedByUserId = user.UserId;
            newJournal.PostedAt = now; newJournal.PostedByUserId = user.UserId;
            db.JournalLines.AddRange(
                new JournalLine { TenantId = tenant, JournalId = newJournal.Id, LineNo = 1,
                    AccountId = cash.Id, CampusId = disbursement.CampusId,
                    EmployeeId = loan.EmployeeId, DebitAmount = request.Amount, CreditAmount = 0m,
                    Description = "Employee loan cash receipt", CreatedAt = now, CreatedBy = user.UserId },
                new JournalLine { TenantId = tenant, JournalId = newJournal.Id, LineNo = 2,
                    AccountId = receivable.Id, CampusId = disbursement.CampusId,
                    EmployeeId = loan.EmployeeId, DebitAmount = 0m, CreditAmount = request.Amount,
                    Description = "Employee loan receivable reduction", CreatedAt = now, CreatedBy = user.UserId });
            loan.OutstandingAmount -= request.Amount;
            loan.IsClosed = loan.OutstandingAmount == 0m;
            loan.UpdatedAt = now; loan.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PostLoanRecoveryResultDto>.SuccessResponse(new PostLoanRecoveryResultDto
            { LoanId = loan.Id, RecoveryId = recovery.Id, JournalReference = journalReference,
                JournalNumber = newJournal.JournalNumber, Amount = request.Amount,
                OutstandingAmount = loan.OutstandingAmount }, "Cash recovery posted to institutional GL.");
        }
        catch (DbUpdateConcurrencyException)
        { return Error<PostLoanRecoveryResultDto>("Loan changed concurrently; reload.", 409); }
        catch (DbUpdateException)
        { return Error<PostLoanRecoveryResultDto>("Loan recovery conflicts with an existing transaction.", 409); }
    }
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
