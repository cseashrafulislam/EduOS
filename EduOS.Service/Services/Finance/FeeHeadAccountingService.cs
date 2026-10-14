using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Tenant-wide institutional fee-head and COA mapping setup; never posts journals
/// and never touches Platform SaaS subscription billing.
/// </summary>
public sealed class FeeHeadAccountingService : IFeeHeadAccountingService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public FeeHeadAccountingService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }

    private bool Readable => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool Writable => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal"));
    private IQueryable<FeeHead> Heads => db.FeeHeads.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<Account> Accounts => db.Accounts.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static ApiResponse<T> Error<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
    private static string V(byte[] value) => Convert.ToBase64String(value);
    private static bool Version(byte[] actual, string? supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static bool Eligible(Account x, AccountType type) =>
        x.IsActive && !x.IsGroup && x.AllowsPosting && x.AccountType == type && x.CurrencyCode == "BDT";
    private async Task<bool> HasInvoicesAsync(long headId, CancellationToken ct) =>
        await db.StudentInvoiceLines.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.FeeHeadId == headId, ct);
    private async Task<bool> MappingLockedAsync(long headId, CancellationToken ct) =>
        await db.JournalLines.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.FeeHeadId == headId &&
            db.Journals.Any(j => j.TenantId == user.TenantId && !j.IsDeleted &&
                j.Id == x.JournalId && (j.State == JournalState.Posted || j.State == JournalState.Reversed)), ct);
    private static FeeHeadAccountingDto Map(FeeHead h, bool history, bool ready) => new()
    {
        Id = h.Id, Code = h.Code, Name = h.Name, DefaultFrequency = h.DefaultFrequency,
        IncomeAccountId = h.IncomeAccountId, ReceivableAccountId = h.ReceivableAccountId,
        IsRefundable = h.IsRefundable, IsActive = h.IsActive, HasInvoiceHistory = history,
        ReadyForPosting = ready, RowVersion = V(h.RowVersion)
    };
    private async Task<FeeHeadAccountingDto> MapAsync(FeeHead h, CancellationToken ct)
    {
        var accounts = await Accounts.AsNoTracking().Where(x =>
            x.Id == h.IncomeAccountId || x.Id == h.ReceivableAccountId).ToListAsync(ct);
        return Map(h, await HasInvoicesAsync(h.Id, ct),
            h.IncomeAccountId.HasValue && h.ReceivableAccountId.HasValue &&
            accounts.Any(x => x.Id == h.IncomeAccountId && Eligible(x, AccountType.Income)) &&
            accounts.Any(x => x.Id == h.ReceivableAccountId && Eligible(x, AccountType.Asset)));
    }
    public async Task<ApiResponse<FeeHeadAccountingSetupDto>> GetSetupAsync(CancellationToken ct = default)
    {
        if (!Readable) return Error<FeeHeadAccountingSetupDto>("Institutional finance access denied.", 403);
        var heads = await Heads.AsNoTracking().OrderBy(x => x.Code).ThenBy(x => x.Id).Take(501).ToListAsync(ct);
        var accounts = await Accounts.AsNoTracking().Where(x => x.IsActive && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT" && (x.AccountType == AccountType.Income || x.AccountType == AccountType.Asset))
            .OrderBy(x => x.Code).ThenBy(x => x.Id).Take(501).ToListAsync(ct);
        if (heads.Count > 500 || accounts.Count > 500)
            return Error<FeeHeadAccountingSetupDto>("Setup exceeds 500 records; paged administration is required.", 409);
        var ids = heads.Select(x => x.Id).ToArray();
        var history = await db.StudentInvoiceLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && ids.Contains(x.FeeHeadId)).Select(x => x.FeeHeadId).Distinct().ToListAsync(ct);
        var billed = history.ToHashSet();
        var accountById = accounts.ToDictionary(x => x.Id);
        var result = new FeeHeadAccountingSetupDto
        {
            FeeHeads = heads.Select(h => Map(h, billed.Contains(h.Id),
                h.IncomeAccountId.HasValue && h.ReceivableAccountId.HasValue &&
                accountById.TryGetValue(h.IncomeAccountId.Value, out var income) &&
                Eligible(income, AccountType.Income) &&
                accountById.TryGetValue(h.ReceivableAccountId.Value, out var receivable) &&
                Eligible(receivable, AccountType.Asset))).ToArray(),
            PostingAccounts = accounts.Select(x => new FeeHeadAccountOptionDto
                { Id = x.Id, Code = x.Code, Name = x.Name, AccountType = x.AccountType }).ToArray()
        };
        return ApiResponse<FeeHeadAccountingSetupDto>.SuccessResponse(result);
    }
    public async Task<ApiResponse<FeeHeadAccountingDto>> SaveHeadAsync(
        long? headId, SaveFeeHeadAccountingHeadRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Error<FeeHeadAccountingDto>("Institution administrator permission required.", 403);
        if (request == null || headId is <= 0 || string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name) || request.Code.Trim().Length > 50 ||
            request.Name.Trim().Length > 100 || !Enum.IsDefined(request.DefaultFrequency))
            return Error<FeeHeadAccountingDto>("Valid fee head code, name and frequency required.");
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var existing = await Heads.FirstOrDefaultAsync(x => x.Code == code, ct);
            if (!headId.HasValue && existing != null)
            {
                if (existing.Name == name && existing.DefaultFrequency == request.DefaultFrequency &&
                    existing.IsRefundable == request.IsRefundable && existing.IsActive == request.IsActive)
                    return ApiResponse<FeeHeadAccountingDto>.SuccessResponse(await MapAsync(existing, ct), "Existing fee head returned.");
                return Error<FeeHeadAccountingDto>("Fee head code already exists with different settings.", 409);
            }
            var row = headId.HasValue ? await Heads.FirstOrDefaultAsync(x => x.Id == headId.Value, ct) : null;
            if (headId.HasValue && row == null) return Error<FeeHeadAccountingDto>("Fee head not found.", 404);
            if (row != null && !Version(row.RowVersion, request.RowVersion))
                return Error<FeeHeadAccountingDto>("Fee head changed; reload.", 409);
            if (existing != null && existing.Id != row?.Id)
                return Error<FeeHeadAccountingDto>("Fee head code already exists.", 409);
            if (row != null && (row.Code != code || row.DefaultFrequency != request.DefaultFrequency) &&
                (await HasInvoicesAsync(row.Id, ct) ||
                 await db.FeeStructureLines.AsNoTracking().AnyAsync(x =>
                     x.TenantId == user.TenantId && !x.IsDeleted && x.FeeHeadId == row.Id, ct)))
                return Error<FeeHeadAccountingDto>("Referenced fee head code/frequency cannot change.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            if (row == null)
            {
                row = new FeeHead { TenantId = user.TenantId, CreatedAt = now, CreatedBy = user.UserId };
                db.FeeHeads.Add(row);
            }
            else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
            row.Code = code; row.Name = name; row.DefaultFrequency = request.DefaultFrequency;
            row.IsRefundable = request.IsRefundable; row.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FeeHeadAccountingDto>.SuccessResponse(await MapAsync(row, ct), "Fee head saved.");
        }
        catch (DbUpdateConcurrencyException) { return Error<FeeHeadAccountingDto>("Fee head changed; reload.", 409); }
        catch (DbUpdateException) { return Error<FeeHeadAccountingDto>("Conflicting fee head update.", 409); }
    }
    public async Task<ApiResponse<FeeHeadAccountingDto>> MapAccountsAsync(
        long headId, SaveFeeHeadAccountMappingRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Error<FeeHeadAccountingDto>("Institution administrator permission required.", 403);
        if (headId <= 0 || request == null || request.IncomeAccountId <= 0 ||
            request.ReceivableAccountId <= 0 || request.IncomeAccountId == request.ReceivableAccountId)
            return Error<FeeHeadAccountingDto>("Distinct income and receivable posting accounts are required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var row = await Heads.FirstOrDefaultAsync(x => x.Id == headId, ct);
            if (row == null) return Error<FeeHeadAccountingDto>("Fee head not found.", 404);
            if (!Version(row.RowVersion, request.RowVersion))
                return Error<FeeHeadAccountingDto>("Fee head changed; reload.", 409);
            var accounts = await Accounts.AsNoTracking().Where(x =>
                x.Id == request.IncomeAccountId || x.Id == request.ReceivableAccountId).ToListAsync(ct);
            if (!accounts.Any(x => x.Id == request.IncomeAccountId && Eligible(x, AccountType.Income)) ||
                !accounts.Any(x => x.Id == request.ReceivableAccountId && Eligible(x, AccountType.Asset)))
                return Error<FeeHeadAccountingDto>("Select active BDT income and asset posting accounts in this tenant.", 409);
            var changing = row.IncomeAccountId != request.IncomeAccountId ||
                row.ReceivableAccountId != request.ReceivableAccountId;
            if (changing && (await MappingLockedAsync(row.Id, ct) ||
                ((row.IncomeAccountId.HasValue || row.ReceivableAccountId.HasValue) &&
                 await HasInvoicesAsync(row.Id, ct))))
                return Error<FeeHeadAccountingDto>("Historical billing/journals lock existing mappings; create a new fee head for future use.", 409);
            if (changing)
            {
                row.IncomeAccountId = request.IncomeAccountId;
                row.ReceivableAccountId = request.ReceivableAccountId;
                row.UpdatedAt = clock.GetUtcNow().UtcDateTime; row.UpdatedBy = user.UserId;
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return ApiResponse<FeeHeadAccountingDto>.SuccessResponse(await MapAsync(row, ct),
                changing ? "Institutional GL mapping saved; automatic journal posting remains disabled." : "Existing mapping returned.");
        }
        catch (DbUpdateConcurrencyException) { return Error<FeeHeadAccountingDto>("Fee head changed; reload.", 409); }
        catch (DbUpdateException) { return Error<FeeHeadAccountingDto>("Conflicting mapping update.", 409); }
    }
}
