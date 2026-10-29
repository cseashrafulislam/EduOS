using EduOS.Core.DTOs.Library;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Bounded, formula-safe CSV for an already-authorized overdue report page.</summary>
public static class LibraryOverdueCsvWriter
{
    public static byte[] Write(IReadOnlyList<LibraryOverdueBranchDto> rows, bool bangla)
    {
        if (rows.Count > 100) throw new ArgumentOutOfRangeException(nameof(rows));
        var output = new StringBuilder();
        var header = bangla
            ? new[] { "ক্যাম্পাস", "শাখা কোড", "লাইব্রেরি শাখা", "মোট মেয়াদোত্তীর্ণ", "১–৭ দিন", "৮–৩০ দিন", "৩১+ দিন", "প্রাচীনতম ফেরতের তারিখ" }
            : new[] { "Campus", "Branch code", "Library branch", "Total overdue", "1-7 days", "8-30 days", "31+ days", "Oldest due date" };
        Append(output, header);
        foreach (var row in rows)
            Append(output, new[]
            {
                row.CampusId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.BranchCode, row.BranchName,
                row.TotalOverdue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Days1To7.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Days8To30.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Days31Plus.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.OldestDueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            });
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(output.ToString());
        var bytes = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);
        return bytes;
    }

    private static void Append(StringBuilder output, IEnumerable<string> fields)
    {
        var first = true;
        foreach (var value in fields)
        {
            if (!first) output.Append(',');
            first = false;
            var text = value ?? string.Empty;
            if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'))
                text = "'" + text;
            output.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        }
        output.Append("\r\n");
    }
}
