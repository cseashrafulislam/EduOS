using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Finance;
public sealed partial class StudentInvoicePostingService
{
    // Build balanced debits and credits from immutable invoice amounts; never from current fee structures.
    private async Task<(string? Error, List<JournalLine> Entries)> BuildEntriesAsync(
        StudentInvoice invoice, long campusId, long studentId, long programId, CancellationToken ct)
    {
        static (string?, List<JournalLine>) Fail(string message) => (message, new List<JournalLine>());
        var tenant = user.TenantId;
        var lines = await db.StudentInvoiceLines.AsNoTracking().Where(x => x.TenantId == tenant &&
            x.StudentInvoiceId == invoice.Id && !x.IsDeleted).OrderBy(x => x.Id).ToListAsync(ct);
        if (lines.Count is < 1 or > 49 || lines.Any(x => !Money(x.Amount) ||
            !Money(x.DiscountAmount) || !Money(x.FineAmount) || !Money(x.NetAmount) ||
            x.Amount - x.DiscountAmount + x.FineAmount != x.NetAmount ||
            x.DiscountAmount > x.Amount || x.FeeHeadId <= 0))
            return Fail("Invoice lines invalid or exceed 49.");
        decimal subtotal, discounts, fines, total;
        try { subtotal = lines.Sum(x => x.Amount); discounts = lines.Sum(x => x.DiscountAmount);
            fines = lines.Sum(x => x.FineAmount); total = lines.Sum(x => x.NetAmount); }
        catch (OverflowException) { return Fail("Amount exceeds precision."); }
        if (fines != 0m || invoice.FineAmount != 0m)
            return Fail("Fine income requires a separately configured GL mapping.");
        if (subtotal != invoice.Subtotal || discounts != invoice.DiscountAmount ||
            fines != invoice.FineAmount || total != invoice.TotalAmount)
            return Fail("Invoice header and lines do not reconcile.");
        var ids = lines.Select(x => x.FeeHeadId).Distinct().ToArray();
        var heads = await db.FeeHeads.AsNoTracking().Where(x => x.TenantId == tenant &&
            ids.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
        if (heads.Count != ids.Length || heads.Any(x => !x.IncomeAccountId.HasValue ||
            !x.ReceivableAccountId.HasValue))
            return Fail("Map fee head income and receivable accounts.");
        var accountIds = heads.SelectMany(x => new[] { x.IncomeAccountId!.Value,
            x.ReceivableAccountId!.Value }).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            accountIds.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
            x.AllowsPosting && x.CurrencyCode == "BDT").ToListAsync(ct);
        var byAccount = accounts.ToDictionary(x => x.Id);
        if (heads.Any(x => !byAccount.TryGetValue(x.IncomeAccountId!.Value, out var income) ||
            income.AccountType != AccountType.Income ||
            !byAccount.TryGetValue(x.ReceivableAccountId!.Value, out var ar) ||
            ar.AccountType != AccountType.Asset))
            return Fail("Fee accounts must be active BDT income/asset ledgers.");
        var byHead = heads.ToDictionary(x => x.Id);
        var debits = lines.Where(x => x.NetAmount > 0m)
            .GroupBy(x => byHead[x.FeeHeadId].ReceivableAccountId!.Value)
            .Select(g => (AccountId: g.Key, Amount: g.Sum(x => x.NetAmount))).ToArray();
        var credits = lines.Where(x => x.NetAmount > 0m).GroupBy(x => x.FeeHeadId)
            .Select(g => (FeeHeadId: g.Key, AccountId: byHead[g.Key].IncomeAccountId!.Value,
                Amount: g.Sum(x => x.NetAmount))).ToArray();
        if (debits.Length + credits.Length > 100 || debits.Sum(x => x.Amount) != total ||
            credits.Sum(x => x.Amount) != total)
            return Fail("Journal is not balanced.");
        var entries = new List<JournalLine>();
        var n = 0;
        foreach (var item in debits)
            entries.Add(new JournalLine { TenantId = tenant, LineNo = ++n,
                AccountId = item.AccountId, CampusId = campusId, StudentId = studentId,
                AcademicProgramId = programId, DebitAmount = item.Amount,
                Description = "Student fee receivable" });
        foreach (var item in credits)
            entries.Add(new JournalLine { TenantId = tenant, LineNo = ++n,
                AccountId = item.AccountId, CampusId = campusId, StudentId = studentId,
                AcademicProgramId = programId, FeeHeadId = item.FeeHeadId,
                CreditAmount = item.Amount, Description = "Student fee income" });
        return (null, entries);
    }
}
