using EduOS.Core.DTOs.Library;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Bounded, formula-safe, borrower-free export of one authorized integrity report page.</summary>
public static class LibraryCopyIntegrityCsvWriter
{
    public static byte[] Write(IReadOnlyList<LibraryCopyIntegrityDto> rows, bool bangla)
    {
        if (rows.Count > 100) throw new ArgumentOutOfRangeException(nameof(rows));
        var csv = new StringBuilder();
        Append(csv, bangla
            ? ["অ্যাকসেশন", "বই", "ক্যাম্পাস", "শাখা", "কপির অবস্থা", "চলমান ইস্যু", "বই অনুপস্থিত", "শাখা অনুপস্থিত", "অবস্থার অমিল", "একাধিক ইস্যু"]
            : ["Accession", "Book", "Campus", "Branch", "Copy state", "Open loans", "Missing book", "Missing branch", "State mismatch", "Multiple open loans"]);
        foreach (var row in rows)
            Append(csv, [
                row.AccessionNumber, row.BookTitle,
                row.CampusId?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.BranchCode ?? "", row.CopyState.ToString(),
                row.OpenLoanCount.ToString(CultureInfo.InvariantCulture),
                Flag(row.MissingBook), Flag(row.MissingBranch),
                Flag(row.StateMismatch), Flag(row.MultipleOpenLoans)
            ]);
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static string Flag(bool value) => value ? "Yes" : "No";

    private static void Append(StringBuilder csv, IEnumerable<string> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first) csv.Append(',');
            first = false;
            csv.Append(PrivateCsvCell.Escape(field));
        }
        csv.Append("\r\n");
    }
}
