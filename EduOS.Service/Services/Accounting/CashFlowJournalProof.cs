using EduOS.Core.Entities.Accounting;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Accounting;

internal static class CashFlowJournalProof
{
    // Binds a manual allocation to the immutable posting AND every original
    // journal line. A back-dated edit invalidates approval instead of silently
    // reinterpreting historical cash flow.
    internal static string Fingerprint(Journal journal, IEnumerable<JournalLine> lines)
    {
        var builder = new StringBuilder();
        builder.Append(journal.Id).Append('|').Append(journal.JournalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(journal.JournalNumber).Append('|').Append(journal.CampusId)
            .Append('|').Append(journal.CurrencyCode).Append('|').Append((int)journal.State);
        foreach (var line in lines.OrderBy(x => x.LineNo).ThenBy(x => x.Id))
        {
            builder.Append('\n').Append(line.Id).Append('|').Append(line.LineNo).Append('|')
                .Append(line.AccountId).Append('|').Append(line.CampusId).Append('|')
                .Append(line.DebitAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                .Append(line.CreditAmount.ToString("0.00", CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
