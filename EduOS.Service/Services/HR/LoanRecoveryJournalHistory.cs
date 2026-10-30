using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.HR;

/// <summary>
/// A posted payroll contra compensates its original loan recovery without rewriting the
/// historical recovery or journal. Only the posted, tenant-matched contra is authoritative.
/// </summary>
internal static class LoanRecoveryJournalHistory
{
    internal sealed record Entry(LoanAdvanceRecovery Recovery, Journal Source, bool Compensated);

    internal static async Task<(string? Error, List<Entry> Entries)> ReadAsync(
        EduOSDbContext db, long tenant, IReadOnlyCollection<LoanAdvanceRecovery> recoveries,
        CancellationToken ct)
    {
        var entries = new List<Entry>();
        if (recoveries.Count == 0) return (null, entries);
        if (recoveries.Any(x => x.TenantId != tenant || x.IsDeleted || x.Amount <= 0m ||
            decimal.Round(x.Amount, 2) != x.Amount || !x.JournalId.HasValue) ||
            recoveries.GroupBy(x => x.ClientRequestId).Any(x => x.Count() != 1) ||
            recoveries.Where(x => x.PayrollEmployeeId.HasValue)
                .GroupBy(x => new { x.JournalId, x.LoanAdvanceId }).Any(x => x.Count() != 1))
            return ("Loan recovery history contains invalid or deleted entries.", entries);
        var journalIds = recoveries.Select(x => x.JournalId!.Value).Distinct().ToArray();
        var sources = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && journalIds.Contains(x.Id)).ToListAsync(ct);
        var compensations = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ReversalOfJournalId.HasValue &&
                journalIds.Contains(x.ReversalOfJournalId.Value) &&
                (x.State == JournalState.Posted || x.State == JournalState.Reversed))
            .ToListAsync(ct);
        foreach (var recovery in recoveries)
        {
            var source = sources.SingleOrDefault(x => x.Id == recovery.JournalId);
            if (source == null || source.IsDeleted || source.State != JournalState.Posted ||
                source.ReversalOfJournalId.HasValue ||
                source.JournalDate != recovery.RecoveryDate ||
                (recovery.PayrollEmployeeId.HasValue ? source.SourceType != "PayrollRun" :
                    source.SourceType != "LoanAdvanceRecovery" || source.SourceId != recovery.Id))
                return ("Loan recovery source journal requires reconciliation.", entries);
            var reversals = compensations.Where(x => x.ReversalOfJournalId == source.Id).ToArray();
            if (reversals.Length > 1 || reversals.Any(x => x.IsDeleted ||
                x.State != JournalState.Posted || !recovery.PayrollEmployeeId.HasValue ||
                x.SourceType != "PayrollRunReversal" || x.SourceId != source.SourceId ||
                x.CampusId != source.CampusId || x.CurrencyCode != source.CurrencyCode ||
                x.JournalDate < source.JournalDate))
                return ("Loan recovery compensation journal requires reconciliation.", entries);
            entries.Add(new Entry(recovery, source, reversals.Length == 1));
        }
        return (null, entries);
    }

    internal static decimal NetRecovered(IEnumerable<Entry> history) =>
        history.Where(x => !x.Compensated).Sum(x => x.Recovery.Amount);
}
