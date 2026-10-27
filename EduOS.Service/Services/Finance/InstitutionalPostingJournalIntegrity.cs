using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;

namespace EduOS.Service.Services.Finance;

/// <summary>Validates the entire historical institutional posting before replay or readback.
/// Callers separately reject any posted reversal, including a legacy soft-deleted reversal.</summary>
public static class InstitutionalPostingJournalIntegrity
{
    public static bool Matches(Journal journal, IReadOnlyCollection<JournalLine> lines,
        long tenantId, string sourceType, long sourceId, long campusId, long studentId,
        decimal amount, long? cashAccountId = null)
    {
        if (journal.TenantId != tenantId || journal.IsDeleted ||
            journal.SourceType != sourceType || journal.SourceId != sourceId ||
            journal.CampusId != campusId || journal.CurrencyCode != "BDT" ||
            journal.State != JournalState.Posted || journal.ReversalOfJournalId.HasValue ||
            !Money(amount) || amount <= 0m || lines.Count is < 2 or > 100)
            return false;
        if (lines.Any(line => line.TenantId != tenantId || line.IsDeleted ||
            line.JournalId != journal.Id || line.CampusId != campusId ||
            line.StudentId != studentId || line.AccountId <= 0 || line.LineNo <= 0 ||
            !Money(line.DebitAmount) || !Money(line.CreditAmount) ||
            (line.DebitAmount > 0m) == (line.CreditAmount > 0m)) ||
            lines.Select(line => line.LineNo).Distinct().Count() != lines.Count)
            return false;
        try
        {
            if (lines.Sum(line => line.DebitAmount) != amount ||
                lines.Sum(line => line.CreditAmount) != amount) return false;
        }
        catch (OverflowException) { return false; }
        if (cashAccountId.HasValue)
        {
            var debits = lines.Where(line => line.DebitAmount > 0m).ToArray();
            if (debits.Length != 1 || debits[0].AccountId != cashAccountId.Value ||
                debits[0].DebitAmount != amount) return false;
        }
        return true;
    }
    private static bool Money(decimal amount) => amount >= 0m &&
        amount <= 9999999999999999.99m && decimal.Round(amount, 2) == amount;
}
