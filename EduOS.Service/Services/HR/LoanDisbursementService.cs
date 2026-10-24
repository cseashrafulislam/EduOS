using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.HR;

/// <summary>Institutional employee loan cash disbursement; separate from platform SaaS billing.</summary>
public sealed class LoanDisbursementService : ILoanDisbursementService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public LoanDisbursementService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }

    private bool Allowed => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static bool Money(decimal value) => value > 0m && value <= 999999999999m &&
        decimal.Round(value, 2) == value;
    private static bool Version(byte[] actual, string supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual.Length > 0 &&
            actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static Guid PostingKey(long tenant, long loanId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            "EduOS:Institution:LoanAdvanceDisbursement:" + tenant + ":" + loanId));
        return new Guid(bytes.AsSpan(0, 16));
    }
    private static bool CashLedger(Account account) =>
        account.Code.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
        account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase);

    public async Task<ApiResponse<LoanDisbursementRegisterDto>> GetRegisterAsync(
        bool pendingOnly, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<LoanDisbursementRegisterDto>("Tenant administrator permission required.", 403);
        if (page < 1 || pageSize is < 1 or > 10 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<LoanDisbursementRegisterDto>("Invalid loan register pagination.");
        var tenant = user.TenantId;
        var query = from loan in db.LoanAdvances.AsNoTracking()
                    join employee in db.Employees.AsNoTracking()
                        on new { loan.TenantId, Id = loan.EmployeeId }
                        equals new { employee.TenantId, employee.Id }
                    where loan.TenantId == tenant && !loan.IsDeleted && !employee.IsDeleted
                    select new { Loan = loan, Employee = employee };
        if (pendingOnly)
            query = query.Where(x => !db.Journals.AsNoTracking().Any(j =>
                j.TenantId == tenant && j.SourceType == "LoanAdvance" &&
                j.SourceId == x.Loan.Id && !j.IsDeleted));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Loan.IssueDate).ThenByDescending(x => x.Loan.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Loan.Id).ToArray();
        var journals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
            x.SourceType == "LoanAdvance" && x.SourceId.HasValue &&
            ids.Contains(x.SourceId.Value) && !x.IsDeleted).ToListAsync(ct);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.StartDate).Take(101)
            .Select(x => new PayrollPostingPeriodDto { Id = x.Id, Name = x.Name,
                StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync(ct);
        if (periods.Count > 100)
            return Error<LoanDisbursementRegisterDto>("Too many open accounting periods.", 409);
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT" && x.AccountType == AccountType.Asset)
            .OrderBy(x => x.Code).Take(301).ToListAsync(ct);
        if (accounts.Count > 300)
            return Error<LoanDisbursementRegisterDto>("Too many asset accounts for selector.", 409);
        return ApiResponse<LoanDisbursementRegisterDto>.SuccessResponse(new LoanDisbursementRegisterDto
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            OpenPeriods = periods,
            AssetAccounts = accounts.Select(x => new PayrollPostingAccountDto
                { Id = x.Id, Code = x.Code, Name = x.Name, AccountType = x.AccountType.ToString() }).ToArray(),
            Loans = rows.Select(x => new LoanDisbursementRowDto
            {
                LoanId = x.Loan.Id, EmployeeName = x.Employee.FullName,
                IssueDate = x.Loan.IssueDate, PrincipalAmount = x.Loan.PrincipalAmount,
                OutstandingAmount = x.Loan.OutstandingAmount, InstallmentAmount = x.Loan.InstallmentAmount,
                RowVersion = Convert.ToBase64String(x.Loan.RowVersion),
                JournalReference = journals.FirstOrDefault(j => j.SourceId == x.Loan.Id)?.PublicId
            }).ToArray()
        });
    }

    public async Task<ApiResponse<PostLoanDisbursementResultDto>> PostAsync(
        long loanId, PostLoanDisbursementRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PostLoanDisbursementResultDto>("Tenant administrator permission required.", 403);
        if (loanId <= 0 || request == null || request.DisbursementDate == default ||
            request.AccountingPeriodId <= 0 || request.LoanReceivableAccountId <= 0 ||
            request.CashAccountId <= 0 || request.CashAccountId == request.LoanReceivableAccountId ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256)
            return Error<PostLoanDisbursementResultDto>("Loan, date, period, distinct accounts and row version required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var loan = await db.LoanAdvances.SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == loanId && !x.IsDeleted, ct);
            if (loan == null) return Error<PostLoanDisbursementResultDto>("Loan not found.", 404);
            var existing = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
                x.SourceType == "LoanAdvance" && x.SourceId == loanId && !x.IsDeleted)
                .Take(2).ToListAsync(ct);
            if (existing.Count > 1)
                return Error<PostLoanDisbursementResultDto>("Duplicate disbursement journals require reconciliation.", 409);
            if (existing.Count == 1)
            {
                var postedJournal = existing[0];
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.JournalId == postedJournal.Id && !x.IsDeleted).ToListAsync(ct);
                if (postedJournal.State != JournalState.Posted || postedJournal.ReversalOfJournalId.HasValue ||
                    postedJournal.IdempotencyKey != PostingKey(tenant, loanId) ||
                    postedJournal.JournalDate != request.DisbursementDate ||
                    postedJournal.AccountingPeriodId != request.AccountingPeriodId ||
                    lines.Count != 2 || lines.Count(x => x.AccountId == request.LoanReceivableAccountId &&
                        x.DebitAmount == loan.PrincipalAmount && x.CreditAmount == 0m) != 1 ||
                    lines.Count(x => x.AccountId == request.CashAccountId &&
                        x.CreditAmount == loan.PrincipalAmount && x.DebitAmount == 0m) != 1 ||
                    await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                        x.ReversalOfJournalId == postedJournal.Id && x.State == JournalState.Posted && !x.IsDeleted, ct))
                    return Error<PostLoanDisbursementResultDto>("Existing disbursement does not match request or was reversed.", 409);
                return ApiResponse<PostLoanDisbursementResultDto>.SuccessResponse(new PostLoanDisbursementResultDto
                { LoanId = loanId, JournalReference = postedJournal.PublicId,
                    JournalNumber = postedJournal.JournalNumber, Amount = loan.PrincipalAmount, AlreadyPosted = true });
            }
            if (!Version(loan.RowVersion, request.RowVersion))
                return Error<PostLoanDisbursementResultDto>("Loan changed; reload and retry.", 409);
            if (loan.IsClosed || !Money(loan.PrincipalAmount) || !Money(loan.InstallmentAmount) ||
                loan.InstallmentAmount > loan.PrincipalAmount ||
                loan.OutstandingAmount != loan.PrincipalAmount ||
                request.DisbursementDate < loan.IssueDate ||
                request.DisbursementDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return Error<PostLoanDisbursementResultDto>("Only unrecovered, open loans can be disbursed on or after issue date.", 409);
            if (loan.CreatedBy == user.UserId)
                return Error<PostLoanDisbursementResultDto>("Loan creator cannot approve their own disbursement.", 409);
            if (!await db.Employees.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.Id == loan.EmployeeId && !x.IsDeleted && x.JoiningDate <= request.DisbursementDate &&
                (!x.LeavingDate.HasValue || x.LeavingDate >= request.DisbursementDate), ct))
                return Error<PostLoanDisbursementResultDto>("Employee was not employed on disbursement date.", 409);
            var assignments = await db.EmployeeCampusAssignments.AsNoTracking().Where(x =>
                x.TenantId == tenant && x.EmployeeId == loan.EmployeeId && !x.IsDeleted &&
                x.EffectiveFrom <= request.DisbursementDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.DisbursementDate))
                .Select(x => x.CampusId).Distinct().Take(2).ToListAsync(ct);
            if (assignments.Count != 1 ||
                !await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.Id == assignments[0] && x.IsActive && !x.IsDeleted, ct))
                return Error<PostLoanDisbursementResultDto>("Employee needs exactly one active campus assignment on disbursement date.", 409);
            if (await db.LoanAdvanceRecoveries.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.LoanAdvanceId == loanId && !x.IsDeleted, ct))
                return Error<PostLoanDisbursementResultDto>("Existing loan recoveries require reconciliation before disbursement.", 409);
            if (await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                x.IdempotencyKey == PostingKey(tenant, loanId), ct))
                return Error<PostLoanDisbursementResultDto>("An unlinked disbursement journal already exists.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate <= request.DisbursementDate &&
                x.EndDate >= request.DisbursementDate, ct);
            if (period == null) return Error<PostLoanDisbursementResultDto>("Open accounting period must cover disbursement date.", 409);
            var receivable = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.LoanReceivableAccountId &&
                !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
                x.CurrencyCode == "BDT" && x.AccountType == AccountType.Asset, ct);
            var cash = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.CashAccountId &&
                !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
                x.CurrencyCode == "BDT" && x.AccountType == AccountType.Asset, ct);
            if (receivable == null || cash == null || CashLedger(receivable) || !CashLedger(cash) ||
                await db.BankAccounts.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    !x.IsDeleted && (x.AccountId == receivable.Id || x.AccountId == cash.Id), ct))
                return Error<PostLoanDisbursementResultDto>("Use an active BDT loan receivable and a non-bank cash-in-hand ledger.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var reference = Guid.NewGuid();
            var journal = new Journal
            {
                TenantId = tenant, PublicId = reference,
                IdempotencyKey = PostingKey(tenant, loanId),
                JournalNumber = "JRN-" + reference.ToString("N").ToUpperInvariant(),
                JournalDate = request.DisbursementDate, AccountingPeriodId = period.Id,
                CampusId = assignments[0], CurrencyCode = "BDT", State = JournalState.Posted,
                SourceType = "LoanAdvance", SourceId = loan.Id,
                ReferenceNumber = "LOAN-" + loan.Id,
                Description = "Institutional employee loan cash disbursement",
                ApprovedByUserId = user.UserId, ApprovedAt = now,
                PostedByUserId = user.UserId, PostedAt = now,
                CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(journal);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(
                new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 1,
                    AccountId = receivable.Id, CampusId = assignments[0], EmployeeId = loan.EmployeeId,
                    DebitAmount = loan.PrincipalAmount, CreditAmount = 0m,
                    Description = "Employee loan receivable", CreatedAt = now, CreatedBy = user.UserId },
                new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 2,
                    AccountId = cash.Id, CampusId = assignments[0], EmployeeId = loan.EmployeeId,
                    DebitAmount = 0m, CreditAmount = loan.PrincipalAmount,
                    Description = "Employee loan cash disbursement", CreatedAt = now, CreatedBy = user.UserId });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PostLoanDisbursementResultDto>.SuccessResponse(new PostLoanDisbursementResultDto
            { LoanId = loanId, JournalReference = reference, JournalNumber = journal.JournalNumber,
                Amount = loan.PrincipalAmount });
        }
        catch (DbUpdateConcurrencyException)
        { return Error<PostLoanDisbursementResultDto>("Loan was updated concurrently; reload.", 409); }
        catch (DbUpdateException)
        { return Error<PostLoanDisbursementResultDto>("Disbursement conflicts with another posting; reload.", 409); }
    }
}
