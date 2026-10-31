using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Finance-owned verification of an independently posted, reference-bound manual
/// journal. This does not create an invoice, credit note, payment or refund.
/// </summary>
public static class ReservationPeriodSettlement
{
    public static bool ValidAmount(decimal amount) =>
        amount >= -999999999999.99m && amount <= 999999999999.99m &&
        decimal.Round(amount, 2) == amount;

    public static bool MatchesSignedStudentLeg(decimal amount,
        IEnumerable<(long? StudentId, decimal Debit, decimal Credit)> lines,
        long studentId)
    {
        if (!ValidAmount(amount) || amount == 0m || studentId <= 0) return false;
        var values = lines.ToList();
        var expected = decimal.Abs(amount);
        return values.Count is >= 2 and <= 100 &&
            values.All(x => x.Debit >= 0m && x.Credit >= 0m &&
                (x.Debit == 0m || x.Credit == 0m) &&
                (!x.StudentId.HasValue || x.StudentId == studentId)) &&
            values.Sum(x => x.Debit) == expected && values.Sum(x => x.Credit) == expected &&
            values.Any(x => x.StudentId == studentId &&
                (amount > 0m ? x.Debit == expected && x.Credit == 0m :
                    x.Credit == expected && x.Debit == 0m));
    }

    public static async Task<bool> HasPostedMatchingJournalAsync(EduOSDbContext db,
        long tenant, long campusId, long studentId, decimal amount,
        long? journalId, string reference, CancellationToken ct)
    {
        if (!journalId.HasValue || journalId.Value <= 0 || !ValidAmount(amount) || amount == 0m ||
            db.CurrentTenantId != tenant) return false;
        var journal = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == journalId && !x.IsDeleted &&
            x.SourceType == "Manual" && x.State == JournalState.Posted &&
            x.CurrencyCode == "BDT" && x.CampusId == campusId &&
            x.ReferenceNumber == reference && x.ApprovedByUserId.HasValue &&
            x.ApprovedByUserId != x.CreatedBy && x.PostedByUserId.HasValue, ct);
        if (journal == null || await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.State == JournalState.Posted &&
                x.ReversalOfJournalId == journal.Id, ct)) return false;
        var lines = await db.JournalLines.AsNoTracking().Where(x =>
            x.TenantId == tenant && x.JournalId == journal.Id && !x.IsDeleted)
            .Select(x => new { x.StudentId, x.AccountId, x.DebitAmount, x.CreditAmount })
            .Take(101).ToListAsync(ct);
        if (!MatchesSignedStudentLeg(amount,
            lines.Select(x => (x.StudentId, x.DebitAmount, x.CreditAmount)), studentId))
            return false;
        var expected = decimal.Abs(amount);
        var studentAccountIds = lines.Where(x => x.StudentId == studentId &&
                (amount > 0m ? x.DebitAmount == expected : x.CreditAmount == expected))
            .Select(x => x.AccountId).Distinct().ToArray();
        return await db.Accounts.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && studentAccountIds.Contains(x.Id) && !x.IsDeleted &&
            x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT" &&
            x.AccountType == AccountType.Asset, ct);
    }
}
