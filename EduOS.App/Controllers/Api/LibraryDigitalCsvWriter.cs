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
            ? "বই,রিসোর্স,ফাইল,ধরন,বাইট,অবস্থা,শেষের তারিখ (UTC),প্রকাশের সময়,ক্যাম্পাস"
            : "Book,Resource,File,Content type,Bytes,Status,Expires UTC,Published UTC,Campus");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", new[]
            {
                Cell(row.BookTitle), Cell(row.Title), Cell(row.FileName),
                Cell(row.ContentType), Cell(row.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.IsActive ? "Active" : "Revoked"),
                Cell(row.ExpiresOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.CreatedAt.ToString("O")),
                Cell(row.CampusId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ??
                    (bangla ? "সব ক্যাম্পাস" : "All campuses"))
            }));
        var payload = new UTF8Encoding(false).GetBytes(csv.ToString());
        var bom = new UTF8Encoding(true).GetPreamble();
        var result = new byte[bom.Length + payload.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(payload, 0, result, bom.Length, payload.Length);
        return result;
    }

    public static byte[] WriteAudit(IEnumerable<LibraryDigitalAuditDto> rows, bool bangla)
    {
        var csv = new StringBuilder();
        csv.AppendLine(bangla
            ? "রিসোর্স,বর্তমান নাম,ঘটনা,সংস্করণ,সময় (UTC),ব্যবহারকারী আইডি"
            : "Resource,Current title,Event,Version,Occurred UTC,Actor user ID");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", new[] {
                Cell(row.ResourceReference.ToString("D")), Cell(row.CurrentTitle),
                Cell(row.Action), Cell(row.VersionNo.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.OccurredAtUtc.ToString("O")),
                Cell(row.ActorUserId?.ToString(System.Globalization.CultureInfo.InvariantCulture))
            }));
        var bytes = new UTF8Encoding(false).GetBytes(csv.ToString());
        var bom = new UTF8Encoding(true).GetPreamble();
        var result = new byte[bom.Length + bytes.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(bytes, 0, result, bom.Length, bytes.Length);
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
