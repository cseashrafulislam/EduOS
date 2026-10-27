using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Read-only reconciliation of immutable institutional refund contra-journals.
/// Query filters are deliberately bypassed for historical evidence, but every
/// database query remains explicitly tenant-scoped.
/// </summary>
internal static class InstitutionalRefundJournalIntegrity
{
    internal sealed class Evidence(
        IReadOnlyDictionary<long, Journal> journals,
        ILookup<long, JournalLine> lines,
        IReadOnlySet<long> reversed,
        IReadOnlyDictionary<long, int> owners)
    {
        public bool Matches(Refund refund, long tenantId, long campusId, long studentId)
        {
            if (refund.State != RefundState.Paid || refund.IsDeleted ||
                !refund.ReversalJournalId.HasValue ||
                !owners.TryGetValue(refund.ReversalJournalId.Value, out var count) || count != 1)
                return false;
            var id = refund.ReversalJournalId.Value;
            return journals.TryGetValue(id, out var journal) &&
                InstitutionalRefundJournalIntegrity.Matches(journal, lines[id].ToArray(),
                    tenantId, refund.Id, campusId, studentId, refund.Amount,
                    reversed.Contains(id));
        }
    }

    public static async Task<Evidence?> LoadAsync(EduOSDbContext db, long tenantId,
        IReadOnlyCollection<Refund> refunds, CancellationToken ct)
    {
        var ids = refunds.Where(x => x.State == RefundState.Paid &&
                x.ReversalJournalId.HasValue)
            .Select(x => x.ReversalJournalId!.Value).Distinct().ToArray();
        var journals = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.JournalId))
            .Take(20001).ToListAsync(ct);
        if (lines.Count > 20000) return null;
        var reversed = (await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.State == JournalState.Posted &&
                x.ReversalOfJournalId.HasValue && ids.Contains(x.ReversalOfJournalId.Value))
            .Select(x => x.ReversalOfJournalId!.Value).ToListAsync(ct)).ToHashSet();
        var owners = await db.Refunds.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ReversalJournalId.HasValue &&
                ids.Contains(x.ReversalJournalId.Value))
            .GroupBy(x => x.ReversalJournalId!.Value)
            .Select(x => new { JournalId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.JournalId, x => x.Count, ct);
        return new Evidence(journals, lines.ToLookup(x => x.JournalId), reversed, owners);
    }

    private static bool Matches(Journal journal, IReadOnlyCollection<JournalLine> lines,
        long tenantId, long refundId, long campusId, long studentId, decimal amount,
        bool hasPostedReversal)
    {
        if (journal.TenantId != tenantId || journal.IsDeleted ||
            journal.State != JournalState.Posted ||
            journal.SourceType != "StudentRefund" || journal.SourceId != refundId ||
            journal.CampusId != campusId || journal.CurrencyCode != "BDT" ||
            journal.ReversalOfJournalId.HasValue || hasPostedReversal ||
            amount <= 0m || decimal.Round(amount, 2) != amount ||
            lines.Count is < 2 or > 100)
            return false;

        var debits = lines.Where(x => x.DebitAmount > 0m).ToArray();
        var credits = lines.Where(x => x.CreditAmount > 0m).ToArray();
        if (debits.Length == 0 || credits.Length != 1 ||
            debits.Length + credits.Length != lines.Count ||
            credits[0].CreditAmount != amount ||
            debits.Sum(x => x.DebitAmount) != amount ||
            debits.Select(x => x.AccountId).Distinct().Count() != debits.Length ||
            debits.Any(x => x.AccountId == credits[0].AccountId) ||
            lines.Select(x => x.LineNo).Distinct().Count() != lines.Count)
            return false;

        return lines.All(x => x.TenantId == tenantId && x.JournalId == journal.Id &&
            !x.IsDeleted && x.LineNo > 0 && x.AccountId > 0 &&
            x.CampusId == campusId && x.StudentId == studentId &&
            x.DebitAmount >= 0m && x.CreditAmount >= 0m &&
            decimal.Round(x.DebitAmount, 2) == x.DebitAmount &&
            decimal.Round(x.CreditAmount, 2) == x.CreditAmount);
    }
}
