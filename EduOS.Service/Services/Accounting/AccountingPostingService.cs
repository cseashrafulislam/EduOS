using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

// Institution journals are separate from Platform SaaS subscription billing.
public sealed partial class AccountingPostingService : IAccountingPostingService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public AccountingPostingService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }

    private bool CanRead => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    // Manual journals have no campus. Only institution-wide administrators can mutate them.
    private bool CanWrite => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal"));
    private IQueryable<Journal> Journals => db.Journals.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<JournalLine> Lines => db.JournalLines.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<AccountingPeriod> Periods => db.AccountingPeriods.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Institutional journal access denied.", 403);
    private static bool Version(byte[] actual, string? supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static string V(byte[] version) => Convert.ToBase64String(version);
    private static bool Money(decimal value) => value >= 0m && value <= 9999999999999999.99m &&
        decimal.Truncate(value * 100m) == value * 100m;
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static bool Manual(Journal j) => j.SourceType == "Manual" && j.SourceId == null &&
        j.ReversalOfJournalId == null && j.CampusId == null;
    private Task<AccountingPeriod?> OpenPeriodAsync(long id, DateOnly date, CancellationToken ct) =>
        Periods.FirstOrDefaultAsync(x => x.Id == id && !x.IsClosed && x.StartDate <= date && x.EndDate >= date, ct);
    private async Task<string?> ValidateLinesAsync(IReadOnlyList<JournalLine> rows, CancellationToken ct)
    {
        if (rows.Count < 2 || rows.Count > 100) return "Journal requires 2 to 100 lines.";
        if (rows.OrderBy(x => x.LineNo).Select((x, i) => x.LineNo == i + 1).Any(x => !x))
            return "Line numbers must be sequential from 1.";
        if (rows.Any(x => x.TenantId != user.TenantId || x.CampusId != null || x.AccountId <= 0 ||
            !Money(x.DebitAmount) || !Money(x.CreditAmount) ||
            (x.DebitAmount > 0m) == (x.CreditAmount > 0m)))
            return "Every line needs exactly one positive two-decimal debit or credit.";
        decimal debit, credit;
        try { debit = rows.Sum(x => x.DebitAmount); credit = rows.Sum(x => x.CreditAmount); }
        catch (OverflowException) { return "Journal amount exceeds decimal precision."; }
        if (debit <= 0m || debit != credit) return "Journal debits and credits must balance exactly.";
        var ids = rows.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && ids.Contains(x.Id) && x.IsActive && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT").Select(x => x.Id).ToListAsync(ct);
        return accounts.Count == ids.Length ? null : "All ledger accounts must be active BDT posting accounts.";
    }
}
