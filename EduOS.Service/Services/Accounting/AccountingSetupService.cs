using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingSetupService : IAccountingSetupService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public AccountingSetupService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool Readable => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool Writable => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal"));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Institutional accounting access denied.", 403);
    private static bool Version(byte[] actual, string? supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static string V(byte[] value) => Convert.ToBase64String(value);
    private IQueryable<Account> Accounts => db.Accounts.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<AccountingPeriod> Periods => db.AccountingPeriods.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<BankAccount> Banks => db.BankAccounts.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static AccountDto AccountMap(Account x, string? parent = null) => new()
    {
        Id = x.Id, ParentAccountId = x.ParentAccountId, ParentAccountName = parent, Code = x.Code,
        Name = x.Name, AccountType = x.AccountType, IsGroup = x.IsGroup, IsActive = x.IsActive, RowVersion = V(x.RowVersion)
    };
    private static AccountingPeriodDto PeriodMap(AccountingPeriod x) => new()
    {
        Id = x.Id, Name = x.Name, StartDate = x.StartDate, EndDate = x.EndDate, IsClosed = x.IsClosed,
        ClosedAt = x.ClosedAt, ClosedByUserId = x.ClosedByUserId, RowVersion = V(x.RowVersion)
    };
    private static BankAccountDto BankMap(BankAccount x, string name) => new()
    {
        Id = x.Id, AccountId = x.AccountId, LedgerAccountName = name, BankName = x.BankName,
        AccountName = x.AccountName, AccountNumber = x.AccountNumber, BranchName = x.BranchName,
        RoutingNumber = x.RoutingNumber, CurrencyCode = x.CurrencyCode, IsActive = x.IsActive, RowVersion = V(x.RowVersion)
    };
    public async Task<ApiResponse<PagedResult<AccountDto>>> GetAccountsAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!Readable) return Denied<PagedResult<AccountDto>>();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Accounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            if (q.Length > 100) return ApiResponse<PagedResult<AccountDto>>.ErrorResponse("Search is too long.");
            query = query.Where(x => x.Code.Contains(q) || x.Name.Contains(q));
        }
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<Account>() :
            await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var parentIds = rows.Where(x => x.ParentAccountId.HasValue).Select(x => x.ParentAccountId!.Value).Distinct().ToArray();
        var parents = await Accounts.AsNoTracking().Where(x => parentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return ApiResponse<PagedResult<AccountDto>>.SuccessResponse(new PagedResult<AccountDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(x => AccountMap(x, x.ParentAccountId.HasValue && parents.TryGetValue(x.ParentAccountId.Value, out var name) ? name : null)).ToList()
        });
    }
    public async Task<ApiResponse<AccountDto>> GetAccountAsync(long accountId, CancellationToken ct = default)
    {
        if (!Readable) return Denied<AccountDto>();
        var row = await Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (row == null) return ApiResponse<AccountDto>.ErrorResponse("Account not found.", 404);
        var parent = row.ParentAccountId.HasValue ? await Accounts.AsNoTracking().Where(x => x.Id == row.ParentAccountId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return ApiResponse<AccountDto>.SuccessResponse(AccountMap(row, parent));
    }
    public async Task<ApiResponse<AccountDto>> SaveAccountAsync(long? accountId, SaveAccountRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Denied<AccountDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) ||
            !Enum.IsDefined(request.AccountType))
            return ApiResponse<AccountDto>.ErrorResponse("Account code, name and type are required.");
        var code = request.Code.Trim().ToUpperInvariant(); var name = request.Name.Trim();
        if (code.Length > 50 || name.Length > 200) return ApiResponse<AccountDto>.ErrorResponse("Account code or name is too long.");
        try
        {
            var row = accountId.HasValue ? await Accounts.FirstOrDefaultAsync(x => x.Id == accountId.Value, ct) : null;
            if (accountId.HasValue && row == null) return ApiResponse<AccountDto>.ErrorResponse("Account not found.", 404);
            if (row != null && !Version(row.RowVersion, request.RowVersion)) return ApiResponse<AccountDto>.ErrorResponse("Account changed; reload.", 409);
            if (await Accounts.AnyAsync(x => x.Code == code && (row == null || x.Id != row.Id), ct))
                return ApiResponse<AccountDto>.ErrorResponse("Account code already exists.", 409);
            if (row != null)
            {
                var used = await db.JournalLines.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId && x.AccountId == row.Id && !x.IsDeleted, ct);
                if (used && (row.Code != code || row.AccountType != request.AccountType || request.IsGroup))
                    return ApiResponse<AccountDto>.ErrorResponse("Referenced account code, type and posting capability cannot change.", 409);
                if ((!request.IsGroup || !request.IsActive) && await Accounts.AnyAsync(x => x.ParentAccountId == row.Id, ct))
                    return ApiResponse<AccountDto>.ErrorResponse("Account with child accounts must remain an active group.", 409);
            }
            if (request.ParentAccountId.HasValue)
            {
                var parent = await Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ParentAccountId.Value, ct);
                if (parent == null || !parent.IsGroup || !parent.IsActive || parent.AccountType != request.AccountType)
                    return ApiResponse<AccountDto>.ErrorResponse("Parent must be an active group of the same account type.");
                var visited = new HashSet<long>();
                for (var current = parent; current != null; )
                {
                    if ((row != null && current.Id == row.Id) || !visited.Add(current.Id))
                        return ApiResponse<AccountDto>.ErrorResponse("Account hierarchy cycle detected.");
                    if (current.ParentAccountId == null) break;
                    current = await Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == current.ParentAccountId.Value, ct);
                    if (current == null) return ApiResponse<AccountDto>.ErrorResponse("Parent hierarchy is invalid.", 409);
                }
            }
            var now = clock.GetUtcNow().UtcDateTime;
            if (row == null)
            {
                row = new Account { TenantId = user.TenantId, CreatedAt = now, CreatedBy = user.UserId, CurrencyCode = "BDT" };
                db.Accounts.Add(row);
            }
            else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
            row.Code = code; row.Name = name; row.ParentAccountId = request.ParentAccountId;
            row.AccountType = request.AccountType; row.IsGroup = request.IsGroup; row.AllowsPosting = !request.IsGroup; row.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return ApiResponse<AccountDto>.SuccessResponse(AccountMap(row), "Account saved.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AccountDto>.ErrorResponse("Account changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<AccountDto>.ErrorResponse("Account conflict; reload.", 409); }
    }
    public async Task<ApiResponse<IReadOnlyList<AccountingPeriodDto>>> GetPeriodsAsync(int? year, CancellationToken ct = default)
    {
        if (!Readable) return Denied<IReadOnlyList<AccountingPeriodDto>>();
        var query = Periods.AsNoTracking();
        if (year.HasValue)
        {
            if (year.Value < 1900 || year.Value > 2200) return ApiResponse<IReadOnlyList<AccountingPeriodDto>>.ErrorResponse("Invalid year.");
            var start = new DateOnly(year.Value, 1, 1); var end = new DateOnly(year.Value, 12, 31);
            query = query.Where(x => x.StartDate <= end && x.EndDate >= start);
        }
        var rows = await query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id).Take(500).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AccountingPeriodDto>>.SuccessResponse(rows.Select(PeriodMap).ToArray());
    }
    public async Task<ApiResponse<AccountingPeriodDto>> SavePeriodAsync(long? periodId, SaveAccountingPeriodRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Denied<AccountingPeriodDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100 ||
            request.StartDate == default || request.EndDate < request.StartDate)
            return ApiResponse<AccountingPeriodDto>.ErrorResponse("Valid name and date range required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var row = periodId.HasValue ? await Periods.FirstOrDefaultAsync(x => x.Id == periodId.Value, ct) : null;
            if (periodId.HasValue && row == null) return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period not found.", 404);
            if (row != null && (!Version(row.RowVersion, request.RowVersion) || row.IsClosed))
                return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period closed or changed; reload.", 409);
            if (row != null && await db.Journals.AnyAsync(x => x.TenantId == user.TenantId && x.AccountingPeriodId == row.Id && !x.IsDeleted, ct))
                return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period with journals cannot be edited.", 409);
            if (await Periods.AnyAsync(x => (!periodId.HasValue || x.Id != periodId.Value) &&
                x.StartDate <= request.EndDate && x.EndDate >= request.StartDate, ct))
                return ApiResponse<AccountingPeriodDto>.ErrorResponse("Accounting periods cannot overlap.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            if (row == null) { row = new AccountingPeriod { TenantId = user.TenantId, CreatedAt = now, CreatedBy = user.UserId }; db.AccountingPeriods.Add(row); }
            else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
            row.Name = request.Name.Trim(); row.StartDate = request.StartDate; row.EndDate = request.EndDate;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AccountingPeriodDto>.SuccessResponse(PeriodMap(row), "Period saved.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period conflict; reload.", 409); }
    }
    public async Task<ApiResponse<AccountingPeriodDto>> ClosePeriodAsync(long periodId, CloseAccountingPeriodRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Denied<AccountingPeriodDto>();
        if (request == null) return ApiResponse<AccountingPeriodDto>.ErrorResponse("Row version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var row = await Periods.FirstOrDefaultAsync(x => x.Id == periodId, ct);
            if (row == null) return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period not found.", 404);
            if (row.IsClosed) return ApiResponse<AccountingPeriodDto>.SuccessResponse(PeriodMap(row), "Already closed.");
            if (!Version(row.RowVersion, request.RowVersion)) return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period changed; reload.", 409);
            if (await db.Journals.AnyAsync(x => x.TenantId == user.TenantId && x.AccountingPeriodId == row.Id && !x.IsDeleted &&
                (x.State == JournalState.Draft || x.State == JournalState.Submitted || x.State == JournalState.Approved), ct))
                return ApiResponse<AccountingPeriodDto>.ErrorResponse("Resolve unposted journals before closing the period.", 409);
            var readiness = await GetPeriodCloseReadinessAsync(periodId, ct);
            if (!readiness.Success || readiness.Data is not { CanClose: true })
                return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period reconciliation is incomplete; inspect close readiness before closing.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            row.IsClosed = true; row.ClosedAt = now; row.ClosedByUserId = user.UserId; row.UpdatedAt = now; row.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<AccountingPeriodDto>.SuccessResponse(PeriodMap(row), "Period closed.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<AccountingPeriodDto>.ErrorResponse("Period conflict; reload.", 409); }
    }
    public async Task<ApiResponse<IReadOnlyList<BankAccountDto>>> GetBankAccountsAsync(CancellationToken ct = default)
    {
        if (!Writable) return Denied<IReadOnlyList<BankAccountDto>>();
        var rows = await (from b in Banks.AsNoTracking() join a in Accounts.AsNoTracking() on b.AccountId equals a.Id
            orderby b.BankName, b.Id select new { Bank = b, LedgerName = a.Name }).Take(500).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<BankAccountDto>>.SuccessResponse(rows.Select(x => BankMap(x.Bank, x.LedgerName)).ToArray());
    }
    public async Task<ApiResponse<BankAccountDto>> SaveBankAccountAsync(long? bankAccountId, SaveBankAccountRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Denied<BankAccountDto>();
        if (request == null || request.AccountId <= 0 || string.IsNullOrWhiteSpace(request.BankName) ||
            string.IsNullOrWhiteSpace(request.AccountName) || string.IsNullOrWhiteSpace(request.AccountNumber) ||
            request.BankName.Trim().Length > 150 || request.AccountName.Trim().Length > 150 ||
            request.AccountNumber.Trim().Length > 100 || (request.BranchName?.Length ?? 0) > 100 ||
            (request.RoutingNumber?.Length ?? 0) > 100 || request.CurrencyCode != "BDT")
            return ApiResponse<BankAccountDto>.ErrorResponse("Valid bank details and BDT currency required.");
        var account = await Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AccountId && x.IsActive &&
            !x.IsGroup && x.AllowsPosting && x.AccountType == AccountType.Asset && x.CurrencyCode == "BDT", ct);
        if (account == null) return ApiResponse<BankAccountDto>.ErrorResponse("An active BDT asset posting account is required.");
        try
        {
            var row = bankAccountId.HasValue ? await Banks.FirstOrDefaultAsync(x => x.Id == bankAccountId.Value, ct) : null;
            if (bankAccountId.HasValue && row == null) return ApiResponse<BankAccountDto>.ErrorResponse("Bank account not found.", 404);
            if (row != null && !Version(row.RowVersion, request.RowVersion)) return ApiResponse<BankAccountDto>.ErrorResponse("Bank account changed; reload.", 409);
            if (row != null && row.AccountId != request.AccountId && await db.StudentPayments.AnyAsync(x => x.TenantId == user.TenantId && x.BankAccountId == row.Id && !x.IsDeleted, ct))
                return ApiResponse<BankAccountDto>.ErrorResponse("Bank ledger cannot change after payments reference it.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            if (row == null) { row = new BankAccount { TenantId = user.TenantId, CreatedAt = now, CreatedBy = user.UserId }; db.BankAccounts.Add(row); }
            else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
            row.AccountId = request.AccountId; row.BankName = request.BankName.Trim(); row.AccountName = request.AccountName.Trim();
            row.AccountNumber = request.AccountNumber.Trim(); row.BranchName = request.BranchName?.Trim();
            row.RoutingNumber = request.RoutingNumber?.Trim(); row.CurrencyCode = "BDT"; row.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return ApiResponse<BankAccountDto>.SuccessResponse(BankMap(row, account.Name), "Bank account saved.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<BankAccountDto>.ErrorResponse("Bank account changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<BankAccountDto>.ErrorResponse("Bank account conflict; reload.", 409); }
    }
}
