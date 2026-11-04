using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Accounting;

// Management planning. Reads booked actuals from the canonical GL reporting service.
public sealed class InstitutionBudgetService : IInstitutionBudgetService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly IAccountingReportingService reporting;
    private readonly TimeProvider clock;

    public InstitutionBudgetService(EduOSDbContext db, ICurrentUserService user,
        IAccountingReportingService reporting, TimeProvider clock)
    { this.db = db; this.user = user; this.reporting = reporting; this.clock = clock; }

    private bool Scoped => user.IsAuthenticated && user.TenantId > 0 && db.CurrentTenantId == user.TenantId;
    private bool CanRead => Scoped && (user.IsTenantAdmin || user.IsInRole("Principal"));
    private bool CanSubmit => CanRead;
    private bool CanReview => Scoped && user.IsTenantAdmin;
    private IQueryable<InstitutionBudget> Budgets => db.InstitutionBudgets.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<InstitutionBudgetLine> Lines => db.InstitutionBudgetLines.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static bool MatchVersion(byte[] actual, string supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static InstitutionBudgetDto Map(InstitutionBudget budget, IReadOnlyList<InstitutionBudgetLine> lines) => new()
    {
        Id = budget.Id, PublicId = budget.PublicId, Name = budget.Name, StartDate = budget.StartDate,
        EndDate = budget.EndDate, StateCode = budget.StateCode, SubmittedByUserId = budget.SubmittedByUserId,
        SubmittedAtUtc = budget.SubmittedAtUtc, ReviewedByUserId = budget.ReviewedByUserId,
        ReviewedAtUtc = budget.ReviewedAtUtc, ReviewReason = budget.ReviewReason, RowVersion = Version(budget.RowVersion),
        Lines = lines.OrderBy(x => x.AccountCodeSnapshot).ThenBy(x => x.Id).Select(x => new InstitutionBudgetLineReportDto
        {
            AccountId = x.AccountId, AccountCode = x.AccountCodeSnapshot,
            AccountName = x.AccountNameSnapshot, Category = x.AccountTypeSnapshot == AccountType.Income ? "Income" : "Expense",
            PlannedAmountBdt = x.PlannedAmountBdt
        }).ToArray()
    };

    public async Task<ApiResponse<PagedResult<InstitutionBudgetDto>>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return ApiResponse<PagedResult<InstitutionBudgetDto>>.ErrorResponse("Institution finance access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<PagedResult<InstitutionBudgetDto>>.ErrorResponse("Valid page and size (1–100) required.");
        var q = Budgets.AsNoTracking();
        var count = await q.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<InstitutionBudget>() :
            await q.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
                .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<InstitutionBudgetDto>>.SuccessResponse(new PagedResult<InstitutionBudgetDto>
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows.Select(x => Map(x, [])).ToList() });
    }

    public async Task<ApiResponse<InstitutionBudgetDto>> GetAsync(long id, CancellationToken ct = default)
    {
        if (!CanRead) return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Institution finance access denied.", 403);
        var budget = await Budgets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (budget == null) return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget not found.", 404);
        var lines = await Lines.AsNoTracking().Where(x => x.InstitutionBudgetId == id).ToListAsync(ct);
        return ApiResponse<InstitutionBudgetDto>.SuccessResponse(Map(budget, lines));
    }

    public async Task<ApiResponse<InstitutionBudgetDto>> SubmitAsync(SubmitInstitutionBudgetRequestDto request, CancellationToken ct = default)
    {
        if (!CanSubmit) return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Institution finance access denied.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || request.StartDate == default || request.EndDate < request.StartDate ||
            request.EndDate.DayNumber - request.StartDate.DayNumber > 3660 ||
            request.Lines == null || request.Lines.Count is < 1 or > 100 ||
            request.Lines.Any(x => x.AccountId <= 0 || x.PlannedAmountBdt <= 0m || x.PlannedAmountBdt > 999999999999.99m ||
                decimal.Round(x.PlannedAmountBdt, 2) != x.PlannedAmountBdt) ||
            request.Lines.Select(x => x.AccountId).Distinct().Count() != request.Lines.Count)
            return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Valid dates, unique posting accounts and positive BDT amounts (up to 100 lines) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await Budgets.SingleOrDefaultAsync(x => x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
            {
                var saved = await Lines.AsNoTracking().Where(x => x.InstitutionBudgetId == existing.Id).ToListAsync(ct);
                var same = existing.SubmittedByUserId == user.UserId && existing.Name == request.Name.Trim() &&
                    existing.StartDate == request.StartDate && existing.EndDate == request.EndDate &&
                    saved.Count == request.Lines.Count && saved.All(x => request.Lines.Any(y =>
                        y.AccountId == x.AccountId && y.PlannedAmountBdt == x.PlannedAmountBdt));
                return same ? ApiResponse<InstitutionBudgetDto>.SuccessResponse(Map(existing, saved)) :
                    ApiResponse<InstitutionBudgetDto>.ErrorResponse("Idempotency key already belongs to a different budget request.", 409);
            }
            var ids = request.Lines.Select(x => x.AccountId).ToArray();
            var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                !x.IsDeleted && ids.Contains(x.Id) && x.IsActive && !x.IsGroup && x.AllowsPosting &&
                x.CurrencyCode == "BDT" && (x.AccountType == AccountType.Income || x.AccountType == AccountType.Expense))
                .ToDictionaryAsync(x => x.Id, ct);
            if (accounts.Count != request.Lines.Count)
                return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Every line must reference an active BDT income/expense posting account.", 409);
            var at = clock.GetUtcNow().UtcDateTime;
            var budget = new InstitutionBudget { TenantId = user.TenantId, Name = request.Name.Trim(),
                ClientRequestId = request.ClientRequestId, StartDate = request.StartDate, EndDate = request.EndDate,
                StateCode = "Submitted", SubmittedByUserId = user.UserId, SubmittedAtUtc = at,
                CreatedAt = at, CreatedBy = user.UserId };
            db.InstitutionBudgets.Add(budget);
            await db.SaveChangesAsync(ct);
            var lines = request.Lines.Select(x => new InstitutionBudgetLine {
                TenantId = user.TenantId, InstitutionBudgetId = budget.Id, AccountId = x.AccountId,
                AccountCodeSnapshot = accounts[x.AccountId].Code, AccountNameSnapshot = accounts[x.AccountId].Name,
                AccountTypeSnapshot = accounts[x.AccountId].AccountType, PlannedAmountBdt = x.PlannedAmountBdt,
                CreatedAt = at, CreatedBy = user.UserId }).ToArray();
            db.InstitutionBudgetLines.AddRange(lines);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InstitutionBudgetDto>.SuccessResponse(Map(budget, lines), "Budget submitted for independent approval.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget request conflict; reload.", 409); }
    }

    public async Task<ApiResponse<InstitutionBudgetDto>> ReviewAsync(long id, ReviewInstitutionBudgetRequestDto request, CancellationToken ct = default)
    {
        if (!CanReview) return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Tenant administrator finance approval required.", 403);
        if (id <= 0 || request == null || request.ReviewRequestId == Guid.Empty ||
            request.Reason?.Length > 800 || (!request.Approve && string.IsNullOrWhiteSpace(request.Reason)))
            return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Valid review reference and rejection reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var budget = await Budgets.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (budget == null) return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget not found.", 404);
            var lines = await Lines.AsNoTracking().Where(x => x.InstitutionBudgetId == id).ToListAsync(ct);
            if (budget.ReviewRequestId == request.ReviewRequestId &&
                budget.StateCode == (request.Approve ? "Approved" : "Rejected") &&
                budget.ReviewedByUserId == user.UserId &&
                budget.ReviewReason == (string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()))
                return ApiResponse<InstitutionBudgetDto>.SuccessResponse(Map(budget, lines));
            if (budget.StateCode != "Submitted")
                return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget already reviewed; create a new proposal for amendments.", 409);
            if (budget.SubmittedByUserId == user.UserId)
                return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Proposer cannot approve or reject their own budget.", 403);
            if (!MatchVersion(budget.RowVersion, request.RowVersion))
                return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget changed; reload before review.", 409);
            if (request.Approve && await Budgets.AsNoTracking().AnyAsync(x => x.Id != id &&
                x.StateCode == "Approved" && x.StartDate <= budget.EndDate && x.EndDate >= budget.StartDate, ct))
                return ApiResponse<InstitutionBudgetDto>.ErrorResponse("An approved institution budget overlaps this period.", 409);
            if (request.Approve)
            {
                var ids = lines.Select(x => x.AccountId).ToArray();
                var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                    !x.IsDeleted && ids.Contains(x.Id) && x.IsActive && !x.IsGroup && x.AllowsPosting &&
                    x.CurrencyCode == "BDT").ToDictionaryAsync(x => x.Id, ct);
                if (accounts.Count != lines.Count || lines.Any(x =>
                    !accounts.TryGetValue(x.AccountId, out var account) ||
                    account.Code != x.AccountCodeSnapshot || account.AccountType != x.AccountTypeSnapshot))
                    return ApiResponse<InstitutionBudgetDto>.ErrorResponse("A budget ledger changed since submission; reject and resubmit.", 409);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            budget.StateCode = request.Approve ? "Approved" : "Rejected";
            budget.ReviewedByUserId = user.UserId; budget.ReviewedAtUtc = now;
            budget.ReviewRequestId = request.ReviewRequestId;
            budget.ReviewReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            budget.UpdatedAt = now; budget.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InstitutionBudgetDto>.SuccessResponse(Map(budget, lines), "Budget reviewed.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<InstitutionBudgetDto>.ErrorResponse("Budget review conflict; reload.", 409); }
    }

    public async Task<ApiResponse<InstitutionBudgetActualDto>> ActualAsync(long id, CancellationToken ct = default)
    {
        if (!CanReview) return ApiResponse<InstitutionBudgetActualDto>.ErrorResponse("Institution-wide budget report requires TenantAdmin.", 403);
        var budget = await Budgets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (budget == null) return ApiResponse<InstitutionBudgetActualDto>.ErrorResponse("Budget not found.", 404);
        if (budget.StateCode != "Approved")
            return ApiResponse<InstitutionBudgetActualDto>.ErrorResponse("Only approved budgets have financial variance reports.", 409);
        var income = await reporting.GetIncomeStatementAsync(budget.StartDate, budget.EndDate, ct);
        if (!income.Success || income.Data == null)
            return ApiResponse<InstitutionBudgetActualDto>.ErrorResponse(income.Message, income.StatusCode);
        var saved = await Lines.AsNoTracking().Where(x => x.InstitutionBudgetId == id).ToListAsync(ct);
        var planned = saved.Select(x => new InstitutionBudgetLineReportDto {
            AccountId = x.AccountId, AccountCode = x.AccountCodeSnapshot,
            AccountName = x.AccountNameSnapshot, Category = x.AccountTypeSnapshot == AccountType.Income ? "Income" : "Expense",
            PlannedAmountBdt = x.PlannedAmountBdt }).ToDictionary(x => x.AccountId);
        foreach (var item in income.Data.IncomeRows.Concat(income.Data.ExpenseRows))
        {
            if (!planned.TryGetValue(item.AccountId, out var row))
            {
                row = new InstitutionBudgetLineReportDto { AccountId = item.AccountId, AccountCode = item.AccountCode,
                    AccountName = item.AccountName, Category = income.Data.IncomeRows.Any(x => x.AccountId == item.AccountId) ? "Income" : "Expense",
                    IsUnbudgeted = true };
                planned.Add(item.AccountId, row);
            }
            row.ActualAmountBdt = item.Amount;
        }
        foreach (var row in planned.Values)
            row.VarianceBdt = row.Category == "Income" ? row.ActualAmountBdt - row.PlannedAmountBdt :
                row.PlannedAmountBdt - row.ActualAmountBdt;
        var values = planned.Values.OrderBy(x => x.Category).ThenBy(x => x.AccountCode).ToArray();
        return ApiResponse<InstitutionBudgetActualDto>.SuccessResponse(new InstitutionBudgetActualDto {
            BudgetId = budget.Id, BudgetName = budget.Name, StartDate = budget.StartDate, EndDate = budget.EndDate,
            PlannedIncomeBdt = values.Where(x => x.Category == "Income").Sum(x => x.PlannedAmountBdt),
            ActualIncomeBdt = income.Data.TotalIncome,
            PlannedExpenseBdt = values.Where(x => x.Category == "Expense").Sum(x => x.PlannedAmountBdt),
            ActualExpenseBdt = income.Data.TotalExpenses, Lines = values });
    }
}
