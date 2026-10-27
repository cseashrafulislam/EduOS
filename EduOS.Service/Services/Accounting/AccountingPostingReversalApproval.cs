using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingPostingService
{
    // Existing manual reversal requests are submitted, never self-approved or directly posted.
    // Historical posted reversals are left unchanged. A rejected proposal may be replaced.
    private static bool ManualReversal(Journal journal) =>
        journal.SourceType == "ManualReversal" &&
        journal.SourceId.HasValue &&
        journal.ReversalOfJournalId == journal.SourceId &&
        journal.CampusId == null;

    private async Task<string?> ValidateManualReversalAsync(Journal reversal, CancellationToken ct)
    {
        if (!ManualReversal(reversal) || reversal.CurrencyCode != "BDT")
            return "Manual reversal provenance is invalid.";

        var original = await Journals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == reversal.SourceId!.Value, ct);
        if (original == null || !Manual(original) || original.State != JournalState.Posted ||
            original.CurrencyCode != "BDT" || reversal.JournalDate < original.JournalDate)
            return "Original posted manual journal is missing or invalid.";

        // Recheck under the caller's serializable transaction at approval and posting.
        if (await Journals.AsNoTracking().AnyAsync(x =>
            x.Id != reversal.Id && x.ReversalOfJournalId == original.Id &&
            x.State != JournalState.Rejected && x.State != JournalState.Cancelled, ct))
            return "Another active reversal already exists.";

        var sourceLines = await Lines.AsNoTracking().Where(x => x.JournalId == original.Id)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        var reversalLines = await Lines.AsNoTracking().Where(x => x.JournalId == reversal.Id)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        if (sourceLines.Count < 2 || sourceLines.Count != reversalLines.Count)
            return "Reversal line count does not match the original journal.";

        for (var i = 0; i < sourceLines.Count; i++)
        {
            var source = sourceLines[i];
            var line = reversalLines[i];
            if (line.TenantId != user.TenantId || source.TenantId != user.TenantId ||
                line.LineNo != source.LineNo || line.AccountId != source.AccountId ||
                line.CampusId != source.CampusId || line.StudentId != source.StudentId ||
                line.EmployeeId != source.EmployeeId ||
                line.AcademicProgramId != source.AcademicProgramId ||
                line.FeeHeadId != source.FeeHeadId ||
                line.DebitAmount != source.CreditAmount ||
                line.CreditAmount != source.DebitAmount ||
                line.Description != source.Description)
                return "Reversal lines must exactly negate the immutable source journal.";
        }

        return null;
    }
}
