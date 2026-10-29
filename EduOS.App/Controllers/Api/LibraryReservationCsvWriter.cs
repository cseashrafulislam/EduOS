using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Privacy-safe CSV for a freshly authorized, bounded student reservation page.</summary>
public static class LibraryReservationCsvWriter
{
    public static byte[] Write(IReadOnlyList<BookReservationDto> rows, bool bangla)
    {
        if (rows.Count > 100) throw new ArgumentOutOfRangeException(nameof(rows));
        var output = new StringBuilder();
        Append(output, bangla
            ? ["বই", "সংরক্ষণের সময়", "মেয়াদ", "অবস্থা"]
            : ["Book", "Reserved at (UTC)", "Expires at (UTC)", "Status"]);
        foreach (var row in rows)
        {
            var status = row.State switch
            {
                BookReservationState.Pending => bangla ? "অপেক্ষমাণ" : "Pending",
                BookReservationState.Fulfilled => bangla ? "সম্পন্ন" : "Fulfilled",
                BookReservationState.Cancelled => bangla ? "বাতিল" : "Cancelled",
                BookReservationState.Expired => bangla ? "মেয়াদোত্তীর্ণ" : "Expired",
                _ => bangla ? "অন্যান্য" : "Other"
            };
            Append(output, [
                row.BookTitle,
                row.ReservedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                row.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
                status
            ]);
        }
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
            var trimmed = text.TrimStart(' ', '\t', '\r', '\n');
            if (trimmed.Length > 0 && (trimmed[0] is '=' or '+' or '-' or '@'))
                text = "'" + text;
            else if (text.Length > 0 && (text[0] is '\t' or '\r' or '\n'))
                text = "'" + text;
            output.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        }
        output.Append("\r\n");
    }
}
