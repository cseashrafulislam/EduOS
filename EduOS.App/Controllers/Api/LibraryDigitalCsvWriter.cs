using EduOS.Core.DTOs.Library;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Bounded metadata-only export; never includes file keys, global Person IDs or borrower data.</summary>
public static class LibraryDigitalCsvWriter
{
    public static byte[] Write(IEnumerable<LibraryDigitalResourceDto> rows, bool bangla)
    {
        var csv = new StringBuilder();
        csv.AppendLine(bangla
            ? "বই,রিসোর্স,ফাইল,ধরন,বাইট,অবস্থা,প্রকাশের সময়"
            : "Book,Resource,File,Content type,Bytes,Status,Published UTC");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", new[]
            {
                Cell(row.BookTitle), Cell(row.Title), Cell(row.FileName),
                Cell(row.ContentType), Cell(row.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.IsActive ? "Active" : "Revoked"), Cell(row.CreatedAt.ToString("O"))
            }));
        var payload = new UTF8Encoding(false).GetBytes(csv.ToString());
        var bom = new UTF8Encoding(true).GetPreamble();
        var result = new byte[bom.Length + payload.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(payload, 0, result, bom.Length, payload.Length);
        return result;
    }

    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        var first = text.TrimStart(' ', '\t', '\r', '\n');
        if (first.Length > 0 && "=+-@".Contains(first[0]))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
