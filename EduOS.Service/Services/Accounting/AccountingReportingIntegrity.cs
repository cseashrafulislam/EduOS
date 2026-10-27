using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Include legacy soft-deleted posting evidence without exposing another tenant's history.
    // Institution-wide reports scan all posted records; account reports restrict to
    // historical lines (including tombstones) belonging to their selected ledgers.
    private async Task<string?> HistoricalPostingErrorAsync(
        DateOnly? fromDate, DateOnly toDate, long[]? accountIds, CancellationToken ct)
    {
        var journals = db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.TenantId == user.TenantId && j.JournalDate <= toDate &&
                (j.State == JournalState.Posted || j.State == JournalState.Reversed));
        if (fromDate.HasValue)
        {
            var start = fromDate.Value;
            journals = journals.Where(j => j.JournalDate >= start);
        }
        if (accountIds != null)
        {
            if (accountIds.Length == 0) return null;
            journals = journals.Where(j => db.JournalLines.IgnoreQueryFilters().Any(l =>
                l.TenantId == user.TenantId && l.JournalId == j.Id &&
                accountIds.Contains(l.AccountId)));
        }
        if (await journals.AnyAsync(j => j.IsDeleted, ct))
            return "Soft-deleted posted journals require historical ledger reconciliation.";
        if (await db.JournalLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(l =>
            l.TenantId == user.TenantId && l.IsDeleted &&
            journals.Any(j => j.Id == l.JournalId), ct))
            return "Soft-deleted posted journal lines require historical ledger reconciliation.";
        return null;
    }
}
