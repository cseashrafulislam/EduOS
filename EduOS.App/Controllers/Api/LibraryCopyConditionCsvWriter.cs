using EduOS.Core.DTOs.Library;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Bounded, staff-authorized physical copy page; never includes borrower details.</summary>
public static class LibraryCopyConditionCsvWriter
{
    public static byte[] Write(IEnumerable<LibraryCopyConditionDto> rows, bool bangla)
    {
        var csv = new StringBuilder();
        csv.AppendLine(bangla
            ? "অ্যাকসেশন,বই,ক্যাম্পাস,শাখা,অবস্থা,চলমান ঋণ"
            : "Accession,Book,Campus,Branch,Condition,Open loans");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", new[]
            {
                Cell(row.AccessionNumber), Cell(row.BookTitle),
                Cell(row.CampusId?.ToString() ?? ""), Cell(row.BranchCode),
                Cell(row.State.ToString()), Cell(row.OpenIssueCount.ToString())
            }));
        return new UTF8Encoding(true).GetPreamble().Concat(
            new UTF8Encoding(false).GetBytes(csv.ToString())).ToArray();
    }
    private static string Cell(string? value)
    {
        var text = value ?? "";
        if ("=+-@".Contains(text.TrimStart(' ', '\t', '\r', '\n').FirstOrDefault()) &&
            !string.IsNullOrWhiteSpace(text))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
