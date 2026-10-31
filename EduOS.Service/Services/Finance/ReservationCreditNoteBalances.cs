using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>Live posted credits sourced only from immutable journals, not draft clearances.</summary>
public static class ReservationCreditNoteBalances
{
    public sealed class Balance
    {
        public long InvoiceId { get; set; }
        public decimal UnpaidReduction { get; set; }
        public decimal StudentCreditPayable { get; set; }
    }

    public static IQueryable<Balance> Posted(EduOSDbContext db, long tenantId) =>
        from journal in db.Journals.IgnoreQueryFilters().AsNoTracking()
        join line in db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            on new { journal.TenantId, Id = journal.Id }
            equals new { line.TenantId, Id = line.JournalId }
        join account in db.Accounts.IgnoreQueryFilters().AsNoTracking()
            on new { line.TenantId, Id = line.AccountId }
            equals new { account.TenantId, account.Id }
        where journal.TenantId == tenantId && journal.SourceType == ReservationAutomaticCreditNote.SourceType &&
            journal.SourceId.HasValue && journal.State == JournalState.Posted && !journal.IsDeleted &&
            !line.IsDeleted && !account.IsDeleted && line.CreditAmount > 0m &&
            !db.Journals.IgnoreQueryFilters().Any(reversal => reversal.TenantId == tenantId &&
                reversal.ReversalOfJournalId == journal.Id && reversal.State == JournalState.Posted)
        group new { line.CreditAmount, account.AccountType } by journal.SourceId!.Value into g
        select new Balance
        {
            InvoiceId = g.Key,
            UnpaidReduction = g.Sum(x => x.AccountType == AccountType.Asset ? x.CreditAmount : 0m),
            StudentCreditPayable = g.Sum(x => x.AccountType == AccountType.Liability ? x.CreditAmount : 0m)
        };

    public static async Task<IReadOnlyDictionary<long, Balance>> ForInvoicesAsync(
        EduOSDbContext db, long tenantId, IReadOnlyCollection<long> invoiceIds, CancellationToken ct)
    {
        if (db.CurrentTenantId != tenantId || invoiceIds.Count > 5000)
            throw new InvalidOperationException("Finance invoice scope exceeds safe bounds.");
        return await Posted(db, tenantId).Where(x => invoiceIds.Contains(x.InvoiceId))
            .ToDictionaryAsync(x => x.InvoiceId, ct);
    }
}
